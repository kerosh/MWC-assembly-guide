// Sandboxed "what-if" interpreter for PlayMaker FSMs.
// It walks states/actions of a real PlayMakerFSM but never executes the game's actions:
// every value it reads goes into a private variable store, every write stays in that store,
// and hashtable writes go to a private overlay. So the game is never affected.
//
// Used for:
//  - socket "Allow install?" state  -> can this socket take a part right now (and why not)
//  - museum inspection "Inspect" FSM -> would the held part pass the historical (VIN) check
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AssemblyGuide
{
    using HutongGames.PlayMaker;
    using HutongGames.PlayMaker.Actions;

    // Where a value came from. Higher = "stronger" when combined.
    public enum Src { Plain = 0, Table = 1, Other = 2, Target = 3 }

    public class SVal
    {
        public object V;
        public Src S;
        public GameObject From;   // object the value was read from (socket / part), for reasons
        public string Var;        // variable name it was read from (e.g. "Type", "Code")
        public SVal(object v, Src s, GameObject from) { V = v; S = s; From = from; }
    }

    // A branching action that ran (for traces, reasons and forks)
    public class SimDecision
    {
        public string State;
        public string Action;
        public string Text;
        public string Event;      // event that was taken (null = none)
        public Src S;
        public List<GameObject> TrueSources = new List<GameObject>();   // bool inputs that were true
        public List<GameObject> FalseSources = new List<GameObject>();  // bool inputs that were false
        public List<string> TrueVars = new List<string>(), FalseVars = new List<string>();   // which variable each source was read from
        public bool HasCmp, Eq;   // equality compare: target value vs reference value
        public bool HasTarget, HasOther;   // inputs include the held part / other parts (mixed checks fork on the other parts)
        public string Var, Val, Ref;
    }

    public class SimState
    {
        public Dictionary<string, SVal> Vars = new Dictionary<string, SVal>();
        public Dictionary<string, Hashtable> Tables = new Dictionary<string, Hashtable>(); // overlay, key = goId:reference
        public string State;
        public int ActionIndex;
        public bool HasPending;
        public string Pending;            // event chosen by a fork, applied when the state loop resumes
        public bool TargetSeen;
        public bool Ended;
        public bool Aborted;
        public int Steps;
        public string ExitState;          // set when a stop-on-exit run leaves its first state
        public string ExitEvent;
        public List<SimDecision> Trace = new List<SimDecision>();
        public List<string> TaintedStates = new List<string>();

        public SimState Clone()
        {
            SimState c = new SimState();
            foreach (KeyValuePair<string, SVal> kv in Vars) c.Vars[kv.Key] = kv.Value;   // SVal treated as immutable
            foreach (KeyValuePair<string, Hashtable> kv in Tables) c.Tables[kv.Key] = (Hashtable)kv.Value.Clone();
            c.State = State; c.ActionIndex = ActionIndex; c.HasPending = HasPending; c.Pending = Pending;
            c.TargetSeen = TargetSeen; c.Ended = Ended; c.Aborted = Aborted; c.Steps = Steps;
            c.ExitState = ExitState; c.ExitEvent = ExitEvent;
            c.Trace = new List<SimDecision>(Trace);
            c.TaintedStates = new List<string>(TaintedStates);
            return c;
        }
    }

    public class FsmSim
    {
        public PlayMakerFSM Pm;
        public GameObject OverrideSocket;   // what-if: this socket's Data.ActivePart = OverridePart, Installed = true
        public GameObject OverridePart;     // the part in the player's hands
        public bool Wildcards;              // decisions on Other-sourced values are forked (best case for the target)
        public bool StopOnExit;             // stop as soon as the first state is left
        public bool Verbose;
        public int MaxSteps = 6000;
        public int MaxForks = 200;
        public static string[] IgnoredFsms = new string[] { "Paint", "Upholstery" }; // TODO: paint / upholstery colour checks

        public List<string> Unknown = new List<string>();     // unsupported actions met (with events)
        public List<string> Log = new List<string>();
        public Func<SimState, int> Score;                     // used to choose between forks

        Dictionary<string, FsmState> states = new Dictionary<string, FsmState>();
        Dictionary<string, Dictionary<string, string>> trans = new Dictionary<string, Dictionary<string, string>>();
        HashSet<string> preTargetForked = new HashSet<string>();
        HashSet<string> neighborhood;     // states where post-target forks are allowed
        int forks;

        public FsmSim(PlayMakerFSM pm)
        {
            Pm = pm;
            foreach (FsmState s in pm.FsmStates)
            {
                states[s.Name] = s;
                Dictionary<string, string> t = new Dictionary<string, string>();
                foreach (FsmTransition tr in s.Transitions)
                    if (!string.IsNullOrEmpty(tr.EventName) && !t.ContainsKey(tr.EventName)) t[tr.EventName] = tr.ToState;
                trans[s.Name] = t;
            }
        }

        public bool HasState(string name) { return states.ContainsKey(name); }
        public FsmState GetState(string name) { FsmState s; return states.TryGetValue(name, out s) ? s : null; }
        public Dictionary<string, string> Transitions(string state)
        {
            Dictionary<string, string> t; return trans.TryGetValue(state, out t) ? t : new Dictionary<string, string>();
        }

        public static FsmStateAction[] ActionsOf(FsmState s)
        {
            try { return s.Actions ?? new FsmStateAction[0]; }
            catch (Exception) { return new FsmStateAction[0]; }
        }

        // Target of the FSM's global transition (where a "start" event leads), else the start state
        public string GlobalStart()
        {
            FsmTransition[] g = Pm.FsmGlobalTransitions;
            if (g != null && g.Length > 0 && !string.IsNullOrEmpty(g[0].ToState)) return g[0].ToState;
            return Pm.Fsm.StartState;
        }

        // States within 'depth' transitions of the given states
        public HashSet<string> Around(IEnumerable<string> from, int depth)
        {
            HashSet<string> res = new HashSet<string>();
            List<string> frontier = new List<string>(from);
            foreach (string s in frontier) res.Add(s);
            for (int d = 0; d < depth; d++)
            {
                List<string> next = new List<string>();
                foreach (string s in frontier)
                    foreach (string to in Transitions(s).Values)
                        if (res.Add(to)) next.Add(to);
                frontier = next;
            }
            return res;
        }

        // ---------------------------------------------------------------- running
        public SimState Run(string startState)
        {
            SimState st = new SimState();
            st.State = startState;
            preTargetForked.Clear();
            forks = 0;
            Loop(st);
            return st;
        }

        void Loop(SimState st)
        {
            while (!st.Ended && !st.Aborted)
            {
                if (++st.Steps > MaxSteps) { st.Aborted = true; Note("aborted: step limit"); return; }
                FsmState s;
                if (!states.TryGetValue(st.State, out s)) { st.Ended = true; return; }
                Dictionary<string, string> t = trans[s.Name];
                string fired = null;

                if (st.HasPending)
                {
                    st.HasPending = false;
                    if (st.Pending != null && t.ContainsKey(st.Pending)) fired = st.Pending;
                }

                if (fired == null)
                {
                    FsmStateAction[] acts = ActionsOf(s);
                    for (; st.ActionIndex < acts.Length; st.ActionIndex++)
                    {
                        FsmStateAction a = acts[st.ActionIndex];
                        if (a == null || !a.Enabled) continue;
                        SimDecision d;
                        List<string> alts;
                        string ev = Exec(st, s.Name, a, out d, out alts);
                        if (d != null)
                        {
                            d.State = s.Name;
                            bool hasT = d.HasTarget || d.S == Src.Target, hasO = d.HasOther || d.S == Src.Other;
                            if (hasT && !st.TaintedStates.Contains(s.Name)) st.TaintedStates.Add(s.Name);
                            if (hasT) st.TargetSeen = true;
                            // decisions that depend on other parts fork - also when the held part is one of the inputs
                            // (e.g. "dashboard + covers + glovebox all installed" while holding a cover)
                            if (Wildcards && hasO && alts != null && alts.Count > 1 && forks < MaxForks)
                            {
                                if (TryFork(st, s.Name, alts, d)) return;   // st now holds the chosen branch's final state
                            }
                            d.Event = ev;
                            st.Trace.Add(d);
                        }
                        if (ev != null && t.ContainsKey(ev)) { fired = ev; break; }
                    }
                    if (fired == null)
                    {
                        if (t.ContainsKey("FINISHED")) fired = "FINISHED";
                        else { st.Ended = true; return; }
                    }
                }

                string to = t[fired];
                if (Verbose) Note("  " + s.Name + " --" + fired + "--> " + to);
                if (StopOnExit) { st.ExitState = to; st.ExitEvent = fired; st.Ended = true; return; }
                st.State = to;
                st.ActionIndex = 0;
            }
        }

        // Fork on a decision whose inputs come from other (non-target) parts: assume the best case for the target.
        bool TryFork(SimState st, string state, List<string> alts, SimDecision d)
        {
            string key = state + "#" + st.ActionIndex;
            bool pre = !st.TargetSeen;
            if (pre)
            {
                if (preTargetForked.Contains(key)) return false;
                preTargetForked.Add(key);
            }
            else
            {
                if (neighborhood == null)
                    neighborhood = Around(st.TaintedStates, 2);
                else foreach (string s2 in Around(st.TaintedStates, 2)) neighborhood.Add(s2);
                if (!neighborhood.Contains(state)) return false;
            }

            SimState best = null; int bestScore = int.MinValue;
            foreach (string alt in alts)
            {
                forks++;
                SimState c = st.Clone();
                SimDecision dc = new SimDecision();
                dc.State = state; dc.Action = d.Action; dc.Text = d.Text + " [assumed]"; dc.Event = alt; dc.S = d.S;
                c.Trace.Add(dc);
                c.ActionIndex = c.ActionIndex + 1;
                c.HasPending = true; c.Pending = alt;
                Loop(c);
                int sc = (pre ? (c.TargetSeen ? 1000 : 0) : 0) + (Score != null ? Score(c) : 0);
                if (sc > bestScore) { bestScore = sc; best = c; }
            }
            if (best == null) return false;
            Copy(best, st);
            return true;
        }

        static void Copy(SimState from, SimState to)
        {
            to.Vars = from.Vars; to.Tables = from.Tables; to.State = from.State; to.ActionIndex = from.ActionIndex;
            to.HasPending = from.HasPending; to.Pending = from.Pending; to.TargetSeen = from.TargetSeen;
            to.Ended = from.Ended; to.Aborted = from.Aborted; to.Steps = from.Steps;
            to.ExitState = from.ExitState; to.ExitEvent = from.ExitEvent;
            to.Trace = from.Trace; to.TaintedStates = from.TaintedStates;
        }

        void Note(string s) { if (Log.Count < 2000) Log.Add(s); }

        // ---------------------------------------------------------------- values
        static Src Max(Src a, Src b) { return (int)a >= (int)b ? a : b; }

        SVal Read(SimState st, NamedVariable nv)
        {
            if (nv == null) return new SVal(null, Src.Plain, null);
            if (nv.UseVariable && !string.IsNullOrEmpty(nv.Name))
            {
                SVal v;
                if (st.Vars.TryGetValue(nv.Name, out v)) return v;
                NamedVariable real = Pm.FsmVariables.GetVariable(nv.Name);
                return new SVal(Raw(real), Src.Plain, null);
            }
            return new SVal(Raw(nv), Src.Plain, null);
        }

        static object Raw(NamedVariable nv)
        {
            if (nv == null) return null;
            if (nv is FsmBool) return ((FsmBool)nv).Value;
            if (nv is FsmInt) return ((FsmInt)nv).Value;
            if (nv is FsmFloat) return ((FsmFloat)nv).Value;
            if (nv is FsmString) return ((FsmString)nv).Value;
            if (nv is FsmGameObject) return ((FsmGameObject)nv).Value;
            if (nv is FsmObject) return ((FsmObject)nv).Value;
            return null;
        }

        static void Write(SimState st, NamedVariable nv, SVal v)
        {
            if (nv == null || !nv.UseVariable || string.IsNullOrEmpty(nv.Name)) return;
            st.Vars[nv.Name] = v;
        }

        public static bool B(object o) { return o is bool && (bool)o; }
        public static int I(object o) { if (o is int) return (int)o; if (o is float) return (int)(float)o; return 0; }
        public static float F(object o) { if (o is float) return (float)o; if (o is int) return (int)o; return 0f; }
        public static string S(object o) { return o == null ? "" : o.ToString(); }
        static GameObject G(object o) { return o as GameObject; }
        static string En(FsmEvent e) { return (e == null || string.IsNullOrEmpty(e.Name)) ? null : e.Name; }

        GameObject Owner(SimState st, FsmOwnerDefault od, out Src src)
        {
            src = Src.Plain;
            if (od == null) return null;
            if (od.OwnerOption == OwnerDefaultOption.UseOwner) return Pm.gameObject;
            SVal v = Read(st, od.GameObject);
            src = v.S;
            return G(v.V);
        }

        // Classify a read from another FSM and apply what-if overrides
        SVal ReadOther(SimState st, GameObject go, string fsmName, string varName, System.Type want)
        {
            if (OverrideSocket != null && go == OverrideSocket && fsmName == "Data")
            {
                st.TargetSeen = true;
                if (varName == "ActivePart") return new SVal(OverridePart, Src.Target, go);
                if (varName == "Installed") return new SVal(true, Src.Target, go);
            }
            Src src = Src.Other;
            if (OverridePart != null && (go == OverridePart || go.transform.IsChildOf(OverridePart.transform)))
            {
                src = Src.Target;
                foreach (string ig in IgnoredFsms) if (ig == fsmName) src = Src.Other;
                if (src == Src.Target) st.TargetSeen = true;
            }
            PlayMakerFSM f = ActionHelpers.GetGameObjectFsm(go, fsmName);
            if (f == null) return null;
            object val = null;
            if (want == typeof(bool)) { FsmBool x = f.FsmVariables.GetFsmBool(varName); if (x == null) return null; val = x.Value; }
            else if (want == typeof(int)) { FsmInt x = f.FsmVariables.GetFsmInt(varName); if (x == null) return null; val = x.Value; }
            else if (want == typeof(float)) { FsmFloat x = f.FsmVariables.GetFsmFloat(varName); if (x == null) return null; val = x.Value; }
            else if (want == typeof(string)) { FsmString x = f.FsmVariables.GetFsmString(varName); if (x == null) return null; val = x.Value; }
            else if (want == typeof(GameObject)) { FsmGameObject x = f.FsmVariables.GetFsmGameObject(varName); if (x == null) return null; val = x.Value; }
            SVal res = new SVal(val, src, go);
            res.Var = varName;
            return res;
        }

        void GetFsmX(SimState st, FsmOwnerDefault od, FsmString fsmName, FsmString varName, NamedVariable store, System.Type want)
        {
            Src osrc;
            GameObject go = Owner(st, od, out osrc);
            if (go == null)
            {
                // the object to read from is missing (e.g. empty socket): the real action keeps the old value.
                // That stale value says nothing about the missing part -> "unknown" (Other), even if it was
                // read from the held part earlier (otherwise e.g. the brake check blames the exhaust manifold).
                if (osrc == Src.Other || osrc == Src.Target)
                {
                    SVal old = Read(st, store);
                    SVal stale = new SVal(old.V, Src.Other, null);
                    stale.Var = old.Var;
                    Write(st, store, stale);
                }
                return;
            }
            SVal v = ReadOther(st, go, S(Read(st, fsmName).V), S(Read(st, varName).V), want);
            if (v != null) Write(st, store, v);
        }

        Hashtable Table(SimState st, GameObject go, string reference, bool forWrite, out bool overlay)
        {
            overlay = false;
            if (go == null) return null;
            string k = go.GetInstanceID() + ":" + reference;
            Hashtable h;
            if (st.Tables.TryGetValue(k, out h)) { overlay = true; return h; }
            if (forWrite) { h = new Hashtable(); st.Tables[k] = h; overlay = true; return h; }
            foreach (PlayMakerHashTableProxy p in go.GetComponents<PlayMakerHashTableProxy>())
                if (p.referenceName == reference) return p.hashTable;
            return null;
        }

        // all values written to overlay tables (e.g. inspection "Results")
        public static object TableValue(SimState st, string key)
        {
            foreach (Hashtable h in st.Tables.Values) if (h.ContainsKey(key)) return h[key];
            return null;
        }

        SVal FromFsmVar(SimState st, FsmVar fv)
        {
            if (fv == null) return new SVal(null, Src.Plain, null);
            if (fv.useVariable && !string.IsNullOrEmpty(fv.variableName))
            {
                SVal v;
                if (st.Vars.TryGetValue(fv.variableName, out v)) return v;
                return new SVal(Raw(Pm.FsmVariables.GetVariable(fv.variableName)), Src.Plain, null);
            }
            switch (fv.Type)
            {
                case VariableType.Bool: return new SVal(fv.boolValue, Src.Plain, null);
                case VariableType.Int: return new SVal(fv.intValue, Src.Plain, null);
                case VariableType.Float: return new SVal(fv.floatValue, Src.Plain, null);
                case VariableType.String: return new SVal(fv.stringValue, Src.Plain, null);
                default: return new SVal(fv.objectReference, Src.Plain, null);
            }
        }

        static void AddAlt(List<string> l, string e) { if (!l.Contains(e)) l.Add(e); }

        // ---------------------------------------------------------------- actions
        // Returns the event the action fires (null = none). d/alts are set for branching actions.
        string Exec(SimState st, string state, FsmStateAction a, out SimDecision d, out List<string> alts)
        {
            d = null; alts = null;
            string name = a.GetType().Name;

            // ---- reads from other FSMs
            if (a is GetFsmBool) { GetFsmBool x = (GetFsmBool)a; GetFsmX(st, x.gameObject, x.fsmName, x.variableName, x.storeValue, typeof(bool)); return null; }
            if (a is GetFsmInt) { GetFsmInt x = (GetFsmInt)a; GetFsmX(st, x.gameObject, x.fsmName, x.variableName, x.storeValue, typeof(int)); return null; }
            if (a is GetFsmFloat) { GetFsmFloat x = (GetFsmFloat)a; GetFsmX(st, x.gameObject, x.fsmName, x.variableName, x.storeValue, typeof(float)); return null; }
            if (a is GetFsmString) { GetFsmString x = (GetFsmString)a; GetFsmX(st, x.gameObject, x.fsmName, x.variableName, x.storeValue, typeof(string)); return null; }
            if (a is GetFsmGameObject) { GetFsmGameObject x = (GetFsmGameObject)a; GetFsmX(st, x.gameObject, x.fsmName, x.variableName, x.storeValue, typeof(GameObject)); return null; }

            // ---- hashtables
            if (a is HashTableGet)
            {
                HashTableGet x = (HashTableGet)a;
                Src os; GameObject go = Owner(st, x.gameObject, out os);
                bool ov;
                Hashtable h = Table(st, go, S(Read(st, x.reference).V), false, out ov);
                string key = S(Read(st, x.key).V);
                d = Dec("HashTableGet", Src.Table, "VIN/table '" + key + "'");
                if (h == null || !h.ContainsKey(key)) return En(x.KeyNotFoundEvent);
                if (x.result != null && x.result.useVariable && !string.IsNullOrEmpty(x.result.variableName))
                    st.Vars[x.result.variableName] = new SVal(h[key], Src.Table, go);
                d.Text = "table '" + key + "' = " + S(h[key]);
                return En(x.KeyFoundEvent);
            }
            if (a is HashTableSet)
            {
                HashTableSet x = (HashTableSet)a;
                Src os; GameObject go = Owner(st, x.gameObject, out os);
                bool ov;
                Hashtable h = Table(st, go, S(Read(st, x.reference).V), true, out ov);
                if (h != null) h[S(Read(st, x.key).V)] = FromFsmVar(st, x.variable).V;
                return null;
            }

            // ---- plain variable operations
            if (a is SetBoolValue) { SetBoolValue x = (SetBoolValue)a; Write(st, x.boolVariable, Read(st, x.boolValue)); return null; }
            if (a is SetIntValue) { SetIntValue x = (SetIntValue)a; Write(st, x.intVariable, Read(st, x.intValue)); return null; }
            if (a is SetFloatValue) { SetFloatValue x = (SetFloatValue)a; Write(st, x.floatVariable, Read(st, x.floatValue)); return null; }
            if (a is SetStringValue) { SetStringValue x = (SetStringValue)a; Write(st, x.stringVariable, Read(st, x.stringValue)); return null; }
            if (a is BoolFlip) { BoolFlip x = (BoolFlip)a; SVal v = Read(st, x.boolVariable); Write(st, x.boolVariable, new SVal(!B(v.V), v.S, v.From)); return null; }
            if (a is FloatAddMutiple)
            {
                FloatAddMutiple x = (FloatAddMutiple)a;
                SVal acc = Read(st, x.addTo); float f = F(acc.V); Src s = acc.S;
                if (x.floatVariables != null) foreach (FsmFloat ff in x.floatVariables) { SVal v = Read(st, ff); f += F(v.V); s = Max(s, v.S); }
                Write(st, x.addTo, new SVal(f, s, null));
                return null;
            }
            if (a is BuildStringFast)
            {
                BuildStringFast x = (BuildStringFast)a;
                StringBuilder sb = new StringBuilder(); Src s = Src.Plain;
                string sep = S(Read(st, x.separator).V);
                if (x.stringParts != null)
                    for (int i = 0; i < x.stringParts.Length; i++)
                    {
                        SVal v = Read(st, x.stringParts[i]); s = Max(s, v.S);
                        sb.Append(S(v.V));
                        if (i < x.stringParts.Length - 1) sb.Append(sep);
                    }
                if (B(Read(st, x.addToEnd).V)) sb.Append(sep);
                Write(st, x.storeResult, new SVal(sb.ToString(), s, null));
                return null;
            }

            // ---- decisions
            if (a is BoolTest)
            {
                BoolTest x = (BoolTest)a; SVal v = Read(st, x.boolVariable);
                d = Dec("BoolTest", v.S, Who(v) + " = " + B(v.V));
                Src1(d, v);
                alts = new List<string>(); AddAlt(alts, En(x.isTrue)); AddAlt(alts, En(x.isFalse));
                return B(v.V) ? En(x.isTrue) : En(x.isFalse);
            }
            if (a is BoolAllTrue || a is BoolAnyTrue || a is BoolNoneTrue)
            {
                FsmBool[] vars; FsmEvent sendEvent; FsmBool store; string kind;
                if (a is BoolAllTrue) { BoolAllTrue x = (BoolAllTrue)a; vars = x.boolVariables; sendEvent = x.sendEvent; store = x.storeResult; kind = "all"; }
                else if (a is BoolAnyTrue) { BoolAnyTrue x = (BoolAnyTrue)a; vars = x.boolVariables; sendEvent = x.sendEvent; store = x.storeResult; kind = "any"; }
                else { BoolNoneTrue x = (BoolNoneTrue)a; vars = x.boolVariables; sendEvent = x.sendEvent; store = x.storeResult; kind = "none"; }
                if (vars == null || vars.Length == 0) return null;
                Src s = Src.Plain; int trues = 0; bool assumed = false;
                d = Dec(name, Src.Plain, "");
                StringBuilder sb = new StringBuilder();
                foreach (FsmBool fb in vars)
                {
                    SVal v = Read(st, fb); s = Max(s, v.S);
                    bool bv = B(v.V);
                    // what-if: other parts are assumed right ("installed" for all/any, "not installed" for none).
                    // Needed because some checks don't branch but store the result (hubcaps: all four installed -> Results)
                    if (Wildcards && v.S == Src.Other) { bv = kind != "none"; assumed = true; }
                    if (bv) trues++;
                    Src1(d, v);
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Who(v)).Append('=').Append(bv);
                }
                bool res = kind == "all" ? trues == vars.Length : kind == "any" ? trues > 0 : trues == 0;
                d.S = s; d.Text = kind + "-true(" + sb + ") -> " + res + (assumed ? " [others assumed]" : "");
                Write(st, store, new SVal(res, assumed ? Src.Plain : s, null));
                if (assumed) { d.HasOther = false; d.S = d.HasTarget ? Src.Target : Src.Plain; return res ? En(sendEvent) : null; }   // decided, no fork needed
                alts = new List<string>(); AddAlt(alts, En(sendEvent)); AddAlt(alts, null);
                return res ? En(sendEvent) : null;
            }
            if (a is StringCompare)
            {
                StringCompare x = (StringCompare)a;
                SVal v1 = Read(st, x.stringVariable), v2 = Read(st, x.compareTo);
                bool eq = S(v1.V) == S(v2.V);
                d = Dec("StringCompare", Max(v1.S, v2.S), Who(v1) + " '" + S(v1.V) + "' vs '" + S(v2.V) + "'");
                Cmp(d, v1, v2, S(v1.V), S(v2.V), eq); Mark(d, v1); Mark(d, v2);
                Write(st, x.storeResult, new SVal(eq, d.S, null));
                alts = new List<string>(); AddAlt(alts, En(x.equalEvent)); AddAlt(alts, En(x.notEqualEvent));
                return eq ? En(x.equalEvent) : En(x.notEqualEvent);
            }
            if (a is StringSwitch)
            {
                StringSwitch x = (StringSwitch)a; SVal v = Read(st, x.stringVariable);
                d = Dec("StringSwitch", v.S, Who(v) + " '" + S(v.V) + "'"); Mark(d, v);
                alts = new List<string>(); string hit = null;
                if (x.compareTo != null)
                    for (int i = 0; i < x.compareTo.Length && i < x.sendEvent.Length; i++)
                    {
                        AddAlt(alts, En(x.sendEvent[i]));
                        if (hit == null && S(Read(st, x.compareTo[i]).V) == S(v.V)) hit = En(x.sendEvent[i]);
                    }
                AddAlt(alts, null);
                return hit;
            }
            if (a is IntCompare)
            {
                IntCompare x = (IntCompare)a;
                SVal v1 = Read(st, x.integer1), v2 = Read(st, x.integer2);
                int i1 = I(v1.V), i2 = I(v2.V);
                d = Dec("IntCompare", Max(v1.S, v2.S), Who(v1) + " " + i1 + " vs " + i2);
                Cmp(d, v1, v2, i1.ToString(), i2.ToString(), i1 == i2); Mark(d, v1); Mark(d, v2);
                alts = new List<string>(); AddAlt(alts, En(x.equal)); AddAlt(alts, En(x.lessThan)); AddAlt(alts, En(x.greaterThan));
                return i1 == i2 ? En(x.equal) : i1 < i2 ? En(x.lessThan) : En(x.greaterThan);
            }
            if (a is IntSwitch)
            {
                IntSwitch x = (IntSwitch)a; SVal v = Read(st, x.intVariable);
                d = Dec("IntSwitch", v.S, Who(v) + " " + I(v.V)); Mark(d, v);
                alts = new List<string>(); string hit = null;
                if (x.compareTo != null)
                    for (int i = 0; i < x.compareTo.Length && i < x.sendEvent.Length; i++)
                    {
                        AddAlt(alts, En(x.sendEvent[i]));
                        if (hit == null && I(Read(st, x.compareTo[i]).V) == I(v.V)) hit = En(x.sendEvent[i]);
                    }
                AddAlt(alts, null);
                return hit;
            }
            if (a is FloatCompare)
            {
                FloatCompare x = (FloatCompare)a;
                SVal v1 = Read(st, x.float1), v2 = Read(st, x.float2);
                float f1 = F(v1.V), f2 = F(v2.V), tol = F(Read(st, x.tolerance).V);
                d = Dec("FloatCompare", Max(v1.S, v2.S), Who(v1) + " " + f1 + " vs " + f2); Mark(d, v1); Mark(d, v2);
                alts = new List<string>(); AddAlt(alts, En(x.equal)); AddAlt(alts, En(x.lessThan)); AddAlt(alts, En(x.greaterThan));
                if (Mathf.Abs(f1 - f2) <= tol) return En(x.equal);
                return f1 < f2 ? En(x.lessThan) : En(x.greaterThan);
            }
            if (a is Wait)
            {
                return En(((Wait)a).finishEvent);
            }

            // ---- known side-effect-only actions (ignored in the sandbox)
            switch (name)
            {
                case "SendEventByName": case "SendEvent": case "ActivateGameObject": case "SetProperty": case "GetProperty":
                case "SetFsmBool": case "SetFsmInt": case "SetFsmFloat": case "SetFsmString": case "SetFsmGameObject":
                case "SetName": case "SetScale": case "SetTag": case "SetLayer": case "SetPosition": case "SetRotation":
                case "PlaySound": case "AudioPlay": case "SetMaterialColor": case "SetMass": case "SetIsKinematic":
                case "DebugLog": case "SetGameObject":
                    return null;
            }
            // unknown action: only matters if it can send events
            foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields())
            {
                if (fi.FieldType == typeof(FsmEvent))
                {
                    FsmEvent e = fi.GetValue(a) as FsmEvent;
                    if (En(e) != null)
                    {
                        string u = name + " in '" + state + "'";
                        if (!Unknown.Contains(u)) Unknown.Add(u);
                        break;
                    }
                }
            }
            return null;
        }

        static SimDecision Dec(string action, Src s, string text)
        {
            SimDecision d = new SimDecision(); d.Action = action; d.S = s; d.Text = text; return d;
        }

        static void Cmp(SimDecision d, SVal v1, SVal v2, string s1, string s2, bool eq)
        {
            d.HasCmp = true; d.Eq = eq;
            if (v2.S == Src.Target && v1.S != Src.Target) { d.Var = v2.Var; d.Val = s2; d.Ref = s1; }
            else { d.Var = v1.Var; d.Val = s1; d.Ref = s2; }
        }

        static void Mark(SimDecision d, SVal v)
        {
            if (v == null) return;
            if (v.S == Src.Target) d.HasTarget = true;
            else if (v.S == Src.Other) d.HasOther = true;
        }

        static void Src1(SimDecision d, SVal v)
        {
            Mark(d, v);
            if (v.From == null) return;
            if (B(v.V)) { d.TrueSources.Add(v.From); d.TrueVars.Add(v.Var ?? "Installed"); }
            else { d.FalseSources.Add(v.From); d.FalseVars.Add(v.Var ?? "Installed"); }
        }

        static string Who(SVal v)
        {
            if (v.From == null) return "";
            return v.From.name;
        }
    }
}

// Historical ("museum") inspection what-if: would the held part pass the VIN check in its socket?
// Runs the game's own inspection FSM in the sandbox (FsmSim), with the held part placed in the socket.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssemblyGuide
{
    using HutongGames.PlayMaker;
    using HutongGames.PlayMaker.Actions;

    public class VinVerdict
    {
        public bool Checked;        // the inspection looks at this socket/part at all
        public bool Pass = true;
        public List<string> FailKeys = new List<string>();
        public List<string> Reasons = new List<string>();
        public List<string> Unknown = new List<string>();
        public List<string> Log;
        public string Note = "";
        public long Ms;
    }

    public static class VinCheck
    {
        public static PlayMakerFSM Inspect;
        public static string Start;
        public static string Status = "not initialised";
        public static List<string> MustNotBeInstalled = new List<string>();   // tuning parts (fail the check when installed)
        static Dictionary<string, List<string>> keysSet = new Dictionary<string, List<string>>();     // state -> keys written
        static Dictionary<string, List<string>> affected = new Dictionary<string, List<string>>();    // state -> keys it decides

        public static void Init()
        {
            Inspect = null; MustNotBeInstalled.Clear(); keysSet.Clear(); affected.Clear();
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                if (f.FsmName == "Inspect" && f.gameObject.name == "InspectionMuseum" && f.gameObject.transform.root.name == "INSPECTION") { Inspect = f; break; }
            if (Inspect == null) { Status = "museum inspection FSM not found - VIN check off"; return; }

            FsmSim sim = new FsmSim(Inspect);
            Start = sim.GlobalStart();
            foreach (FsmState s in Inspect.FsmStates)
            {
                List<string> keys = new List<string>();
                Dictionary<string, string> installedVars = new Dictionary<string, string>();
                foreach (FsmStateAction a in FsmSim.ActionsOf(s))
                {
                    HashTableSet hs = a as HashTableSet;
                    if (hs != null && hs.key != null && !hs.key.UseVariable && !keys.Contains(hs.key.Value)) keys.Add(hs.key.Value);
                    GetFsmBool g = a as GetFsmBool;
                    if (g != null && g.variableName != null && g.variableName.Value == "Installed" && g.storeValue != null)
                    {
                        GameObject t = Owner(g.gameObject);
                        if (t != null) installedVars[g.storeValue.Name] = t.name;
                    }
                    BoolNoneTrue n = a as BoolNoneTrue;
                    if (n != null && n.boolVariables != null)
                        foreach (FsmBool b in n.boolVariables)
                            if (b != null && installedVars.ContainsKey(b.Name) && !MustNotBeInstalled.Contains(installedVars[b.Name]))
                                MustNotBeInstalled.Add(installedVars[b.Name]);
                }
                keysSet[s.Name] = keys;
            }
            // BoolNoneTrue may sit in the state after the reads: second pass across one transition
            foreach (FsmState s in Inspect.FsmStates)
            {
                Dictionary<string, string> installedVars = new Dictionary<string, string>();
                foreach (FsmStateAction a in FsmSim.ActionsOf(s))
                {
                    GetFsmBool g = a as GetFsmBool;
                    if (g != null && g.variableName != null && g.variableName.Value == "Installed" && g.storeValue != null)
                    {
                        GameObject t = Owner(g.gameObject);
                        if (t != null) installedVars[g.storeValue.Name] = t.name;
                    }
                }
                if (installedVars.Count == 0) continue;
                foreach (string to in sim.Transitions(s.Name).Values)
                {
                    FsmState ns = sim.GetState(to);
                    if (ns == null) continue;
                    foreach (FsmStateAction a in FsmSim.ActionsOf(ns))
                    {
                        BoolNoneTrue n = a as BoolNoneTrue;
                        if (n == null || n.boolVariables == null) continue;
                        foreach (FsmBool b in n.boolVariables)
                            if (b != null && installedVars.ContainsKey(b.Name) && !MustNotBeInstalled.Contains(installedVars[b.Name]))
                                MustNotBeInstalled.Add(installedVars[b.Name]);
                    }
                }
            }
            // which result keys a state decides: its own writes, else 1 hop, else 2 hops
            foreach (FsmState s in Inspect.FsmStates)
            {
                List<string> k = new List<string>(keysSet[s.Name]);
                List<string> frontier = new List<string>(); frontier.Add(s.Name);
                for (int hop = 0; hop < 2 && k.Count == 0; hop++)
                {
                    List<string> nf = new List<string>();
                    foreach (string st in frontier)
                        foreach (string to in sim.Transitions(st).Values)
                        {
                            nf.Add(to);
                            List<string> ks;
                            if (keysSet.TryGetValue(to, out ks)) foreach (string x in ks) if (!k.Contains(x)) k.Add(x);
                        }
                    frontier = nf;
                }
                affected[s.Name] = k;
            }
            Status = "museum inspection: " + Inspect.FsmStates.Length + " states, start '" + Start + "', " + MustNotBeInstalled.Count + " tuning sockets";
        }

        static GameObject Owner(FsmOwnerDefault od)
        {
            if (od == null) return null;
            if (od.OwnerOption == OwnerDefaultOption.UseOwner) return Inspect.gameObject;
            FsmGameObject g = od.GameObject;
            if (g == null) return null;
            if (g.UseVariable && !string.IsNullOrEmpty(g.Name)) { FsmGameObject v = Inspect.FsmVariables.GetFsmGameObject(g.Name); return v == null ? null : v.Value; }
            return g.Value;
        }

        static List<string> KeysOf(SimState st)
        {
            List<string> keys = new List<string>();
            foreach (string t in st.TaintedStates)
            {
                List<string> k;
                if (affected.TryGetValue(t, out k)) foreach (string x in k) if (!keys.Contains(x)) keys.Add(x);
            }
            return keys;
        }

        static int Score(SimState st)
        {
            int sc = 0;
            foreach (string k in KeysOf(st))
            {
                object v = FsmSim.TableValue(st, k);
                if (v is bool) sc += (bool)v ? 2 : -1;
            }
            return sc;
        }

        // human-readable reason for a decision on the held part's value
        static string Reason(SimDecision d)
        {
            if (!d.HasCmp || string.IsNullOrEmpty(d.Var)) return d.Text;
            if (d.Eq) return d.Var + " " + d.Val + " is not allowed";          // e.g. forbidden tire type
            return d.Var + " is '" + d.Val + "', VIN needs '" + d.Ref + "'";   // e.g. grille Code 'L' vs 'ALL'
        }

        public static VinVerdict Check(GameObject socket, GameObject part, bool verbose)
        {
            VinVerdict r = new VinVerdict();
            if (Inspect == null) { r.Note = Status; return r; }
            if (MustNotBeInstalled.Contains(socket.name))
            {
                r.Checked = true; r.Pass = false; r.FailKeys.Add("General");
                r.Reasons.Add("tuning part - not allowed for historical registration");
                return r;
            }
            FsmSim sim = new FsmSim(Inspect);
            sim.OverrideSocket = socket; sim.OverridePart = part; sim.Wildcards = true; sim.Verbose = verbose;
            sim.Score = Score;
            SimState st;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            try { st = sim.Run(Start); }
            catch (Exception e) { r.Note = "sim error: " + e.Message; return r; }
            r.Ms = sw.ElapsedMilliseconds;
            r.Unknown = sim.Unknown;
            r.Log = sim.Log;
            if (st.Aborted) r.Note = "inspection sim aborted";
            if (st.TaintedStates.Count == 0) { r.Checked = false; return r; }
            r.Checked = true;
            foreach (string k in KeysOf(st))
            {
                object v = FsmSim.TableValue(st, k);
                if (!(v is bool) || !(bool)v) { r.Pass = false; r.FailKeys.Add(k); }
            }
            if (!r.Pass)
                foreach (SimDecision d in st.Trace)
                    if (d.S == Src.Target && !string.IsNullOrEmpty(d.Text) && r.Reasons.Count < 4)
                    {
                        string why = Reason(d);
                        if (why != null && !r.Reasons.Contains(why)) r.Reasons.Add(why);
                    }
            if (verbose)
                foreach (SimDecision d in st.Trace)
                    r.Log.Add("[" + d.State + "] " + d.Action + " " + d.Text + " -> " + (d.Event ?? "-") + " (" + d.S + ")");
            return r;
        }
    }
}

// Install sockets ("VINP_*" objects with a "Data" FSM), their install rules and the missing-parts list.
// Everything is read from the game's own FSMs at load time - nothing about specific parts is hardcoded.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AssemblyGuide
{
    using HutongGames.PlayMaker;
    using HutongGames.PlayMaker.Actions;

    public class SocketInfo
    {
        public string Name;                                   // e.g. "VINP_Headgasket"
        public string Nice;                                   // e.g. "Headgasket"
        public List<string> Requires = new List<string>();    // must be installed first
        public List<string> RequiresAny = new List<string>(); // at least one of these first
        public List<string> BlockedBy = new List<string>();   // must NOT be installed (this one goes first)
        public bool Optional;                                 // tuning part etc.
        public bool HasRule;                                  // "Allow install?" state found
        public bool ResetPos = true, ResetRot = true;         // how the part is placed on install
        public int Order = 100000;
    }

    public class InstallCheck
    {
        public bool Known;
        public bool Allowed = true;
        public List<string> Blockers = new List<string>();   // remove these first
        public List<string> Needs = new List<string>();      // install these first
        public string Note = "";
    }

    public class MissingItem
    {
        public string Text;
        public bool CanNow;
        public int Order;
        public List<string> WouldBlock = new List<string>();   // still-missing parts this one would block
        public List<string> Names = new List<string>();        // socket names behind this line
        public int Rank { get { return CanNow ? (WouldBlock.Count == 0 ? 0 : 1) : 2; } }
    }

    public static class Sockets
    {
        public const string RuleState = "Allow install?";
        public static Dictionary<string, SocketInfo> Info = new Dictionary<string, SocketInfo>();
        public static List<List<string>> AltGroups = new List<List<string>>();
        public static Dictionary<string, int> AltGroupOf = new Dictionary<string, int>();
        public static List<string> Order = new List<string>();
        public static string Status = "not scanned";

        public static bool IsSocket(PlayMakerFSM f)
        {
            return f != null && f.FsmName == "Data" && f.gameObject.name.StartsWith("VINP_");
        }

        public static PlayMakerFSM DataFsm(GameObject go)
        {
            if (go == null) return null;
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Data") return f;
            return null;
        }

        public static bool Installed(PlayMakerFSM data)
        {
            if (data == null) return false;
            FsmBool b = data.FsmVariables.GetFsmBool("Installed");
            return b != null && b.Value;
        }

        public static string Nice(string socketName)
        {
            string n = socketName.StartsWith("VINP_") ? socketName.Substring(5) : socketName;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(n[i - 1])) { sb.Append(' '); sb.Append(char.ToLower(c)); }
                else if (i > 0 && char.IsDigit(c) && !char.IsDigit(n[i - 1])) { sb.Append(' '); sb.Append(c); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static GameObject OwnerStatic(PlayMakerFSM pm, FsmOwnerDefault od)
        {
            if (od == null) return null;
            if (od.OwnerOption == OwnerDefaultOption.UseOwner) return pm.gameObject;
            FsmGameObject g = od.GameObject;
            if (g == null) return null;
            if (g.UseVariable && !string.IsNullOrEmpty(g.Name))
            {
                FsmGameObject v = pm.FsmVariables.GetFsmGameObject(g.Name);
                return v == null ? null : v.Value;
            }
            return g.Value;
        }

        static bool LeadsAway(FsmSim sim, string state, FsmEvent e, string startState)
        {
            if (e == null || string.IsNullOrEmpty(e.Name)) return false;
            string to;
            return sim.Transitions(state).TryGetValue(e.Name, out to) && to != startState;
        }

        // ---------------------------------------------------------------- scan
        public static void Scan()
        {
            Info.Clear(); AltGroups.Clear(); AltGroupOf.Clear(); Order.Clear();
            int n = 0, rules = 0;
            List<string> seenOrder = new List<string>();

            // Prefer live scene instances; walk the car first so the fallback order follows the car's hierarchy
            List<PlayMakerFSM> all = new List<PlayMakerFSM>();
            GameObject car = GameObject.Find("CORRIS");
            if (car != null) foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true)) if (IsSocket(f)) all.Add(f);
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM))) if (IsSocket(f) && !all.Contains(f)) all.Add(f);

            foreach (PlayMakerFSM f in all)
            {
                string name = f.gameObject.name;
                if (Info.ContainsKey(name)) continue;
                SocketInfo si = Analyze(f);
                Info[name] = si;
                seenOrder.Add(name);
                n++; if (si.HasRule) rules++;
            }

            foreach (string tn in VinCheck.MustNotBeInstalled) { SocketInfo si; if (Info.TryGetValue(tn, out si)) si.Optional = true; }

            // alternatives: two sockets that block each other (manual/auto lever, hood/fibreglass hood ...)
            foreach (SocketInfo a in Info.Values)
                foreach (string b in a.BlockedBy)
                {
                    SocketInfo sb;
                    if (!Info.TryGetValue(b, out sb) || !sb.BlockedBy.Contains(a.Name)) continue;
                    int ga = AltGroupOf.ContainsKey(a.Name) ? AltGroupOf[a.Name] : -1;
                    int gb = AltGroupOf.ContainsKey(b) ? AltGroupOf[b] : -1;
                    if (ga < 0 && gb < 0) { AltGroups.Add(new List<string>()); ga = AltGroups.Count - 1; }
                    int g = ga >= 0 ? ga : gb;
                    if (!AltGroups[g].Contains(a.Name)) AltGroups[g].Add(a.Name);
                    if (!AltGroups[g].Contains(b)) AltGroups[g].Add(b);
                    AltGroupOf[a.Name] = g; AltGroupOf[b] = g;
                }

            BuildOrder(seenOrder);
            Status = n + " sockets, " + rules + " with install rules, " + AltGroups.Count + " alternative groups";
        }

        static SocketInfo Analyze(PlayMakerFSM f)
        {
            SocketInfo si = new SocketInfo();
            si.Name = f.gameObject.name;
            si.Nice = Nice(si.Name);
            Transform p = f.transform;
            while (p != null) { if (p.name == "AssembliesTuning") si.Optional = true; p = p.parent; }

            FsmSim sim;
            try { sim = new FsmSim(f); } catch (Exception) { return si; }
            string start = f.Fsm.StartState;

            // how the part is attached (SetParent in the install states)
            foreach (FsmState s in f.FsmStates)
                foreach (FsmStateAction a in FsmSim.ActionsOf(s))
                {
                    SetParent sp = a as SetParent;
                    if (sp == null || sp.parent == null) continue;
                    if (sp.resetLocalPosition != null) si.ResetPos = sp.resetLocalPosition.Value;
                    if (sp.resetLocalRotation != null) si.ResetRot = sp.resetLocalRotation.Value;
                    break;
                }

            FsmState rule = sim.GetState(RuleState);
            if (rule == null) return si;
            si.HasRule = true;

            Dictionary<string, string> varSrc = new Dictionary<string, string>();   // bool var -> socket name
            foreach (FsmStateAction a in FsmSim.ActionsOf(rule))
            {
                GetFsmBool g = a as GetFsmBool;
                if (g != null)
                {
                    GameObject t = OwnerStatic(f, g.gameObject);
                    if (t != null && g.storeValue != null && !string.IsNullOrEmpty(g.storeValue.Name) && t.name.StartsWith("VINP_"))
                        varSrc[g.storeValue.Name] = t.name;
                    continue;
                }
                BoolNoneTrue none = a as BoolNoneTrue;
                if (none != null && none.boolVariables != null)
                {
                    foreach (FsmBool b in none.boolVariables)
                        if (b != null && varSrc.ContainsKey(b.Name)) Add(si.BlockedBy, varSrc[b.Name]);
                    continue;
                }
                BoolAllTrue allT = a as BoolAllTrue;
                if (allT != null && allT.boolVariables != null)
                {
                    foreach (FsmBool b in allT.boolVariables)
                        if (b != null && varSrc.ContainsKey(b.Name)) Add(si.Requires, varSrc[b.Name]);
                    continue;
                }
                BoolAnyTrue anyT = a as BoolAnyTrue;
                if (anyT != null && anyT.boolVariables != null)
                {
                    foreach (FsmBool b in anyT.boolVariables)
                        if (b != null && varSrc.ContainsKey(b.Name)) Add(si.RequiresAny, varSrc[b.Name]);
                    continue;
                }
                BoolTest bt = a as BoolTest;
                if (bt != null && bt.boolVariable != null && varSrc.ContainsKey(bt.boolVariable.Name))
                {
                    string src = varSrc[bt.boolVariable.Name];
                    if (LeadsAway(sim, RuleState, bt.isTrue, start)) Add(si.Requires, src);
                    else if (LeadsAway(sim, RuleState, bt.isFalse, start)) Add(si.BlockedBy, src);
                    else if (bt.isTrue != null && !string.IsNullOrEmpty(bt.isTrue.Name)) Add(si.BlockedBy, src);   // true -> back to idle
                    else if (bt.isFalse != null && !string.IsNullOrEmpty(bt.isFalse.Name)) Add(si.Requires, src);
                }
            }
            si.Requires.Remove(si.Name); si.BlockedBy.Remove(si.Name); si.RequiresAny.Remove(si.Name);
            return si;
        }

        static void Add(List<string> l, string s) { if (!l.Contains(s)) l.Add(s); }

        // Assembly order: requirements first, a socket before the ones that would block it.
        static void BuildOrder(List<string> natural)
        {
            Dictionary<string, List<string>> next = new Dictionary<string, List<string>>();
            Dictionary<string, int> indeg = new Dictionary<string, int>();
            foreach (string s in natural) { next[s] = new List<string>(); indeg[s] = 0; }
            Action<string, string> edge = delegate (string a, string b)
            {
                if (a == b || !next.ContainsKey(a) || !next.ContainsKey(b) || next[a].Contains(b)) return;
                next[a].Add(b); indeg[b]++;
            };
            foreach (SocketInfo si in Info.Values)
            {
                foreach (string r in si.Requires) edge(r, si.Name);
                foreach (string r in si.RequiresAny) edge(r, si.Name);
                foreach (string b in si.BlockedBy)
                {
                    SocketInfo o;
                    if (Info.TryGetValue(b, out o) && o.BlockedBy.Contains(si.Name)) continue;   // alternatives
                    edge(si.Name, b);
                }
            }
            List<string> done = new List<string>();
            HashSet<string> used = new HashSet<string>();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (string s in natural)          // stable: keep hierarchy order among ready nodes
                {
                    if (used.Contains(s) || indeg[s] > 0) continue;
                    used.Add(s); done.Add(s); progress = true;
                    foreach (string b in next[s]) indeg[b]--;
                    break;
                }
            }
            foreach (string s in natural) if (!used.Contains(s)) done.Add(s);   // cycles: append
            Order = done;
            for (int i = 0; i < done.Count; i++) Info[done[i]].Order = i;
        }

        // ---------------------------------------------------------------- live checks
        public static InstallCheck CheckInstall(PlayMakerFSM data)
        {
            InstallCheck c = new InstallCheck();
            if (data == null) return c;
            FsmSim sim;
            try { sim = new FsmSim(data); } catch (Exception e) { c.Note = e.Message; return c; }
            if (!sim.HasState(RuleState)) { c.Note = "no install rule"; return c; }
            sim.StopOnExit = true;
            SimState st;
            try { st = sim.Run(RuleState); } catch (Exception e) { c.Note = "sim error: " + e.Message; return c; }
            if (st.ExitState == null) { c.Note = "rule did not finish"; return c; }
            c.Known = true;
            c.Allowed = st.ExitState != data.Fsm.StartState;
            if (c.Allowed) return c;
            foreach (SimDecision d in st.Trace)
            {
                if (d.Action == "BoolNoneTrue") foreach (GameObject g in d.TrueSources) Add(c.Blockers, Nice(g.name));
                else if (d.Action == "BoolAllTrue" || d.Action == "BoolAnyTrue") foreach (GameObject g in d.FalseSources) Add(c.Needs, Nice(g.name));
                else if (d.Action == "BoolTest")
                {
                    SocketInfo si; Info.TryGetValue(data.gameObject.name, out si);
                    foreach (GameObject g in d.TrueSources) if (si == null || si.BlockedBy.Contains(g.name)) Add(c.Blockers, Nice(g.name));
                    foreach (GameObject g in d.FalseSources) if (si == null || si.Requires.Contains(g.name)) Add(c.Needs, Nice(g.name));
                }
                else if (d.S == Src.Other || d.S == Src.Plain) c.Note = d.Text;
            }
            return c;
        }

        // still-missing sockets that installing 'name' would block (alternatives excluded)
        public static List<string> WouldBlock(string name, ICollection<string> missing)
        {
            List<string> res = new List<string>();
            SocketInfo me;
            Info.TryGetValue(name, out me);
            foreach (string m in missing)
            {
                SocketInfo o;
                if (m == name || !Info.TryGetValue(m, out o) || !o.BlockedBy.Contains(name)) continue;
                if (me != null && me.BlockedBy.Contains(m)) continue;   // mutual = alternatives
                if (!res.Contains(o.Nice)) res.Add(o.Nice);
            }
            return res;
        }

        // live (active) sockets by name, refreshed every few seconds
        static Dictionary<string, PlayMakerFSM> live = new Dictionary<string, PlayMakerFSM>();
        static float liveAt = -100f;
        public static PlayMakerFSM Live(string name)
        {
            if (Time.time - liveAt > 5f)
            {
                live.Clear();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                    if (IsSocket(f) && f.gameObject.activeInHierarchy) live[f.gameObject.name] = f;
                liveAt = Time.time;
            }
            PlayMakerFSM r;
            return live.TryGetValue(name, out r) && r != null ? r : null;
        }

        // still-missing live sockets anywhere that installing 'name' would block
        public static List<string> WouldBlockLive(string name)
        {
            List<string> missing = new List<string>();
            foreach (SocketInfo o in Info.Values)
            {
                if (!o.BlockedBy.Contains(name) || o.Optional) continue;
                PlayMakerFSM f = Live(o.Name);
                if (f != null && !Installed(f)) missing.Add(o.Name);
            }
            return WouldBlock(name, missing);
        }

        // sockets listed in an ArrayList proxy with this reference name (e.g. "MainBearings" on CORRIS/AssembyDatabase)
        public static List<GameObject> Group(string reference)
        {
            List<GameObject> res = new List<GameObject>();
            foreach (PlayMakerArrayListProxy p in Resources.FindObjectsOfTypeAll(typeof(PlayMakerArrayListProxy)))
            {
                if (p == null || p.referenceName != reference || p.arrayList == null) continue;
                foreach (object o in p.arrayList)
                {
                    GameObject g = o as GameObject;
                    if (g != null && g.name.StartsWith("VINP_") && !res.Contains(g)) res.Add(g);
                }
            }
            return res;
        }

        // nearest object tagged PART above a socket = the part the socket belongs to
        public static GameObject OwnerPart(Transform socket)
        {
            Transform p = socket.parent;
            while (p != null) { if (p.CompareTag("PART")) return p.gameObject; p = p.parent; }
            return null;
        }

        public static string PartName(GameObject part)
        {
            PlayMakerFSM d = DataFsm(part);
            if (d != null) { FsmString s = d.FsmVariables.GetFsmString("Name"); if (s != null && !string.IsNullOrEmpty(s.Value)) return s.Value; }
            return part.name;
        }

        public static List<MissingItem> Missing(Transform root, out int total)
        {
            total = 0;
            List<MissingItem> res = new List<MissingItem>();
            if (root == null) return res;
            List<PlayMakerFSM> socks = new List<PlayMakerFSM>();
            foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(false)) if (IsSocket(f)) socks.Add(f);
            return MissingFrom(socks, out total);
        }

        public static List<MissingItem> MissingFrom(List<PlayMakerFSM> socks, out int total)
        {
            total = 0;
            List<MissingItem> res = new List<MissingItem>();
            Dictionary<string, PlayMakerFSM> byName = new Dictionary<string, PlayMakerFSM>();
            foreach (PlayMakerFSM f in socks) byName[f.gameObject.name] = f;
            HashSet<int> groupsDone = new HashSet<int>();

            foreach (PlayMakerFSM f in socks)
            {
                string name = f.gameObject.name;
                SocketInfo si;
                if (!Info.TryGetValue(name, out si)) continue;
                if (si.Optional || Installed(f)) continue;
                string text = si.Nice;
                List<PlayMakerFSM> members = new List<PlayMakerFSM>();
                members.Add(f);
                int g;
                if (AltGroupOf.TryGetValue(name, out g))
                {
                    if (groupsDone.Contains(g)) continue;
                    groupsDone.Add(g);
                    bool any = false;
                    StringBuilder sb = new StringBuilder();
                    foreach (string m in AltGroups[g])
                    {
                        PlayMakerFSM mf;
                        if (!byName.TryGetValue(m, out mf)) continue;
                        SocketInfo ms; Info.TryGetValue(m, out ms);
                        if (ms != null && ms.Optional) continue;
                        if (Installed(mf)) any = true;
                        if (mf != f) members.Add(mf);
                        if (sb.Length > 0) sb.Append(" / ");
                        sb.Append(Nice(m));
                    }
                    if (any) continue;
                    text = sb.ToString();
                }
                bool can = false;
                foreach (PlayMakerFSM m in members) { InstallCheck c = CheckInstall(m); if (c.Allowed) can = true; }
                MissingItem it = new MissingItem();
                it.Text = text; it.CanNow = can; it.Order = si.Order;
                foreach (PlayMakerFSM m in members) it.Names.Add(m.gameObject.name);
                res.Add(it);
            }
            // which still-missing parts would each item block? (install those first)
            List<string> missingNames = new List<string>();
            foreach (PlayMakerFSM f in socks)
            {
                SocketInfo si;
                if (Info.TryGetValue(f.gameObject.name, out si) && !si.Optional && !Installed(f)) missingNames.Add(f.gameObject.name);
            }
            foreach (MissingItem it in res)
                foreach (string n in it.Names)
                    foreach (string wb in WouldBlock(n, missingNames))
                        if (!it.WouldBlock.Contains(wb)) it.WouldBlock.Add(wb);
            total = res.Count;
            res.Sort(delegate (MissingItem a, MissingItem b)
            {
                int c = a.Rank.CompareTo(b.Rank);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });
            return res;
        }
    }
}

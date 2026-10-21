// Bolts and adjustment screws of a part. Classified from the shape of the game's own FSMs:
//  - Fastener:    FSM "Screw" with a *Tightness* variable; "tight" limit = literal in its compare/clamp actions (8 on normal bolts)
//  - Calibration: FSM "Screw" tracking Alignment / AdjustmentStep / Rotation (valve adjusters, carb idle screws, toe...),
//                 FSM "HandRotate" (distributor timing, alternator belt) and FSM "Adjustment" (4-barrel carb)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssemblyGuide
{
    using HutongGames.PlayMaker;
    using HutongGames.PlayMaker.Actions;

    public enum BoltKind { Unknown, Fastener, Calibration }

    public class BoltInfo
    {
        public PlayMakerFSM Fsm;
        public BoltKind Kind;
        public string Var;      // tightness variable
        public bool IsFloat;
        public float Max;       // value at which the bolt is fully tight
    }

    public static class Bolts
    {
        static Dictionary<int, BoltInfo> cache = new Dictionary<int, BoltInfo>();
        static readonly string[] CalibrationVars = { "Alignment", "AdjustmentStep", "Rotation" };

        public static bool IsBoltFsm(PlayMakerFSM f)
        {
            if (f == null) return false;
            string n = f.FsmName;
            return n == "Screw" || n == "HandRotate" || n == "Adjustment";
        }

        public static BoltInfo Info(PlayMakerFSM f)
        {
            BoltInfo b;
            int id = f.GetInstanceID();
            if (cache.TryGetValue(id, out b)) return b;
            b = Classify(f);
            cache[id] = b;
            return b;
        }

        static BoltInfo Classify(PlayMakerFSM f)
        {
            BoltInfo b = new BoltInfo();
            b.Fsm = f;
            if (f.FsmName == "HandRotate" || f.FsmName == "Adjustment") { b.Kind = BoltKind.Calibration; return b; }

            List<string> candidates = new List<string>();
            List<bool> isFloat = new List<bool>();
            foreach (FsmInt v in f.FsmVariables.IntVariables) if (v.Name.IndexOf("Tightness", StringComparison.Ordinal) >= 0) { candidates.Add(v.Name); isFloat.Add(false); }
            foreach (FsmFloat v in f.FsmVariables.FloatVariables) if (v.Name.IndexOf("Tightness", StringComparison.Ordinal) >= 0) { candidates.Add(v.Name); isFloat.Add(true); }

            for (int i = 0; i < candidates.Count; i++)
            {
                float max = MaxFor(f, candidates[i]);
                if (max > 0f) { b.Kind = BoltKind.Fastener; b.Var = candidates[i]; b.IsFloat = isFloat[i]; b.Max = max; return b; }
            }
            foreach (string cv in CalibrationVars)
                if (f.FsmVariables.GetFsmFloat(cv) != null || f.FsmVariables.GetFsmInt(cv) != null) { b.Kind = BoltKind.Calibration; return b; }
            b.Kind = BoltKind.Unknown;
            return b;
        }

        static bool Lit(NamedVariable v) { return v != null && !v.UseVariable; }
        static bool Is(NamedVariable v, string name) { return v != null && v.UseVariable && v.Name == name; }

        // largest literal the tightness variable is compared or clamped against
        static float MaxFor(PlayMakerFSM f, string var)
        {
            float max = 0f;
            foreach (FsmState s in f.FsmStates)
                foreach (FsmStateAction a in FsmSim.ActionsOf(s))
                {
                    IntCompare ic = a as IntCompare;
                    if (ic != null && Is(ic.integer1, var) && Lit(ic.integer2)) max = Mathf.Max(max, ic.integer2.Value);
                    FloatCompare fc = a as FloatCompare;
                    if (fc != null && Is(fc.float1, var) && Lit(fc.float2)) max = Mathf.Max(max, fc.float2.Value);
                    IntClamp icl = a as IntClamp;
                    if (icl != null && Is(icl.intVariable, var) && Lit(icl.maxValue)) max = Mathf.Max(max, icl.maxValue.Value);
                    FloatClamp fcl = a as FloatClamp;
                    if (fcl != null && Is(fcl.floatVariable, var) && Lit(fcl.maxValue)) max = Mathf.Max(max, fcl.maxValue.Value);
                }
            return max;
        }

        public static float Value(BoltInfo b)
        {
            if (b.Var == null) return 0f;
            if (b.IsFloat) { FsmFloat v = b.Fsm.FsmVariables.GetFsmFloat(b.Var); return v == null ? 0f : v.Value; }
            FsmInt iv = b.Fsm.FsmVariables.GetFsmInt(b.Var);
            return iv == null ? 0f : iv.Value;
        }

        public static bool Tight(BoltInfo b) { return Value(b) >= b.Max - 0.01f; }

        // where to draw the mark: the moving bolt mesh if there is one
        public static Vector3 Pos(BoltInfo b)
        {
            FsmGameObject tb = b.Fsm.FsmVariables.GetFsmGameObject("ThisBolt");
            GameObject g = tb != null ? tb.Value : null;
            if (g == null) g = b.Fsm.gameObject;
            Renderer r = g.GetComponentInChildren<Renderer>();
            if (r != null && r.enabled && g.activeInHierarchy) return r.bounds.center;
            return g.transform.position;
        }

        // A part = object with a "Data" FSM that has a part "Name" (sockets VINP_* have none).
        // Not the PART tag: the game changes the tag of installed parts.
        static Dictionary<int, bool> isPart = new Dictionary<int, bool>();
        static float isPartReset;
        public static bool IsPart(GameObject g)
        {
            if (g == null || g.name.StartsWith("VINP_")) return false;
            if (Time.time >= isPartReset) { isPart.Clear(); isPartReset = Time.time + 10f; }
            bool v;
            int id = g.GetInstanceID();
            if (isPart.TryGetValue(id, out v)) return v;
            PlayMakerFSM d = Sockets.DataFsm(g);
            v = d != null && d.FsmVariables.GetFsmString("Name") != null;
            isPart[id] = v;
            return v;
        }

        public static GameObject PartOf(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent) if (IsPart(p.gameObject)) return p.gameObject;
            return null;
        }

        static GameObject OwnerPart(Transform t) { return PartOf(t); }

        // can the part be taken off right now? (its socket's "Allow removal?" state; loose parts: yes)
        public static bool Removable(GameObject part)
        {
            if (part == null) return true;
            Transform s = part.transform.parent;
            while (s != null && !s.name.StartsWith("VINP_")) s = s.parent;
            if (s == null) return true;
            PlayMakerFSM data = Sockets.DataFsm(s.gameObject);
            if (data == null || !Sockets.Installed(data)) return true;
            try
            {
                FsmSim sim = new FsmSim(data);
                if (!sim.HasState("Allow removal?")) return true;
                string denied;
                if (!sim.Transitions("Allow removal?").TryGetValue("FINISHED", out denied)) return true;
                sim.StopOnExit = true;
                SimState st = sim.Run("Allow removal?");
                return st.ExitState == null || st.ExitState != denied;
            }
            catch (Exception) { return true; }
        }

        // all bolt FSMs in the scene (for "nearest bolt to the line of sight")
        static List<PlayMakerFSM> all = new List<PlayMakerFSM>();
        static float allAt = -100f;
        public static GameObject NearestBoltPart(Vector3 origin, Vector3 dir, float maxDist, float maxPerp)
        {
            if (Time.time - allAt > 5f)
            {
                all.Clear();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                    if (IsBoltFsm(f) && f.gameObject.activeInHierarchy) all.Add(f);
                allAt = Time.time;
            }
            GameObject best = null; float bp = maxPerp;
            foreach (PlayMakerFSM f in all)
            {
                if (f == null || !f.gameObject.activeInHierarchy) continue;
                BoltInfo b = Info(f);
                if (b.Kind == BoltKind.Unknown) continue;
                Vector3 v = Pos(b) - origin;
                float along = Vector3.Dot(v, dir);
                if (along < 0.1f || along > maxDist) continue;
                float perp = (v - dir * along).magnitude;
                if (perp < bp) { GameObject p = PartOf(f.transform); if (p != null) { bp = perp; best = p; } }
            }
            return best;
        }

        // top-most part above (the whole assembly: engine block with everything on it, or the part itself)
        public static GameObject TopPart(GameObject part)
        {
            GameObject top = part;
            if (part == null) return null;
            for (Transform p = part.transform.parent; p != null; p = p.parent) if (IsPart(p.gameObject)) top = p.gameObject;
            return top;
        }

        // fasteners of the whole assembly that belong to other parts than 'except'
        public static void CollectOthers(GameObject root, GameObject except, List<BoltInfo> into)
        {
            into.Clear();
            if (root == null) return;
            foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(false))
            {
                if (!IsBoltFsm(f)) continue;
                if (PartOf(f.transform) == except) continue;
                BoltInfo b = Info(f);
                if (b.Kind == BoltKind.Fastener) into.Add(b);
            }
        }

        // ---- sizes and tools
        public static float Size(BoltInfo b)
        {
            FsmFloat s = b.Fsm.FsmVariables.GetFsmFloat("Boltsize");
            if (s == null) s = b.Fsm.FsmVariables.GetFsmFloat("BoltSize");
            return s == null ? 0f : s.Value;
        }

        // special tools: the player's tool FSM sets ToolWrenchSize to a literal per tool (screwdriver, lug wrench...);
        // spanners and sockets use their mesh scale (0.5..1.5 = 5..15 mm)
        static Dictionary<float, string> tools;
        static float nextToolTry;
        // The tool FSM only becomes readable after the player has taken out the tools once (until then its object is
        // inactive and its actions can't be loaded), so retry every few seconds until it worked.
        static void InitTools()
        {
            if (tools != null && tools.Count > 0) return;
            if (tools != null && Time.time < nextToolTry) return;
            nextToolTry = Time.time + 5f;
            tools = new Dictionary<float, string>();
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                if (f.FsmName != "PickUp" || f.gameObject.name != "Pick" || f.transform.parent == null || f.transform.parent.name != "2Spanner") continue;
                if (f.Fsm == null || !f.Fsm.Initialized) continue;
                foreach (FsmState s in f.FsmStates)
                    foreach (FsmStateAction a in FsmSim.ActionsOf(s))
                    {
                        SetFloatValue sv = a as SetFloatValue;
                        if (sv == null || sv.floatVariable == null || sv.floatVariable.Name != "ToolWrenchSize" || sv.floatValue == null || sv.floatValue.UseVariable) continue;
                        float v = Mathf.Round(sv.floatValue.Value * 100f) / 100f;
                        if (!tools.ContainsKey(v)) tools[v] = s.Name.ToLower();
                    }
                break;
            }
        }

        public static string ToolName(float size)
        {
            if (size <= 0f) return "by hand";
            InitTools();
            foreach (KeyValuePair<float, string> kv in tools) if (Mathf.Abs(kv.Key - size) <= 0.021f) return kv.Value;
            float mm = size * 10f;
            if (Mathf.Abs(mm - Mathf.Round(mm)) < 0.21f) return Mathf.RoundToInt(mm) + " mm";
            return "size " + size.ToString("0.00");
        }

        // "4 × 10 mm, 1 × screwdriver"
        public static string Summary(List<BoltInfo> list, Predicate<BoltInfo> which)
        {
            Dictionary<string, int> n = new Dictionary<string, int>();
            List<string> order = new List<string>();
            foreach (BoltInfo b in list)
            {
                if (!which(b)) continue;
                string tool = b.Fsm.FsmName == "HandRotate" ? "by hand" : ToolName(Size(b));
                if (!n.ContainsKey(tool)) { n[tool] = 0; order.Add(tool); }
                n[tool]++;
            }
            order.Sort();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (string k in order) { if (sb.Length > 0) sb.Append(", "); sb.Append(n[k]).Append(" \u00d7 ").Append(k); }
            return sb.ToString();
        }

        public static bool HasBolts(GameObject part)
        {
            List<BoltInfo> tmp = new List<BoltInfo>();
            Collect(part, tmp);
            return tmp.Count > 0;
        }

        // bolts that belong to this part (not to parts installed on it)
        public static void Collect(GameObject part, List<BoltInfo> into)
        {
            into.Clear();
            if (part == null) return;
            HashSet<int> seen = new HashSet<int>();
            foreach (PlayMakerFSM f in part.GetComponentsInChildren<PlayMakerFSM>(false))
            {
                if (!IsBoltFsm(f) || OwnerPart(f.transform) != part) continue;
                Add(f, into, seen);
            }
            // bolts kept outside the part's hierarchy, referenced by its Data FSM
            PlayMakerFSM d = Sockets.DataFsm(part);
            if (d != null)
            {
                FsmGameObject bg = d.FsmVariables.GetFsmGameObject("Bolts");
                if (bg != null && bg.Value != null && !bg.Value.transform.IsChildOf(part.transform))
                    foreach (PlayMakerFSM f in bg.Value.GetComponentsInChildren<PlayMakerFSM>(false))
                        if (IsBoltFsm(f)) Add(f, into, seen);
            }
        }

        static void Add(PlayMakerFSM f, List<BoltInfo> into, HashSet<int> seen)
        {
            if (!seen.Add(f.GetInstanceID())) return;
            BoltInfo b = Info(f);
            if (b.Kind != BoltKind.Unknown) into.Add(b);
        }
    }
}

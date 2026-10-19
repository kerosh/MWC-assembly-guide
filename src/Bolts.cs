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

        static GameObject OwnerPart(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent) if (p.CompareTag("PART")) return p.gameObject;
            return null;
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

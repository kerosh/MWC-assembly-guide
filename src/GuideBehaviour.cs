// Runtime: watches the part in the player's hands, draws the ghost at its socket, shows status text and the missing-parts list.
using System;
using System.Collections.Generic;
using System.Text;
using MSCLoader;
using UnityEngine;

namespace AssemblyGuide
{
    using HutongGames.PlayMaker;

    public enum Verdict { None, Green, Yellow, Red, Occupied, Unavailable }

    public class GuideBehaviour : MonoBehaviour
    {
        public static GuideBehaviour Instance;
        bool ready;
        float initAt;

        PlayMakerFSM hand;               // PLAYER/.../1Hand_Assemble/Hand, FSM "PickUp"
        float nextHandSearch;

        // ---- held part
        public GameObject HeldPart, HeldSocket;
        public GameObject LastPart, LastSocket;   // last part held (for diagnostics after dropping it)
        public PlayMakerFSM HeldSocketData;
        public string HeldName = "";
        public InstallCheck HeldInstall;
        public VinVerdict HeldVin;
        public bool SocketInstalled;
        public Verdict HeldVerdict = Verdict.None;
        float nextInstallCheck, nextVinCheck;
        List<MeshFilter> ghostMeshes = new List<MeshFilter>();
        List<Matrix4x4> ghostRel = new List<Matrix4x4>();
        Material ghostMat;

        // ---- missing list
        bool showList;
        List<MissingItem> carList = new List<MissingItem>(), targetList = new List<MissingItem>();
        int carTotal, targetTotal;
        string targetName = "";
        Transform targetRoot;               // assembly under the crosshair (null = car area / nothing)
        Vector3 targetPoint;
        bool targetCarArea;
        float nextRaycast;
        Dictionary<int, bool> hasSockets = new Dictionary<int, bool>();
        float hasSocketsReset;
        float nextListRefresh, nextSocketCache;
        List<PlayMakerFSM> socketCache = new List<PlayMakerFSM>();
        GameObject car;

        // ---- bolt marks
        public bool ShowBolts = true;
        GameObject boltPart;
        List<BoltInfo> bolts = new List<BoltInfo>();
        float nextBoltRefresh;
        Texture2D dotTex;

        // ---- UI
        GUIStyle textStyle;
        Texture2D bgTex;
        string labelText = "";

        void Start()
        {
            Instance = this;
            initAt = Time.time + 4f;   // let save loading finish (VIN table, part spawns)
        }

        public void Init()
        {
            try { VinCheck.Init(); } catch (Exception e) { ModConsole.Error("[AssemblyGuide] VIN init failed: " + e); }
            try { Sockets.Scan(); } catch (Exception e) { ModConsole.Error("[AssemblyGuide] socket scan failed: " + e); }
            ModConsole.Print("[AssemblyGuide] " + Sockets.Status + "; " + VinCheck.Status);
            car = GameObject.Find("CORRIS");
            nextSocketCache = 0f;
            ready = true;
        }

        void FindHand()
        {
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                if (f.FsmName == "PickUp" && f.gameObject.name == "Hand" && f.transform.parent != null && f.transform.parent.name == "1Hand_Assemble")
                { hand = f; return; }
        }

        public string HandState { get { return hand == null ? "(hand FSM not found)" : hand.ActiveStateName; } }

        GameObject Held()
        {
            if (hand == null || !hand.gameObject.activeInHierarchy) return null;
            string st = hand.ActiveStateName ?? "";
            if (st.IndexOf("picked", StringComparison.OrdinalIgnoreCase) < 0) return null;
            FsmGameObject g = hand.FsmVariables.GetFsmGameObject("PickedObject");
            GameObject go = g == null ? null : g.Value;
            if (go == null || !go.activeInHierarchy || !go.CompareTag("PART")) return null;
            return go;
        }

        void SetHeld(GameObject part)
        {
            HeldPart = part; HeldSocket = null; HeldSocketData = null; HeldInstall = null; HeldVin = null;
            HeldVerdict = Verdict.None; SocketInstalled = false; HeldName = "";
            ghostMeshes.Clear(); ghostRel.Clear();
            nextInstallCheck = 0f; nextVinCheck = Time.time + 0.3f;
            if (part == null) return;
            HeldName = Sockets.PartName(part);
            PlayMakerFSM d = Sockets.DataFsm(part);
            if (d != null)
            {
                FsmGameObject ip = d.FsmVariables.GetFsmGameObject("InstallPoint");
                if (ip != null) HeldSocket = ip.Value;
            }
            if (HeldSocket != null) HeldSocketData = Sockets.DataFsm(HeldSocket);
            LastPart = part; LastSocket = HeldSocket;
            Matrix4x4 inv = part.transform.worldToLocalMatrix;
            foreach (MeshFilter mf in part.GetComponentsInChildren<MeshFilter>(false))
            {
                Renderer r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled) continue;
                ghostMeshes.Add(mf);
                ghostRel.Add(inv * mf.transform.localToWorldMatrix);
            }
        }

        void Update()
        {
            if (!ready) { if (Time.time >= initAt) Init(); return; }
            if (hand == null && Time.time >= nextHandSearch) { FindHand(); nextHandSearch = Time.time + 3f; }
            if (AssemblyGuideMod.kList != null && AssemblyGuideMod.kList.GetKeybindDown()) { showList = !showList; nextListRefresh = 0f; }
            if (AssemblyGuideMod.kDump != null && AssemblyGuideMod.kDump.GetKeybindDown()) GuideCommand.Execute(new string[] { "all" });

            GameObject held = null;
            try { held = Held(); } catch (Exception) { }
            if (held != HeldPart) SetHeld(held);

            if (HeldPart != null)
            {
                if (Time.time >= nextInstallCheck) { RefreshInstall(); nextInstallCheck = Time.time + 0.5f; }
                if (Time.time >= nextVinCheck) { RefreshVin(); nextVinCheck = float.MaxValue; }   // once per pickup: result depends only on part + VIN
                UpdateVerdict();
                if (AssemblyGuideMod.On(AssemblyGuideMod.sGhost)) DrawGhost();
            }
            if (AssemblyGuideMod.kBolts != null && AssemblyGuideMod.kBolts.GetKeybindDown()) ShowBolts = !ShowBolts;
            bool bolting = ShowBolts && AssemblyGuideMod.On(AssemblyGuideMod.sBolts);
            if ((showList || bolting) && Time.time >= nextRaycast)
            {
                nextRaycast = Time.time + 0.3f;
                bool changed = PickTarget();
                if (showList && (changed || Time.time >= nextListRefresh)) { RefreshTarget(); nextListRefresh = Time.time + 1.5f; }
                if (bolting && (lookPartChanged || Time.time >= nextBoltRefresh))
                {
                    boltPart = lookPart;
                    try { Bolts.Collect(boltPart, bolts); } catch (Exception e) { bolts.Clear(); ModConsole.Error("[AssemblyGuide] bolts: " + e.Message); }
                    nextBoltRefresh = Time.time + 2f;
                }
            }
            else if (showList && Time.time >= nextListRefresh) { RefreshTarget(); nextListRefresh = Time.time + 1.5f; }
            if (!bolting && bolts.Count > 0) { bolts.Clear(); boltPart = null; }
        }

        void RefreshInstall()
        {
            if (HeldSocketData == null) { HeldInstall = null; return; }
            SocketInstalled = Sockets.Installed(HeldSocketData);
            try { HeldInstall = Sockets.CheckInstall(HeldSocketData); }
            catch (Exception e) { HeldInstall = null; ModConsole.Error("[AssemblyGuide] install check: " + e.Message); }
        }

        void RefreshVin()
        {
            HeldVin = null;
            if (HeldSocket == null || !AssemblyGuideMod.On(AssemblyGuideMod.sVin)) return;
            try { HeldVin = VinCheck.Check(HeldSocket, HeldPart, false); }
            catch (Exception e) { ModConsole.Error("[AssemblyGuide] VIN check: " + e.Message); }
        }

        void UpdateVerdict()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<b>").Append(HeldName).Append("</b>");
            if (HeldSocket == null) { HeldVerdict = Verdict.None; sb.Append("  <color=#bbbbbb>no install point known for this part</color>"); labelText = sb.ToString(); return; }
            if (SocketInstalled) { HeldVerdict = Verdict.Occupied; sb.Append("  <color=#bbbbbb>").Append(Sockets.Nice(HeldSocket.name)).Append(" already has a part</color>"); labelText = sb.ToString(); return; }
            if (!HeldSocket.activeInHierarchy) { HeldVerdict = Verdict.Unavailable; sb.Append("  <color=#bbbbbb>its place is not available right now</color>"); labelText = sb.ToString(); return; }

            bool red = HeldInstall != null && HeldInstall.Known && !HeldInstall.Allowed;
            bool yellow = HeldVin != null && HeldVin.Checked && !HeldVin.Pass;
            if (red)
            {
                HeldVerdict = Verdict.Red;
                sb.Append("  <color=#ff6655>can't install yet</color>");
                if (HeldInstall.Blockers.Count > 0) sb.Append("\n<color=#ff8877>remove first: ").Append(string.Join(", ", HeldInstall.Blockers.ToArray())).Append("</color>");
                if (HeldInstall.Needs.Count > 0) sb.Append("\n<color=#ff8877>install first: ").Append(string.Join(", ", HeldInstall.Needs.ToArray())).Append("</color>");
                if (HeldInstall.Blockers.Count == 0 && HeldInstall.Needs.Count == 0 && HeldInstall.Note.Length > 0) sb.Append("\n<color=#ff8877>").Append(HeldInstall.Note).Append("</color>");
            }
            else
            {
                HeldVerdict = yellow ? Verdict.Yellow : Verdict.Green;
                sb.Append(yellow ? "  <color=#ffd84a>fits</color>" : "  <color=#77ee77>fits</color>");
            }
            if (yellow)
            {
                sb.Append("\n<color=#ffd84a>not original for this VIN (").Append(string.Join(", ", HeldVin.FailKeys.ToArray())).Append(")");
                if (HeldVin.Reasons.Count > 0) sb.Append(": ").Append(HeldVin.Reasons[0]);
                sb.Append("</color>");
            }
            labelText = sb.ToString();
        }

        // ---------------------------------------------------------------- ghost
        Material GhostMat()
        {
            if (ghostMat != null) return ghostMat;
            string src =
                "Shader \"AssemblyGuide/Ghost\" { Properties { _Color (\"Color\", Color) = (0,1,0,0.35) } " +
                "SubShader { Tags { \"Queue\"=\"Transparent+100\" \"IgnoreProjector\"=\"True\" \"RenderType\"=\"Transparent\" } " +
                "Pass { ZWrite Off ZTest LEqual Cull Off Lighting Off Blend SrcAlpha OneMinusSrcAlpha Color [_Color] } " +
                "Pass { ZWrite Off ZTest Greater Cull Off Lighting Off Blend SrcAlpha One Color (0.25,0.25,0.25,0.25) } } }";
            try
            {
#pragma warning disable 618
                ghostMat = new Material(src);
#pragma warning restore 618
            }
            catch (Exception) { ghostMat = null; }
            if (ghostMat == null || ghostMat.shader == null || !ghostMat.shader.isSupported)
            {
                string[] names = { "Unlit/Transparent", "Transparent/Diffuse", "Particles/Alpha Blended", "Sprites/Default" };
                foreach (string n in names) { Shader s = Shader.Find(n); if (s != null) { ghostMat = new Material(s); break; } }
            }
            return ghostMat;
        }

        void DrawGhost()
        {
            if (HeldSocket == null || SocketInstalled || !HeldSocket.activeInHierarchy || ghostMeshes.Count == 0) return;
            if (HeldVerdict == Verdict.None || HeldVerdict == Verdict.Occupied || HeldVerdict == Verdict.Unavailable) return;
            Camera cam = Camera.main;
            if (cam != null && Vector3.Distance(cam.transform.position, HeldSocket.transform.position) > AssemblyGuideMod.Val(AssemblyGuideMod.sGhostDist, 8f)) return;
            Material m = GhostMat();
            if (m == null) return;
            float a = AssemblyGuideMod.Val(AssemblyGuideMod.sGhostAlpha, 0.35f);
            a *= 0.8f + 0.2f * Mathf.Sin(Time.time * 4f);
            Color c = HeldVerdict == Verdict.Red ? new Color(1f, 0.25f, 0.2f, a)
                    : HeldVerdict == Verdict.Yellow ? new Color(1f, 0.85f, 0.1f, a)
                    : new Color(0.25f, 1f, 0.35f, a);
            m.color = c;
            Matrix4x4 socketM = HeldSocket.transform.localToWorldMatrix;
            for (int i = 0; i < ghostMeshes.Count; i++)
            {
                MeshFilter mf = ghostMeshes[i];
                if (mf == null || mf.sharedMesh == null) continue;
                Matrix4x4 mtx = socketM * ghostRel[i];
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    Graphics.DrawMesh(mf.sharedMesh, mtx, m, mf.gameObject.layer, null, sub);
            }
        }

        // ---------------------------------------------------------------- missing list
        // whole car (console "aguide list")
        public List<MissingItem> CarList
        {
            get
            {
                if (car == null) car = GameObject.Find("CORRIS");
                carList = Sockets.Missing(car == null ? null : car.transform, out carTotal);
                return carList;
            }
        }

        void CacheSockets()
        {
            if (Time.time < nextSocketCache) return;
            socketCache.Clear();
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM))) if (Sockets.IsSocket(f)) socketCache.Add(f);
            nextSocketCache = Time.time + 10f;
        }

        bool HasSockets(Transform p)
        {
            if (Time.time >= hasSocketsReset) { hasSockets.Clear(); hasSocketsReset = Time.time + 10f; }
            bool v;
            int id = p.GetInstanceID();
            if (hasSockets.TryGetValue(id, out v)) return v;
            v = false;
            foreach (PlayMakerFSM f in p.GetComponentsInChildren<PlayMakerFSM>(false)) if (Sockets.IsSocket(f)) { v = true; break; }
            hasSockets[id] = v;
            return v;
        }

        GameObject lookPart;          // nearest PART under the crosshair (for bolt marks)
        bool lookPartChanged;

        // What is under the crosshair: an assembly (part with sockets) or an area of the car. Returns true if it changed.
        bool PickTarget()
        {
            lookPartChanged = false;
            Camera cam = Camera.main;
            if (cam == null) return false;
            if (car == null) car = GameObject.Find("CORRIS");
            RaycastHit[] hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, AssemblyGuideMod.Val(AssemblyGuideMod.sLookRange, 4f));
            Transform hitT = null; float bd = float.MaxValue; Vector3 point = Vector3.zero;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.isTrigger) continue;
                Transform ht = h.collider.transform;
                if (HeldPart != null && ht.IsChildOf(HeldPart.transform)) continue;
                if (ht.root.name == "PLAYER") continue;
                if (h.distance < bd) { bd = h.distance; hitT = ht; point = h.point; }
            }
            if (hitT == null) return false;   // keep the previous target while looking at nothing

            GameObject lp = null;
            for (Transform p = hitT; p != null; p = p.parent) if (p.CompareTag("PART")) { lp = p.gameObject; break; }
            if (lp != lookPart) { lookPart = lp; lookPartChanged = true; }   // null = looking at a non-part (car body ...)

            Transform asm = null;
            for (Transform p = hitT; p != null; p = p.parent)
            {
                if (car != null && p == car.transform) break;
                if (p.CompareTag("PART") && HasSockets(p)) { asm = p; break; }
            }
            bool carArea = asm == null && car != null && hitT.IsChildOf(car.transform);
            if (asm == null && !carArea) return false;
            bool changed = asm != targetRoot || carArea != targetCarArea ||
                           (carArea && Vector3.Distance(point, targetPoint) > 0.3f);
            targetRoot = asm; targetCarArea = carArea;
            if (changed) targetPoint = point;
            return changed;
        }

        void RefreshTarget()
        {
            List<PlayMakerFSM> socks = new List<PlayMakerFSM>();
            if (targetRoot != null)
            {
                targetName = Sockets.PartName(targetRoot.gameObject);
                foreach (PlayMakerFSM f in targetRoot.GetComponentsInChildren<PlayMakerFSM>(false)) if (Sockets.IsSocket(f)) socks.Add(f);
            }
            else if (targetCarArea && car != null)
            {
                targetName = "Car, around here";
                CacheSockets();
                float r = AssemblyGuideMod.Val(AssemblyGuideMod.sAreaRadius, 1.5f);
                foreach (PlayMakerFSM f in socketCache)
                {
                    if (f == null || !f.gameObject.activeInHierarchy || !f.transform.IsChildOf(car.transform)) continue;
                    if (Vector3.Distance(f.transform.position, targetPoint) <= r) socks.Add(f);
                }
            }
            else { targetName = ""; targetList = new List<MissingItem>(); targetTotal = 0; return; }
            try { targetList = Sockets.MissingFrom(socks, out targetTotal); }
            catch (Exception e) { targetList = new List<MissingItem>(); ModConsole.Error("[AssemblyGuide] list: " + e.Message); }
        }

        // ---------------------------------------------------------------- GUI
        void EnsureStyles()
        {
            if (bgTex == null)
            {
                bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
                bgTex.Apply();
            }
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label);
                textStyle.richText = true;
                textStyle.wordWrap = true;
                textStyle.normal.textColor = Color.white;
            }
            float scale = Screen.height / 1080f * AssemblyGuideMod.Val(AssemblyGuideMod.sFont, 1f);
            textStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(17f * scale));
        }

        void Box(Rect r, string text)
        {
            GUI.DrawTexture(new Rect(r.x - 8, r.y - 6, r.width + 16, r.height + 12), bgTex);
            GUI.Label(r, text, textStyle);
        }

        void OnGUI()
        {
            if (!ready) return;
            bool drawBolts = bolts.Count > 0 && ShowBolts && AssemblyGuideMod.On(AssemblyGuideMod.sBolts);
            if (HeldPart == null && !showList && !drawBolts) return;
            EnsureStyles();
            if (drawBolts) DrawBolts();
            if (HeldPart != null && AssemblyGuideMod.On(AssemblyGuideMod.sLabel) && labelText.Length > 0)
            {
                float w = Mathf.Min(Screen.width * 0.6f, textStyle.fontSize * 34f);
                GUIContent gc = new GUIContent(labelText);
                float h = textStyle.CalcHeight(gc, w);
                Box(new Rect((Screen.width - w) / 2f, Screen.height * 0.08f, w, h), labelText);
            }
            if (showList)
            {
                int max = Mathf.RoundToInt(AssemblyGuideMod.Val(AssemblyGuideMod.sListLines, 30f));
                StringBuilder sb = new StringBuilder();
                if (targetName.Length == 0) sb.Append("<color=#bbbbbb>look at the car or an assembly (engine, head...)</color>\n");
                else
                {
                    sb.Append("<b>").Append(targetName).Append(" - ").Append(targetTotal).Append(" missing</b>\n");
                    Lines(sb, targetList, max);
                }
                sb.Append("\n<size=").Append(Mathf.Max(9, textStyle.fontSize - 3)).Append("><color=#77ee77>green</color> = can install now, <color=#999999>grey</color> = later</size>");
                string text = sb.ToString();
                float w = textStyle.fontSize * 20f;
                float h = textStyle.CalcHeight(new GUIContent(text), w);
                Box(new Rect(20f, Screen.height * 0.15f, w, h), text);
            }
        }

        // x-ray marks: drawn on the screen, so they show through everything
        void DrawBolts()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            if (dotTex == null) dotTex = MakeDot(32);
            float size = AssemblyGuideMod.Val(AssemblyGuideMod.sBoltSize, 14f) * Screen.height / 1080f;
            Color old = GUI.color;
            int tight = 0, loose = 0, cal = 0;
            foreach (BoltInfo b in bolts)
            {
                if (b.Fsm == null || !b.Fsm.gameObject.activeInHierarchy) continue;
                Vector3 sp = cam.WorldToScreenPoint(Bolts.Pos(b));
                if (sp.z <= 0.05f) continue;
                Color c;
                if (b.Kind == BoltKind.Calibration) { c = new Color(1f, 0.85f, 0.1f, 0.95f); cal++; }
                else if (Bolts.Tight(b)) { c = new Color(0.25f, 1f, 0.35f, 0.95f); tight++; }
                else { c = new Color(1f, 0.2f, 0.15f, 0.95f); loose++; }
                Rect r = new Rect(sp.x - size / 2f, Screen.height - sp.y - size / 2f, size, size);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), dotTex);
                GUI.color = c;
                GUI.DrawTexture(r, dotTex);
            }
            GUI.color = old;
            if (boltPart != null && (tight + loose + cal) > 0 && AssemblyGuideMod.On(AssemblyGuideMod.sLabel))
            {
                string s = Sockets.PartName(boltPart) + ":  <color=#77ee77>" + tight + " tight</color>  <color=#ff6655>" + loose + " loose</color>" +
                           (cal > 0 ? "  <color=#ffd84a>" + cal + " adjust</color>" : "");
                float w = textStyle.fontSize * 22f;
                float h = textStyle.CalcHeight(new GUIContent(s), w);
                Box(new Rect((Screen.width - w) / 2f, Screen.height * 0.82f, w, h), s);
            }
        }

        static Texture2D MakeDot(int n)
        {
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(c - d + 0.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static void Lines(StringBuilder sb, List<MissingItem> list, int max)
        {
            if (list.Count == 0) { sb.Append("<color=#77ee77>nothing missing</color>\n"); return; }
            int n = 0;
            foreach (MissingItem it in list)
            {
                if (n++ >= max) { sb.Append("<color=#999999>... ").Append(list.Count - max).Append(" more</color>\n"); break; }
                sb.Append(it.CanNow ? "<color=#77ee77>" : "<color=#999999>").Append(it.Text).Append("</color>\n");
            }
        }
    }
}

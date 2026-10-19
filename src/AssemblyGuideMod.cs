using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MSCLoader;
using UnityEngine;

[assembly: AssemblyTitle("AssemblyGuide")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyTrademark("AI generated code (Claude)")]

namespace AssemblyGuide
{
    using HutongGames.PlayMaker;

    public class AssemblyGuideMod : Mod
    {
        public override string ID { get { return "AssemblyGuide"; } }
        public override string Name { get { return "Assembly Guide"; } }
        public override string Author { get { return "hotkeros"; } }
        public override string Version { get { return "1.1.0"; } }
        public override string Description
        {
            get
            {
                return "Shows a ghost of the part in your hands where it goes: green = fits, orange = fits but install another part first, yellow = fits but not original for your VIN " +
                       "(historical inspection), red = something must be installed/removed first. Toggleable list of missing parts in assembly order. " +
                       "Rules are read from the game's own logic, nothing is hardcoded. (Code written with AI assistance - Claude)";
            }
        }
        public override MSCLoader.Game SupportedGames { get { return MSCLoader.Game.MyWinterCar; } }

        public static SettingsCheckBox sGhost, sLabel, sVin, sBolts;
        public static SettingsSlider sGhostAlpha, sGhostDist, sListLines, sFont, sLookRange, sAreaRadius, sBoltSize;
        public static AssemblyGuideMod Instance;
        public static SettingsKeybind kList, kDump, kBolts;
        GameObject go;

        public static bool On(SettingsCheckBox c) { return c == null || c.GetValue(); }
        public static float Val(SettingsSlider s, float def) { return s == null ? def : s.GetValue(); }

        public override void ModSetup()
        {
            Instance = this;
            SetupFunction(Mod.Setup.ModSettings, Mod_Settings);
            SetupFunction(Mod.Setup.OnLoad, Mod_OnLoad);
        }

        void Mod_Settings()
        {
            Settings.AddHeader("Held part");
            sGhost = Settings.AddCheckBox("ghost", "Show ghost of the held part where it goes", true);
            sGhostAlpha = Settings.AddSlider("ghostAlpha", "Ghost opacity", 0.1f, 0.8f, 0.35f, null, 2);
            sGhostDist = Settings.AddSlider("ghostDist", "Max ghost distance (m)", 2f, 20f, 8f, null, 0);
            sLabel = Settings.AddCheckBox("label", "Show status text for the held part", true);
            sVin = Settings.AddCheckBox("vin", "Check historical (VIN) inspection rules (yellow)", true);
            Settings.AddHeader("Bolt marks");
            sBolts = Settings.AddCheckBox("bolts", "Mark bolts of the part under the crosshair (red loose, green tight, yellow adjustment)", true);
            sBoltSize = Settings.AddSlider("boltSize", "Mark size", 6f, 30f, 14f, null, 0);
            Settings.AddHeader("Missing parts list");
            Settings.AddText("The list shows what is missing on the assembly under your crosshair (engine, head...) or, on the car itself, around the spot you look at.");
            sListLines = Settings.AddSlider("listLines", "Max lines", 5f, 40f, 15f, null, 0);
            sLookRange = Settings.AddSlider("lookRange", "Look distance (m)", 1f, 8f, 4f, null, 1);
            sAreaRadius = Settings.AddSlider("areaRadius", "Car area radius (m)", 0.5f, 3f, 1.5f, null, 1);
            sFont = Settings.AddSlider("font", "Text size", 0.7f, 1.6f, 1f, null, 2);
            Settings.AddText("Ctrl+Num9 writes diagnostics for the part in hands (or the last one held). Console: <b>aguide</b> held | socket NAME | list | vin | order | all | rescan  (details go to aguide_dump.txt in this mod's settings folder)");
            Keybind.AddHeader("Assembly Guide");
            kList = Keybind.Add("k_list", "Show / hide missing parts list", KeyCode.Keypad7, KeyCode.LeftControl);
            kBolts = Keybind.Add("k_bolts", "Show / hide bolt marks", KeyCode.Keypad8, KeyCode.LeftControl);
            kDump = Keybind.Add("k_dump", "Write diagnostics (aguide all) for the part in hands", KeyCode.Keypad9, KeyCode.LeftControl);
        }

        void Mod_OnLoad()
        {
            try
            {
                go = new GameObject("AssemblyGuide");
                go.AddComponent<GuideBehaviour>();
                ConsoleCommand.Add(new GuideCommand());
                ModConsole.Print("[AssemblyGuide] loaded, analysing car parts in a few seconds...");
            }
            catch (Exception e) { ModConsole.Error("[AssemblyGuide] load failed: " + e); }
        }
    }

    // Console command. Details go to aguide_dump.txt (mod settings folder) - the in-game console is small and can't be copied from.
    public class GuideCommand : ConsoleCommand
    {
        public override string Name { get { return "aguide"; } }
        const string HelpText = "Assembly Guide: aguide held | socket NAME | list | vin | order | all | rescan";
        public override string Help { get { return HelpText; } }

        static StringBuilder outp = new StringBuilder();
        static void P(string s) { outp.Append(s).Append('\n'); }
        static void Say(string s) { ModConsole.Print("[AssemblyGuide] " + s); }

        public static string DumpPath
        {
            get
            {
                string dir = AssemblyGuideMod.Instance != null ? ModLoader.GetModSettingsFolder(AssemblyGuideMod.Instance) : ".";
                return System.IO.Path.Combine(dir, "aguide_dump.txt");
            }
        }

        public override void Run(string[] args) { Execute(args); }

        public static void Execute(string[] args)
        {
            GuideBehaviour b = GuideBehaviour.Instance;
            string cmd = args.Length > 0 ? args[0].ToLower() : "";
            outp = new StringBuilder();
            P("=== aguide " + string.Join(" ", args) + "   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            P(Sockets.Status + " | " + VinCheck.Status);
            try
            {
                if (cmd == "rescan") { if (b != null) b.Init(); Say(Sockets.Status); return; }
                else if (cmd == "held") Held(b);
                else if (cmd == "socket" && args.Length > 1) Socket(args[1]);
                else if (cmd == "list") ListCmd(b);
                else if (cmd == "vin") Vin();
                else if (cmd == "order") Order();
                else if (cmd == "all") { Held(b); Vin(); ListCmd(b); Order(); }
                else { Say(HelpText); Say(Sockets.Status + " | " + VinCheck.Status); return; }
            }
            catch (Exception e) { P("ERROR " + e); Say("error: " + e.Message); }
            try
            {
                string path = DumpPath;
                if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 2000000) System.IO.File.Delete(path);
                System.IO.File.AppendAllText(path, outp.ToString() + "\n");
                Say("details written to " + path);
            }
            catch (Exception e) { Say("could not write dump: " + e.Message); }
        }

        static void Order()
        {
            P("--- assembly order");
            foreach (string s in Sockets.Order)
            {
                SocketInfo si = Sockets.Info[s];
                P(si.Order + " " + s + (si.Optional ? " (optional)" : "") + (Sockets.AltGroupOf.ContainsKey(s) ? " (alternative)" : ""));
            }
        }

        static void Held(GuideBehaviour b)
        {
            P("--- held part");
            if (b == null) { P("not loaded"); return; }
            P("hand state: " + b.HandState);
            GameObject part = b.HeldPart != null ? b.HeldPart : b.LastPart;
            GameObject sock = b.HeldPart != null ? b.HeldSocket : b.LastSocket;
            if (part == null) { P("no part in hands (and none held before)"); Say("no part held yet"); return; }
            string pname = Sockets.PartName(part);
            if (b.HeldGroup.Length > 0) P("fits one of several places: group '" + b.HeldGroup + "', nearest free one chosen");
            P((b.HeldPart != null ? "part in hands: " : "last held part: ") + pname + " (" + part.name + "), socket: " + (sock == null ? "none" : FullPath(sock.transform)) +
              (b.HeldPart != null ? ", verdict: " + b.HeldVerdict : ""));
            Say(pname + " -> " + (sock == null ? "no socket" : sock.name) + (b.HeldPart != null ? ": " + b.HeldVerdict : " (last held)"));
            if (sock == null) return;
            SocketInfo si;
            if (Sockets.Info.TryGetValue(sock.name, out si)) PrintInfo(si);
            P("would block (still missing): " + string.Join(", ", Sockets.WouldBlockLive(sock.name).ToArray()));
            InstallCheck c = Sockets.CheckInstall(Sockets.DataFsm(sock));
            P("install check: known=" + c.Known + " allowed=" + c.Allowed + " blockers=[" + string.Join(", ", c.Blockers.ToArray()) + "] needs=[" + string.Join(", ", c.Needs.ToArray()) + "] " + c.Note);
            VinVerdict v = VinCheck.Check(sock, part, true);
            P("VIN check: checked=" + v.Checked + " pass=" + v.Pass + " fail=[" + string.Join(", ", v.FailKeys.ToArray()) + "] " + v.Note + " (" + v.Ms + " ms)");
            foreach (string r in v.Reasons) P("  reason: " + r);
            foreach (string u in v.Unknown) P("  unsupported action: " + u);
            if (v.Log != null) foreach (string l in v.Log) P("  " + l);
        }

        static void PrintInfo(SocketInfo si)
        {
            P(si.Name + ": order " + si.Order + (si.Optional ? ", optional" : "") + (si.HasRule ? "" : ", no install rule") +
              "\n  requires: " + string.Join(", ", si.Requires.ToArray()) +
              "\n  requires any: " + string.Join(", ", si.RequiresAny.ToArray()) +
              "\n  blocked by: " + string.Join(", ", si.BlockedBy.ToArray()) +
              "\n  placement: resetPos=" + si.ResetPos + " resetRot=" + si.ResetRot);
        }

        static void Socket(string name)
        {
            P("--- socket " + name);
            if (!name.StartsWith("VINP_")) name = "VINP_" + name;
            SocketInfo si;
            if (!Sockets.Info.TryGetValue(name, out si)) { P("unknown socket " + name); Say("unknown socket " + name); return; }
            PrintInfo(si);
            int n = 0;
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                if (!Sockets.IsSocket(f) || f.gameObject.name != name || !f.gameObject.activeInHierarchy) continue;
                InstallCheck c = Sockets.CheckInstall(f);
                n++;
                P("  live " + FullPath(f.transform) + ": installed=" + Sockets.Installed(f) + " allowed=" + c.Allowed +
                  " blockers=[" + string.Join(", ", c.Blockers.ToArray()) + "] needs=[" + string.Join(", ", c.Needs.ToArray()) + "] " + c.Note);
            }
            Say(name + ": " + n + " live instance(s)");
        }

        static string FullPath(Transform t)
        {
            string s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        static void ListCmd(GuideBehaviour b)
        {
            P("--- missing on the whole car");
            if (b == null) return;
            List<MissingItem> l = b.CarList;
            P("Car - " + l.Count + " missing:");
            foreach (MissingItem it in l) P((it.CanNow ? (it.WouldBlock.Count > 0 ? "  [after " + string.Join(", ", it.WouldBlock.ToArray()) + "] " : "  [now] ") : "        ") + it.Text);
            Say("car: " + l.Count + " missing");
        }

        static void Vin()
        {
            P("--- VIN");
            GameObject plate = GameObject.Find("CARPARTS/VINPlate");
            if (plate == null) { P("VINPlate not found"); return; }
            foreach (PlayMakerHashTableProxy p in plate.GetComponents<PlayMakerHashTableProxy>())
            {
                if (p.hashTable == null) continue;
                P("VIN table '" + p.referenceName + "':");
                foreach (DictionaryEntry e in p.hashTable) P("  " + e.Key + " = " + e.Value);
            }
        }
    }
}

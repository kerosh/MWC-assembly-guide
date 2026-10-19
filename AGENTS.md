# AGENTS.md - instructions for AI coding agents

**Assembly Guide** is an MSCLoader mod for *My Winter Car*: ghost of the held part at its socket (green/yellow/red), a missing-parts list and x-ray bolt marks.
Read `README.md` for what it does and `docs/ARCHITECTURE.md` for how. `docs/GAME_INTERNALS.md` holds the reverse-engineered facts; trust it over guesses.
`docs/DEVELOPMENT.md` has the toolchain, test loop and RE tools.

## Hard constraints
- C# 5 on .NET 3.5 / Mono 2.0, Unity 5.0.0f4: no `=>` members, `?.`, `$""`, `nameof`, auto-property initialisers, no .NET 4 APIs.
- Only build path is `build.bat` (Windows csc.exe against the game DLLs). No compiler in the Linux sandboxes - review carefully, user builds.
- `Assembly-CSharp` has global-namespace classes (e.g. `Setup`): write `Mod.Setup.X`, and keep `using HutongGames.PlayMaker(.Actions)` *inside* the namespace.
- Keep the AI disclosure in `Description` and `[assembly: AssemblyTrademark("AI generated code (Claude)")]`.
- **No hardcoded part rules.** Everything is read from the game's FSMs at runtime (owner requirement: must work for every save and after updates).

## Pieces
- `FsmSim.cs` - sandboxed PlayMaker interpreter (private variable store + hashtable overlay, never runs game actions). Values carry a source
  (Plain/Table/Other/Target). With `Wildcards`, decisions on *other* parts' values fork and the best case for the target part is kept.
- `Sockets.cs` - `VINP_*` sockets (FSM "Data"), static rules from state `Allow install?` (requires / blocked by / alternatives), topological
  assembly order, live install check (sim with `StopOnExit`), missing list.
- `VinCheck.cs` - runs `INSPECTION/Functions/InspectionMuseum` FSM "Inspect" with the held part placed in its socket; result keys decided by
  the states that read the part. Tuning sockets (`BoolNoneTrue` over Installed in that FSM) = optional + never original.
- `GuideBehaviour.cs` - held part (`PickUp` FSM on `.../1Hand_Assemble/Hand`, var `PickedObject`, state contains "picked"), part's socket =
  part `Data.InstallPoint`, ghost via `Graphics.DrawMesh` with a runtime fixed-function shader, OnGUI text/list with dark background.
  Missing list = target under the crosshair (RaycastAll, nearest non-trigger): nearest PART ancestor that has sockets, else car sockets within a radius of the hit point.
- `Bolts.cs` - bolt/adjustment classification from FSM shape (see README); marks drawn in OnGUI via WorldToScreenPoint = x-ray.
- Console `aguide` writes details to `aguide_dump.txt` in the mod settings folder; only a summary goes to the in-game console (it is small and cannot be copied).

## Owner preferences
- Hotkeys: Ctrl + Numpad. Text needs a dark background. Owner tests in game and builds with `build.bat`; give short concrete test steps.

## Useful paths
- Game: `<game folder>`, typically `<Steam library>\steamapps\common\My Winter Car`. The repo is usually cloned inside it (`<game folder>\AssemblyGuide_src\`); otherwise set `MWC_DIR`.
- Decompiled game: `<game>\assetRipper\mywintercar\ExportedProject\Assets\Scripts\…`, scene YAML in `Assets\_Scenes\GAME.unity`, prefabs in `Assets\Prefab\`.
- Diagnostics: `<game>\Mods\Config\Mod Settings\AssemblyGuide\aguide_dump.txt` (read it instead of asking the owner to copy console text). Logs: `<game>\output_log.txt`.

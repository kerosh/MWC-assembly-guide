# Development guide

## Toolchain (no Visual Studio needed)
`build.bat` uses the C# compiler that ships with Windows, `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. It compiles against the
**game's own** assemblies in `<game>\mywintercar_Data\Managed\`:

- `mscorlib.dll`, `System.dll`, `System.Core.dll` (the Mono 2.0 / .NET 3.5 profile: `/nostdlib+ /noconfig`)
- `UnityEngine.dll`, `PlayMaker.dll`, `Assembly-CSharp.dll` (PlayMaker actions and collection proxies live here), `MSCLoader.dll`

### Language limits: C# 5 (`/langversion:5`) on .NET 3.5
- **Syntax:** no `=>` members, `?.`, `$""`, `nameof` or auto-property initializers.
- **APIs:** no .NET 4 APIs (`string.IsNullOrWhiteSpace`, `Tuple`, `Task`…). `HashSet`, `Func`, `Action<T1,T2>` are in System.Core 3.5 and fine.
- **Name clashes:** `Assembly-CSharp` has global-namespace classes (e.g. `Setup`). Write `Mod.Setup.X`, and keep
  `using HutongGames.PlayMaker(.Actions)` *inside* the namespace so those win over global names.
- A static member can't use the instance property `ConsoleCommand.Help`.

## Build
1. Put the repo folder **inside the game folder**: `<game>\AssemblyGuide_src\`. Or set the environment variable `MWC_DIR` to the game folder.
2. Double-click `build.bat`. The output goes to `bin\AssemblyGuide.dll` and is copied to `<game>\Mods\`.
3. Compiler messages are in `build_log.txt` (console OEM code page, e.g. cp866 on Russian Windows).

The copy into `Mods\` fails while the game is running, because the DLL is locked. Quit the game first.

## Test loop
1. Build, start the game, load a save. After about 4 s the console prints
   `[AssemblyGuide] N sockets, N with install rules, N alternative groups; museum inspection: N states, start '...', N tuning sockets`.
2. Pick up parts: head gasket with the head on/off (red/green), a non-original grille or carburettor (yellow).
3. Ctrl+Numpad7 and look at the block, the head, the car body; Ctrl+Numpad8 and look at the head (bolts + valve adjusters).
4. Ctrl+Numpad9 writes `aguide_dump.txt` in `<game>\Mods\Config\Mod Settings\AssemblyGuide\`: held part, install rules, full VIN-check
   trace with "unsupported action" lines and timing, VIN table, whole-car list, assembly order.
5. `<game>\output_log.txt` has the Unity / MSCLoader log; mod messages are prefixed `[AssemblyGuide]`.

## Reverse-engineering the game
Export the game with AssetRipper into `<game>\assetRipper\`. Decompiled scripts are in `ExportedProject\Assets\Scripts\<Assembly>\`,
scenes as YAML in `Assets\_Scenes\` (`GAME.unity` is about 240 MB), part prefabs in `Assets\Prefab\`.

Scripts in `tools/` (Python 3, no dependencies):
- `fsmdump.py GAME.unity "<path regex>"` – every PlayMaker FSM under matching GameObject paths: states, actions, string/object refs, transitions.
- `fsmraw.py GAME.unity "<exact path>" <FsmName>` – raw YAML of one FSM.
- `fsm_intdecode.py <raw.yaml>` – literal values of `IntCompare` / `IntSwitch` parameters (stored in `byteData`).

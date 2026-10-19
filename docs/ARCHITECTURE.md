# Architecture

```
 save loaded ──(4 s)──► GuideBehaviour.Init
                         ├─ VinCheck.Init   : find museum-inspection FSM, which states decide which result keys, tuning sockets
                         └─ Sockets.Scan    : all VINP_* sockets → install rules (requires / blocked by), alternatives, assembly order

 every frame (GuideBehaviour.Update)
   held part? (PickUp FSM "Part picked" + PickedObject tagged PART)
     ├─ socket = part Data.InstallPoint
     ├─ every 0.5 s : Sockets.CheckInstall(socket)     ── FsmSim, state "Allow install?", stop on first exit → red + reason
     ├─ once        : VinCheck.Check(socket, part)     ── FsmSim, whole "Inspect" FSM, held part placed in socket → yellow + reason
     └─ DrawGhost   : Graphics.DrawMesh of the part's meshes at the socket (runtime fixed-function transparent shader)
   every 0.3 s raycast from the camera (list and/or bolt marks on)
     ├─ assembly = nearest PART ancestor with sockets, else car sockets around the hit point → Sockets.MissingFrom → list
     └─ part     = nearest PART ancestor → Bolts.Collect → marks

 OnGUI: status text, list, bolt marks (WorldToScreenPoint, so they show through everything), all on a dark background
```

## FsmSim — the sandbox interpreter
The heart of the mod. It walks the states and actions of a real `PlayMakerFSM` **without running the game's actions**:
- reads go into a private variable store, writes stay there; hashtable writes go to a private overlay;
- every value carries a source: `Plain` (literal / own FSM), `Table` (hashtable, e.g. the VIN), `Other` (another part or socket) or
  `Target` (the held part, or its socket with the what-if override `ActivePart = held part`, `Installed = true`);
- supported actions: `GetFsm{Bool,Int,Float,String,GameObject}`, `HashTableGet/Set`, `Set{Bool,Int,Float,String}Value`, `BoolFlip`,
  `FloatAddMutiple`, `BuildStringFast`, `BoolTest`, `Bool{All,Any,None}True`, `StringCompare`, `StringSwitch`, `IntCompare`, `IntSwitch`,
  `FloatCompare`, `Wait`; known side-effect actions are skipped; unknown actions that can send events are reported (`aguide held`);
- **wildcards** (VIN check): a decision that depends only on *other* parts is forked and the branch that is best for the held part is
  kept ("assume everything else is right"). Before the held part is first read, forks just search for the path that reaches it.
  After that, forks are limited to states within two transitions of the states that read the held part, and to 200 forks in total.

## Socket rules (Sockets.cs)
Static analysis of each socket's `Allow install?` state: `GetFsmBool(<other socket>, "Data", "Installed")` feeding
- `BoolNoneTrue` → *blocked by* (this one goes first),
- `BoolAllTrue` / `BoolTest` leading on → *requires*, `BoolAnyTrue` → *requires one of*.

Two sockets that block each other are alternatives. Assembly order is a topological sort (requires first, blockers after),
stable in hierarchy order. The live check runs the state in `FsmSim` and reads the blockers / missing requirements from the trace.

## VIN check (VinCheck.cs)
Runs FSM `Inspect` on `INSPECTION/Functions/InspectionMuseum` from its global-transition target. A state that reads the held
part decides the result keys it writes itself, or those written one or two transitions later. The part fails if any of those keys is
not `true` in the best fork. Paint and upholstery reads are ignored (`FsmSim.IgnoredFsms`).

## Bolts (Bolts.cs)
Classification by FSM shape, cached per FSM: `Screw` with a `*Tightness*` variable and a literal limit in a compare/clamp → fastener;
`Screw` with `Alignment`/`AdjustmentStep`/`Rotation`, or `HandRotate`, or `Adjustment` → calibration. Only bolts whose nearest
`PART` ancestor is the looked-at part, plus the part's `Data.Bolts` container.

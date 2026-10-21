# My Winter Car internals used by this mod

Reverse-engineered from an AssetRipper export of the game (decompiled `Assembly-CSharp` / `PlayMaker`, `GAME.unity` YAML, part prefabs).
Engine: **Unity 5.0.0f4**, Mono 2.0 profile. Loader: MSCLoader 1.3.x.

## Parts and sockets
- Project car root: `CORRIS`. Parts are prefabs `VIN<id>` tagged `PART` (1xx engine, 2xx systems, 3xx suspension, 4xx body, 5xx interior)
  plus aftermarket prefabs (`CARB2BRLa0`, `HEADERSa0`…), spawned by `CARPARTS/PARTSYSTEM/SPAWNERS_*`. Start engine pieces sit in `CARPARTS/StartParts/`.
- **Install points ("sockets")** are GameObjects `VINP_<Name>` (about 190). Most sit under `CORRIS`, the rest are children of parts
  (engine block, cylinder head…), so they exist only where that part is. All are listed in `CORRIS/AssembyDatabase/VINPS` (ArrayList proxy).
- Socket FSM **`Data`**: bool `Installed`, `Bolted`, `Damaged`, float `Tightness`, `Wear`, GameObject `ActivePart`, `AssemblyPoint`.
  States `Idle` -(CHECK)-> **`Allow install?`** -> `Far` <-> `Near` -> `Install 1` -> `Install 2` -> `Installed`; global INSTALL / REMOVE / BACK.
- Part FSM **`Data`**: `Name`, `Installed`, `Type` (int), `Code` / `Model` (string), `Facelift`, GameObject **`InstallPoint`** (set by the
  spawner when the part is created), `Bolts`.
- Parts that fit several identical places have no `InstallPoint`; instead string `Data.ArrayReference` (e.g. `MainBearings`, `Pistons`,
  `Rockers`, `Wheels`) names an ArrayList proxy on `CORRIS/AssembyDatabase` listing the sockets (item 0 is empty). On pickup the part
  sends CHECK to an installer FSM which takes the closest socket from that list.
- Player: `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand`, FSM `PickUp`, var `PickedObject`, state `Part picked` while holding a part.
  It sends ASSEMBLING to the part, whose `Another part?` state sets the socket's `ActivePart` and sends CHECK.

- Installing a part changes its tag (it is no longer `PART`), so the mod identifies parts by their `Data` FSM having a `Name`
  variable. Many installed parts also switch their collider off.
- Socket state `Allow removal?`: leaving via FINISHED = can't be removed now (something blocks it).

## Install rules
In each socket's `Allow install?` state: `GetFsmBool(<other socket>, "Data", "Installed")` + `BoolNoneTrue` = blocked by
(e.g. head gasket blocked by cylinder head, crankshaft by main bearings / pistons / oil pan); `BoolTest` / `BoolAllTrue` = requires
(e.g. crank pulley needs crankshaft, fan belt needs alternator + crank pulley + water pump + pulley). Leaving to the start state (`Idle`) = refused.

## VIN plate and historical (museum) inspection
- `CARPARTS/VINPlate`: hashtable proxy **`VIN`** with keys Country, AssemblyPlant, Model, Body, Version, Year, Month, Serial, Drive, Engine,
  Gearbox, AxleRatio, AxleLock, ColorsBody, InteriorTrim, Radio, InstrumentPanel, Windshield, Seats, Suspension, PowerBrakes, Wheels,
  WindowHeater, VinylRoof. Rolled once per save from `VINPlate/Options{L,LX,SLX,GT}`.
- Version: D = L, E = LX, G = SLX, P = GT. Engine NA / NE, Gearbox B = manual, 7 = automatic.
- FSM **`Inspect`** on `INSPECTION/Functions/InspectionMuseum` compares VIN codes with the installed part's `Data` at specific sockets
  (`Type`, `Code`, `Model`, `Facelift`, wheel `TireType`, `Paint.Code`, `Upholstery.MaterialID`) and writes pass flags into hashtable `Results`
  (Engine, Gearbox, Axle, Brakes, ColorsBody, Seats, InteriorTrim, Windshield, Radio, RearWindow, InstrumentPanel, Wheels, Grille, Bumpers,
  Options, SteeringWheel, GearLever, Exhaust, Suspension, General). Any installed tuning part fails `General`.
  Example: engine NA needs carburettor and exhaust manifold `Type` 1, NE needs 2. Engine internals are not checked.

## Bolts
- Each bolt is an object `BoltPM` (under `<part>/Bolts/[Masked/]`) with FSM **`Screw`**: int `BoltTightness` 0..8 (state `Tight?`:
  IntCompare against 8), `ThisBolt` = moving mesh, `ThisPart` = owning part.
- Hand-screwed parts (oil filter, gear knob): FSM `Screw` on the part, float `Tightness` clamped 0..8; drain plug and some others: float `TightnessF`.
- Adjustment: `Screw` with `Alignment` (valve adjusters `ValveAdjustment/…`, carburettor idle/mixture, steering rack toe), `AdjustmentStep`
  (race shocks); FSM `HandRotate` (distributor timing, alternator belt), FSM `Adjustment` (4-barrel carburettor).

## PlayMaker serialization notes
- Uninitialized FSMs still expose `FsmStates`, transitions and variables; `FsmState.Actions` loads the actions on demand.
- Literal `FsmInt` values are stored in `byteData`: int32 little-endian value, then a `useVariable` byte, then the variable name
  (`tools/fsm_intdecode.py`).

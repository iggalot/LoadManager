# Load Manager: notes for assistants

- **Read first:** `..\DesignHub\docs\SUITE.md`: all seven repos, how they fit, the branches, the build, and **the
  owner's rules** (local commits only, no installs, stay in the project folders, never click the owner's windows).
- **Branch:** `dev/ui-pass-2`.
- **Projects:**
  - `LoadManager`, the engine (no UI):
    - `LoadSection`: the `Loads` section of the project file: level loads, zones, wall tops, the walls' dead load.
    - `GravityLoadPath`. What carries what is found from the geometry, level by level from the roof down: a beam is
      held up by every wall and column under it anywhere along it, and at an end with neither, by a beam it lands on
      partway along, else a beam whose end is there (`SupportsOf`, `BeamLoad.Supports`, each named "start", "end" or
      "at 12.5 ft"). Its reactions are its free body's (`BuildingModel.Statics.BeamStatics`, owner 2026-11-10): two
      supports by statics, an end past them a cantilever (a back support pulled up is said so); three or more a continuous
      beam, EI the same along it; a beam that cannot stand (fewer than two supports, or beams propping each other up over nothing) is the model's error `BeamNotHeldUp` (`BuildingModel.Statics.BeamSupports`, `ModelChecks`), and with any model error nothing is calculated (`Result.ModelErrors`, `FoundationCheck.NotChecked`; the window's red bar and its list, each error shown on its level when clicked; owner 2026-11-10). Beams are solved carried-before-carrier; beams bearing on each other round a loop (reciprocal
      framing) are solved together, going round until nothing changes (`SolveBeams`); a loop resting only on each
      other's ends is a mechanism, said so and not traced. A beam running along the joists or rafters carries half a
      spacing each side (`ModelDefaults.FloorJoistSpacing`, `RafterSpacing`), taken from the lines across the framing,
      **never from a member it carries** (owner 2026-11-10: a girder takes nothing off its beams; `Tributaries`).
    - `WindUplift` / `RoofWind`.
    - `LateralSection` (now in `BuildingModel.Core`, `BuildingModel.Model`; 2026-11-06): the shear walls' forces on the foundation and each point's connector, written by the Shear Wall Calculator. Every plan draws them on the Ground plan (the shared sketch).
    - The foundation tab shows the Ground plan (the lowest level), not Level 2.
    - `FoundationAnalysis` and `LoadCombinations`: ASD and strength, every wind case, the envelope.
  - `LoadManager.Wpf`: the controls.
  - `LoadManager.App`: `LoadManagerApp.exe`.
  - `LoadManager.Tests`.
- **The rules this app stands on:**
  - Loads live only here (the `Loads` section), never in the BuildingModel classes.
  - The wind is the wind calculator's own `WindCases`, by name, one combination per case, never a range. The shear walls'
    forces come once for each way they share a wind (`<name> (rigid)`, `<name> (flexible)`, `LateralSection.WindOf`): each
    is its own wind case here, with that wind's roof pressures (`FoundationAnalysis.WindCasesOf`).
  - On save the app re-reads the file and replaces only `Loads`. Other apps' changes are kept.
  - **The plan's selection is the shared sketch's** (`InitPicking` in `MainWindow.Loads.cs`): walls from the model, the
    zones (`zone:<i>`) and foundation points (`point:<from>`) as its own objects. Several walls: one top load for all;
    several zones: delete them all. Never add a picking of its own.
  - **One Load Manager per project:** it holds the `loads` project lock (`ProjectLock` in `BuildingModel.Core`). Someone else's project opens view only (`viewOnly`: `Edit` puts the loads back, the side panel read only, no saves).
- **The foundation with no window:** `FoundationCheck` (LoadManager core): `RoofWindOf` (the wind from `WindCheck`),
  `Analyze` (the load path, the uplift, `FoundationAnalysis`), `Run` (all of it, with given shear wall forces). The window
  uses `RoofWindOf` and `Analyze`; the hub uses `Run`. Uplift is a flag for the foundation design, not a fault. Stale shear
  wall forces: `ModelFingerprint.ChangedFromModel(lateral.BasedOn, b)`.
- **Free bodies are WISE, for the gravity and the wind alike, and the body is the selection** (owner): `FreeBody.OfBody`
  of the selected members. W the weight; E the loads applied (the floors, the roof, the wind: never dropped); S the
  foundation; I where the body is cut: the reaction of every member connected to it that is not selected (a wall under
  it or standing on it, a beam end, a column base). A connection between two selected members is a pair inside the body
  (`Inside`): its halves cancel, out of the sums, drawn only with the internal forces shown. Every force knows the member
  at its other end (`Other`; the load path records it: `ConcentratedLoad.Source` and `Onto`, `WallTopLoad.AboveParts`).
  The wind's side (`Connections`, `Shears`) is per wind case, from SWC's `Lateral` (`ConnectionPairs` in
  `BuildingModel.Core`). `ConnectionBalance` checks every member and the whole structure in every case (ΣF, ΣM about three
  axes: `FreeBody.WindOutOfBalance`, one rule for the check and the window); `FoundationCheck.Run` includes it. In the
  window: Ctrl or Shift + click members in the 3D foundation for one body; View > Internal forces is the plans' and the
  free bodies' one setting. In a wind case (`OfBody(..., uplift, windCase)`) a free body has the gravity, the roof's wind
  in that case (its uplift pass) and the shear walls' connections in it, so its combinations with W balance too
  (`Imbalance`, `ConnectionsUp`); in the window, the case picked under Wind is the combinations' W. Gravity free bodies check
  ΣFz and ΣM too (`FreeBody.OutOfBalance`), every member and the whole structure by `GravityBalance` (in `FoundationCheck.Run`).
  Upper beam ends and column bases carrying hold-downs: the net force on each support at worst (`UpperUplift`, step 5;
  SWC sizes their connection for the hold-downs alone: conservative, the owner's choice for now). Every diaphragm too
  (step 6): its own wind (`Lateral.Diaphragms`), the walls' shears under it and from above as pairs (`ShearPairs`, in
  `BuildingModel.Core`), ΣFx, ΣFy and ΣMz in the rigid cases (`ConnectionBalance.DiaphragmOutOfBalance`). The Foundation page steps up through the levels
  (the level bar; `OnFoundationPlan`: the footings drawn on the foundation's own plan only); a level's diaphragm clicked shows its
  free body in the side panel, one case at a time (`DiaphragmPanel`, `ConnectionBalance.DiaphragmForces`), the plan drawing the
  same case (`SketchCanvas.FreeBodyCase`). What carries
  each pair, in words, comes from the connection details (`WoodFraming.Connections`, `ConnectionsSection.Describer`: the
  file's "Connections", or a fresh design's passed to `FoundationCheck.Run` / `ConnectionBalance.Run`); the details are
  not the model's.
- **Start page:** `--page loads|foundation|foundation3d` after the project's path (the hub's steps), `ShowPage`.
- **Roof ties** (`WindUplift`, `RoofTie`; owner): the C&C uplift where it is higher than the MWFRS uplift at the same wall
  or beam, else the MWFRS (`Governs`); place by place along it too (`Along`, `RoofTieStep`: cut where any of them changes).
- **Build:** `MSBuild LoadManager.sln -restore`.
- **Test:** `vstest LoadManager.Tests\bin\Debug\net472\LoadManager.Tests.dll`.
- **The suite's TODO list** is `..\ShearWallCalculator\ROADMAP.md`.

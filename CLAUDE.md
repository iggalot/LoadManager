# Load Manager: notes for assistants

- **Read first:** `..\DesignHub\docs\SUITE.md`: all seven repos, how they fit, the branches, the build, and **the
  owner's rules** (local commits only, no installs, stay in the project folders, never click the owner's windows).
- **Branch:** `dev/ui-pass-2`.
- **Projects:**
  - `LoadManager`, the engine (no UI):
    - `LoadSection`: the `Loads` section of the project file: level loads, zones, wall tops, the walls' dead load.
    - `GravityLoadPath`.
    - `WindUplift` / `RoofWind`.
    - `LateralSection` (now in `BuildingModel.Core`, `BuildingModel.Model`; 2026-11-06): the shear walls' forces on the foundation and each point's connector, written by the Shear Wall Calculator. Every plan draws them on the Ground plan (the shared sketch).
    - The foundation tab shows the Ground plan (the lowest level), not Level 2.
    - `FoundationAnalysis` and `LoadCombinations`: ASD and strength, every wind case, the envelope.
  - `LoadManager.Wpf`: the controls.
  - `LoadManager.App`: `LoadManagerApp.exe`.
  - `LoadManager.Tests`.
- **The rules this app stands on:**
  - Loads live only here (the `Loads` section), never in the BuildingModel classes.
  - The wind is the wind calculator's own `WindCases`, by name, one combination per case, never a range.
  - On save the app re-reads the file and replaces only `Loads`. Other apps' changes are kept.
  - **The plan's selection is the shared sketch's** (`InitPicking` in `MainWindow.Loads.cs`): walls from the model, the
    zones (`zone:<i>`) and foundation points (`point:<from>`) as its own objects. Several walls: one top load for all;
    several zones: delete them all. Never add a picking of its own.
  - **One Load Manager per project:** it holds the `loads` project lock (`ProjectLock` in `BuildingModel.Core`).
- **The foundation with no window:** `FoundationCheck` (LoadManager core): `RoofWindOf` (the wind from `WindCheck`),
  `Analyze` (the load path, the uplift, `FoundationAnalysis`), `Run` (all of it, with given shear wall forces). The window
  uses `RoofWindOf` and `Analyze`; the hub uses `Run`. Uplift is a flag for the foundation design, not a fault. Stale shear
  wall forces: `ModelFingerprint.ChangedFromModel(lateral.BasedOn, b)`.
- **Free bodies are WISE, for the gravity and the wind alike** (owner): `FreeBody` (W weight, I internal at cuts, S
  supports, E applied). The wind is an external load and is never dropped. `FreeBody.Connections` and `Shears` are the
  wind's side, per wind case: E the wind's shear on a wall's top (`ShearWallDesign.ShearOnTop`) and the connections of the
  members standing on it, S its own hold-downs, compression posts and sill. Every connection is a pair of equal and
  opposite forces (`ConnectionPairs` in `BuildingModel.Core`, from SWC's `Lateral.Holdowns`); which side is I, S or E
  depends only on the body's boundary. `ConnectionBalance` checks every member in every case (ΣFx, ΣFy, ΣM:
  `FreeBody.WindOutOfBalance`, one rule for the check and the window); `FoundationCheck.Run` includes it.
- **Build:** `MSBuild LoadManager.sln -restore`.
- **Test:** `vstest LoadManager.Tests\bin\Debug\net472\LoadManager.Tests.dll`.
- **The suite's TODO list** is `..\ShearWallCalculator\ROADMAP.md`.

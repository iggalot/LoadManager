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
- **Build:** `MSBuild LoadManager.sln -restore`.
- **Test:** `vstest LoadManager.Tests\bin\Debug\net472\LoadManager.Tests.dll`.
- **The suite's TODO list** is `..\ShearWallCalculator\ROADMAP.md`.

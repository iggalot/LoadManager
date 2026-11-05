# Load Manager: notes for assistants

- **Read first:** `..\DesignHub\docs\SUITE.md`: all seven repos, how they fit, the branches, the build, and **the
  owner's rules** (local commits only, no installs, stay in the project folders, never click the owner's windows).
- **Branch:** `dev/ui-pass-2`.
- **Projects:**
  - `LoadManager`, the engine (no UI):
    - `LoadSection`: the `Loads` section of the project file: level loads, zones, wall tops, the walls' dead load.
    - `GravityLoadPath`.
    - `WindUplift` / `RoofWind`.
    - `LateralSection`: the shear walls' forces on the foundation, written by the Shear Wall Calculator.
    - `FoundationAnalysis` and `LoadCombinations`: ASD and strength, every wind case, the envelope.
  - `LoadManager.Wpf`: the controls.
  - `LoadManager.App`: `LoadManagerApp.exe`.
  - `LoadManager.Tests`.
- **The rules this app stands on:**
  - Loads live only here (the `Loads` section), never in the BuildingModel classes.
  - The wind is the wind calculator's own `WindCases`, by name, one combination per case, never a range.
  - On save the app re-reads the file and replaces only `Loads`. Other apps' changes are kept.
- **Build:** `MSBuild LoadManager.sln -restore`.
- **Test:** `vstest LoadManager.Tests\bin\Debug\net472\LoadManager.Tests.dll`.
- **The suite's TODO list** is `..\ShearWallCalculator\ROADMAP.md`.

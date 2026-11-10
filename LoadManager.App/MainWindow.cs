using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ASCE7WindLoadCalculator;
using ASCE7WindLoadCalculator.ModelAdapter;
using BuildingModel.Diagnostics;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using BuildingModel.Sketch;

namespace LoadManager.App
{
    /// <summary>
    /// The Load Manager: the gravity loads of the project (its "Loads" section of the .bldg.json: each level's area loads, load zones,
    /// the walls' own weight, loads entered by hand on a wall's top) edited on the building model's plan, and where they go: the load
    /// path to each wall, the wind uplift, and the foundation, every combination and its envelope, on the plan, along a wall and in 3D.
    /// The building itself is read only here (the Building Model app draws it); saving writes only the loads into the file.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private enum Mode { Loads, Foundation, Foundation3D }

        private readonly SketchCanvas sketch = new SketchCanvas { ShowIssues = false };
        private readonly Building3DView view3D = new Building3DView { Visibility = Visibility.Collapsed, Ghost = true };
        private readonly Wpf.EnvelopeDiagram diagram = new Wpf.EnvelopeDiagram { Height = 250 };
        private readonly Border diagramBox;
        private readonly StackPanel levelBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border levelBox;
        private readonly StackPanel side = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        private readonly TextBlock status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 10, 0) };
        private readonly ToggleButton loadsTab, foundationTab, tab3D;
        private readonly TextBlock legend3D = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Foreground = Ui.Ink, Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(10, 10, 10, 44), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };   // above the 3D view's bottom bar

        private ModelDocument doc;
        private string path;
        /// <summary>This app's hold on the open project (one Load Manager per project).</summary>
        private ProjectLock projectLock;
        private Mode mode = Mode.Loads;
        private string loadsLevelId;

        // the suite's undo (EditHistory), on this app's own part: snapshots of the "Loads" section; the building is not edited here
        private readonly EditHistory history = new EditHistory();
        private bool Dirty => history.IsDirty;

        // what the loads give (found again after every change)
        private GravityLoadPath.Result loadPath;
        private WindUplift uplift;
        private FoundationAnalysis foundation;
        private LateralSection lateral;
        private DesignMethod method = DesignMethod.Asd;

        // the wind calculator's roof pressures for this geometry (found when the building is read)
        private List<RoofWindCase> windCases;
        private Func<double, RoofWindCase> cladding;
        private string windWhy;

        private ProjectWatcher watcher;

        private Building B => sketch.Building;

        public MainWindow()
        {
            Width = 1440; Height = 880; MinWidth = 960; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowFit.Fit(this);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            Background = Brushes.White;

            var menu = new Menu { Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            MenuItem Item(ItemsControl parent, string header, string keys, Action a, string tip = null)
            {
                var m = new MenuItem { Header = header, InputGestureText = keys ?? "", ToolTip = tip };
                if (a != null) m.Click += (s, e) => a();
                parent.Items.Add(m);
                return m;
            }
            var file = Item(menu, "_File", null, null);
            Item(file, "_Open…", "Ctrl+O", OpenDialog, "A project's building model (.bldg.json)");
            Item(file, "_Save", "Ctrl+S", () => Save(), "Writes the loads into the project file (the building in it is left as it is)");
            Item(file, "_Reload the building", null, () => Reload(true), "Reads the building from the file again (another app has changed it); your load changes are kept");
            file.Items.Add(new Separator());
            Item(file, "E_xit", "Alt+F4", Close);
            var edit = Item(menu, "_Edit", null, null);
            Item(edit, "_Undo", "Ctrl+Z", Undo);
            Item(edit, "_Redo", "Ctrl+Y", Redo);
            var loads = Item(menu, "_Loads", null, null);
            Item(loads, "_Floor and roof loads…", null, AreaLoads, "Each level's area loads (D, L, Lr, S psf), the load zones and the walls' own weight");
            Item(loads, "Draw a load _zone", "R", () => StartZone(), "Drag a rectangle on the plan: an area with its own loads on this level");
            loads.Items.Add(new Separator());
            Item(loads, "_Wind uplift…", null, ShowUplift, "The roof wind against the dead load, 0.6D + 0.6W: the ties at the roof and down the load path");
            Item(loads, "Foundation loads (service)…", null, () => new Wpf.FoundationLoadsWindow(B) { Owner = this }.Show(), "What reaches the foundation, by kind of load, unfactored");
            Item(loads, "Foundation loads (_combined)…", null, () => ShowCombined(null), "Every combination with its load factors, each wall and point, and the envelope");
            var view = Item(menu, "_View", null, null);
            Item(view, "_Loads plan", null, () => SetMode(Mode.Loads));
            Item(view, "_Foundation plan", null, () => SetMode(Mode.Foundation));
            Item(view, "Foundation _3D", null, () => SetMode(Mode.Foundation3D));
            view.Items.Add(new Separator());
            Item(view, "_Zoom to the drawing", "Z", () => sketch.ZoomExtents());
            view.Items.Add(new Separator());
            var internalForces = sketch.InternalForcesMenuItem();
            // one setting for the plans and the free bodies
            internalForces.Click += (s, e) => { Title3D(); RefreshSide(); if (view3D.Visibility == Visibility.Visible) view3D.Show(B); };
            view.Items.Add(internalForces);

            var bar = new DockPanel { LastChildFill = false };
            bar.Children.Add(new TextBlock { Text = "Load Manager", FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
            bar.Children.Add(menu);
            loadsTab = Ui.Toggle("", "Loads", "The loads on each level's plan", (s, e) => SetMode(Mode.Loads));
            foundationTab = Ui.Toggle("", "Foundation", "What comes down onto the foundation, combined: the plan and each wall's envelope", (s, e) => SetMode(Mode.Foundation));
            tab3D = Ui.Toggle("", "Foundation 3D", "The foundation loads in 3D", (s, e) => SetMode(Mode.Foundation3D));
            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            tabs.Children.Add(loadsTab); tabs.Children.Add(foundationTab); tabs.Children.Add(tab3D);
            DockPanel.SetDock(tabs, Dock.Right);
            bar.Children.Add(tabs);
            var appBar = new Border { Child = bar, Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 3, 6, 3) };

            levelBox = new Border { Child = levelBar, Background = Ui.Bar, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 3, 8, 3) };
            diagramBox = new Border { Child = diagram, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed };
            // the plan's bottom bar, as in every view: VIEW, zoom to all, the mouse hint
            var planBar = Ui.ViewBar(sketch.ZoomExtents, Ui.PlanHint);
            var plan = new DockPanel();
            DockPanel.SetDock(levelBox, Dock.Top);
            DockPanel.SetDock(planBar, Dock.Bottom);
            DockPanel.SetDock(diagramBox, Dock.Bottom);
            plan.Children.Add(levelBox);
            plan.Children.Add(planBar);
            plan.Children.Add(diagramBox);
            plan.Children.Add(sketch);
            var center = new Grid();
            center.Children.Add(plan);
            center.Children.Add(view3D);
            center.Children.Add(legend3D);
            center.Children.Add(Title3DBox());

            var sideBorder = new Border
            {
                Width = 360, Background = Ui.Bar, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0),
                Child = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            var statusBar = new Border { Child = status, Background = Ui.Bar, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Height = 26 };

            var root = new DockPanel();
            DockPanel.SetDock(appBar, Dock.Top);
            DockPanel.SetDock(viewOnlyBanner, Dock.Top);
            DockPanel.SetDock(statusBar, Dock.Bottom);
            DockPanel.SetDock(sideBorder, Dock.Right);
            root.Children.Add(appBar);
            root.Children.Add(viewOnlyBanner);
            root.Children.Add(statusBar);
            root.Children.Add(sideBorder);
            root.Children.Add(center);
            Content = root;

            // the plan is for looking and picking (the shared sketch's selection): its drawing tools never get a key or typed text
            sketch.Cursor = Cursors.Arrow;
            sketch.PreviewMouseLeftButtonDown += PlanDown;
            sketch.PreviewMouseMove += PlanMove;
            sketch.PreviewMouseLeftButtonUp += PlanUp;
            InitPicking(); // the shared sketch picks: the walls, and this app's zones and foundation points
            sketch.PreviewMouseRightButtonDown += (s, e) => e.Handled = true;
            sketch.PreviewMouseRightButtonUp += (s, e) => e.Handled = true;
            sketch.PreviewTextInput += (s, e) => e.Handled = true;
            sketch.PreviewKeyDown += (s, e) => { if (!(e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.None)) e.Handled = true; };
            // the footings' loads on the foundation's own plan only; above it, the level's diaphragm and the hold-downs (its free body when picked)
            sketch.DrawOver += (s, e) => { if (mode == Mode.Loads) DrawLoads(e.Context); else if (mode == Mode.Foundation && OnFoundationPlan) DrawFoundation(e.Context); };
            sketch.ActiveLevelChanged += (s, e) =>
            {
                if (mode == Mode.Loads) loadsLevelId = sketch.ActiveLevel?.Id;
                if (mode == Mode.Loads || mode == Mode.Foundation) { sketch.ClearSelection(); UpdateLevelBar(); RefreshSide(); }
            };
            view3D.Extra = b => fb != null ? FreeBody3D() : Foundation3D();
            view3D.Picked += Picked3D;
            view3D.PickScreen = Pick3DAt;

            PreviewKeyDown += (s, e) =>
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
                else if (ctrl && e.Key == Key.O) { OpenDialog(); e.Handled = true; }
                else if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
                else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
                else if (e.Key == Key.Escape) { CancelZone(); Select(null, -1, null); e.Handled = true; }
                else if (!ctrl && e.Key == Key.R && mode == Mode.Loads && !(Keyboard.FocusedElement is TextBox)) { StartZone(); e.Handled = true; }
                else if (!ctrl && e.Key == Key.Delete && mode == Mode.Loads && selectedZones.Count > 0 && !(Keyboard.FocusedElement is TextBox)) { sketch.DeleteSelected(); e.Handled = true; } // the zones (the walls are the Building Model's)
                else if (ctrl && e.Key == Key.A && mode != Mode.Foundation3D && !(Keyboard.FocusedElement is TextBox)) { sketch.HandleKey(Key.A, ModifierKeys.Control); e.Handled = true; }
            };
            Closing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
            Closed += (s, e) => { watcher?.Dispose(); projectLock?.Dispose(); projectLock = null; };

            doc = new ModelDocument { Building = Building.New("No project") };
            sketch.SetBuilding(doc.Building, null);
            ResetHistory();
            SetMode(Mode.Loads);
            Changed();
            Say("Open a project's building model (File > Open). The building is drawn in the Building Model app; the loads are edited here.");
        }

        // ------------------------------------------------------------------ files

        private void OpenDialog()
        {
            if (!ConfirmDiscard()) return;
            string file = ProjectDialogs.OpenModelPath(this);
            if (file != null) Open(file);
        }

        /// <summary>
        /// The page to show (the hub's <c>--page</c>, its step's): "loads", "foundation" or "foundation3d"; anything else is
        /// left as it is (logged).
        /// </summary>
        /// <summary>A project is open (the lock taken, the file read).</summary>
        public bool HasProject => !string.IsNullOrEmpty(path);

        public void ShowPage(string page)
        {
            switch ((page ?? "").Trim().ToLowerInvariant())
            {
                case "loads": SetMode(Mode.Loads); break;
                case "foundation": SetMode(Mode.Foundation); break;
                case "foundation3d": SetMode(Mode.Foundation3D); break;
                default: Log.Warning("App", "No such page: " + page + " (loads, foundation or foundation3d)", null); break;
            }
        }

        public void Open(string file)
        {
            // one Load Manager per project (the same project again keeps this app's own lock)
            bool refused = false, lookOnly = false;
            var taken = projectLock != null && string.Equals(projectLock.ModelPath, file, StringComparison.OrdinalIgnoreCase)
                ? projectLock : ProjectDialogs.TakeLock(this, file, "loads", "Load Manager", true, out refused, out lookOnly);
            if (refused) return;
            SetViewOnly(lookOnly);
            try
            {
                ProjectFolder.OpenFor(file);
                var d = ModelFile.Load(file);
                if (projectLock != taken) projectLock?.Dispose();
                projectLock = taken;
                doc = d;
                path = file;
                sketch.SetBuilding(d.Building, null);
                loadsLevelId = sketch.ActiveLevel?.Id;
                ResetHistory();
                FindWind();
                Watch();
                Select(null, -1, null);
                Changed();
                sketch.ZoomExtents();
                Log.Info("Project", "Opened " + file);
                Say($"Opened {Path.GetFileName(file)} (revision {doc.Revision}).");
                // what the hub marked for this app's steps when it last checked (its "Hub.Status": the hub writes it, this app only reads)
                string hub = ProjectStatus.Read(d.Building)?.Words("Gravity loads", "Foundation loads");
                if (hub != null)
                {
                    Log.Warning("Project", hub.Replace("\n", " "), null);
                    Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(this, hub, "Open", MessageBoxButton.OK, MessageBoxImage.Warning)), DispatcherPriority.ApplicationIdle);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is System.Runtime.Serialization.SerializationException || ex is ArgumentException)
            {
                if (taken != projectLock) taken?.Dispose();
                Log.Warning("Project", "Could not open " + file, ex);
                MessageBox.Show(this, "This file could not be opened.\n\n" + ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Saves the loads: the file is read again (another app may have saved the building since), only its "Loads" section is
        /// replaced with these, and it is written back (its revision goes up, as for any save).
        /// </summary>
        private bool Save()
        {
            if (string.IsNullOrEmpty(path)) { MessageBox.Show(this, "Open a project first (File > Open).", "Save"); return false; }
            if (viewOnly)
            {
                MessageBox.Show(this, "View only: someone else is working on this project, so the loads are not saved from here.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            try
            {
                var fresh = ModelFile.Load(path);
                fresh.Building.SetData(LoadSection.Key, LoadsJson());
                ModelFile.Save(path, fresh);
                watcher?.SavedByMe();
                bool geometryChanged = ModelFile.BuildingToJson(WithoutLoads(fresh.Building)) != ModelFile.BuildingToJson(WithoutLoads(B));
                doc = fresh;
                if (geometryChanged) { string lvl = sketch.ActiveLevel?.Id; sketch.SetBuilding(fresh.Building, lvl); FindWind(); }
                else doc.Building = B;
                history.MarkSaved();
                Changed();
                Log.Info("Project", $"Loads saved to {path} (revision {doc.Revision}).");
                Say($"Loads saved (revision {doc.Revision}).");
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning("Project", "Could not save " + path, ex);
                MessageBox.Show(this, "The file could not be saved.\n\n" + ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private static Building WithoutLoads(Building b)
        {
            var c = ModelFile.Clone(b);
            c.SetData(LoadSection.Key, null);
            c.SetData(LateralSection.Key, null);
            c.SetData(WoodFraming.Connections.ConnectionsSection.Key, null);
            return c;
        }

        private bool ConfirmDiscard()
        {
            if (!Dirty || viewOnly) return true;
            return ProjectDialogs.SaveChangesFirst(this, "Save the load changes first?", "Load Manager", Save);
        }

        /// <summary>The file changed (the Building Model or the shear wall app saved it): read the building again, keeping unsaved loads.</summary>
        private void Watch()
        {
            watcher?.Dispose();
            watcher = null;
            if (string.IsNullOrEmpty(path)) return;
            watcher = new ProjectWatcher(path);
            watcher.ChangedOnDisk += (s, e) => Reload(false);
        }

        private void Reload(bool asked)
        {
            if (string.IsNullOrEmpty(path)) return;
            ModelDocument d;
            try { d = ModelFile.Load(path); }
            catch (IOException) { if (File.Exists(path)) watcher?.CheckAgain(); return; } // still being written: try again shortly
            catch (Exception ex) when (ex is InvalidDataException || ex is System.Runtime.Serialization.SerializationException || ex is ArgumentException)
            {
                Log.Warning("Project", "The changed file could not be read", ex);
                return;
            }
            string mine = LoadsJson();
            bool keep = Dirty && !viewOnly; // view only: theirs is read, nothing here is kept
            string lvl = sketch.ActiveLevel?.Id;
            doc = d;
            sketch.SetBuilding(d.Building, lvl);
            if (keep) B.SetData(LoadSection.Key, mine);
            else ResetHistory();
            FindWind();
            Changed();
            Log.Info("Project", $"Building read again from the file (revision {d.Revision}){(keep ? ", unsaved load changes kept" : "")}.");
            Say((asked ? "Read again from the file" : "The file was changed by another app and read again") + $" (revision {d.Revision})" + (keep ? "; your unsaved load changes are kept." : "."));
        }

        // ------------------------------------------------------------------ the loads' undo

        private string LoadsJson()
        {
            string j = B.GetData(LoadSection.Key);
            return string.IsNullOrEmpty(j) ? null : j;
        }

        private void ResetHistory() => history.Reset(LoadsJson());

        /// <summary>
        /// Someone else's project, opened to look at only (the suite's view-only mode): no lock, no saves, no "save first?",
        /// the side panel's boxes read only and every load edit put back (<see cref="Edit"/>); their saves are read as they
        /// make them. Opening the project again once they have closed it works on it.
        /// </summary>
        private bool viewOnly;
        public const string ViewOnlyMessage = "View only: someone else is working on this project, so the loads cannot be changed here.";
        private readonly Border viewOnlyBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x4A, 0x5C)), Padding = new Thickness(10, 5, 10, 5), Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "View only: someone else is working on this project. You can look at the loads and the foundation; nothing can be changed or saved here. Their saves show here as they make them. Open it again once they have closed it.",
                Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
            },
        };

        private void SetViewOnly(bool on)
        {
            viewOnly = on;
            sketch.ViewOnly = on;
            viewOnlyBanner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) Log.Info("Project", "View only: someone else is working on this project.");
        }

        /// <summary>View only: every box in the side panel read only (what it shows stays selectable and copyable).</summary>
        private void SideReadOnly()
        {
            if (!viewOnly) return;
            void Walk(DependencyObject d)
            {
                if (d is TextBox t) t.IsReadOnly = true;
                else if (d is ComboBox c) c.IsEnabled = false;
                else if (d is CheckBox k) k.IsEnabled = false;
                foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
            }
            Walk(side);
        }

        /// <summary>One edit of the loads (an undo step, named <paramref name="what"/>), made by <paramref name="change"/> on the building's data. False: nothing changed.</summary>
        private bool Edit(string what, Action<Building> change)
        {
            if (string.IsNullOrEmpty(path)) { Say("Open a project first (File > Open)."); return false; }
            change(B);
            if (viewOnly)
            {
                PutLoads(history.Current); // put back
                Changed();
                Log.Info("Loads", "Refused (view only): " + what);
                Say(ViewOnlyMessage);
                return false;
            }
            if (!history.Record(LoadsJson(), what)) return false;
            Log.Info("Loads", what);
            Changed();
            Say(what + " (Ctrl+Z undoes it.)");
            return true;
        }

        private void PutLoads(string json) => B.SetData(LoadSection.Key, string.IsNullOrEmpty(json) ? null : json);

        private void Undo()
        {
            if (!history.CanUndo) { Say("Nothing to undo."); return; }
            string what = history.UndoLabel;
            PutLoads(history.Undo());
            Changed();
            Say(what == null ? "Undone." : "Undone: " + what);
        }

        private void Redo()
        {
            if (!history.CanRedo) { Say("Nothing to redo."); return; }
            string what = history.RedoLabel;
            PutLoads(history.Redo());
            Changed();
            Say(what == null ? "Redone." : "Redone: " + what);
        }

        // ------------------------------------------------------------------ what the loads give

        /// <summary>The wind calculator's roof pressures for the building as it is, from the project's wind setup (the shared <see cref="FoundationCheck"/>).</summary>
        private void FindWind()
        {
            windWhy = FoundationCheck.RoofWindOf(B, WindCheck.Run(B), out windCases, out cladding);
            if (windWhy != null && WindSection.Read(B) != null) Log.Warning("Wind", windWhy, null);
        }

        /// <summary>After every change of the loads or the building: the load path, the uplift, the foundation, and what is shown.</summary>
        private void Changed()
        {
            var s = LoadSection.Read(B);
            lateral = LateralSection.Read(B);
            string why = windWhy;
            foundation = FoundationCheck.Analyze(B, s, windCases, cladding, lateral, method, out loadPath, out uplift, ref why); // the hub's check runs the same
            if (why != windWhy) { windWhy = why; Log.Warning("Wind", windWhy, null); }
            FoundationScales();
            // the same combination by name after a change (the list changes with the method and the loads); else the envelope
            combo3D = combo3D == null ? null : foundation.Cases.FirstOrDefault(c => c.Name == combo3D.Name);
            RefreshFreeBody();
            Title3D();
            sketch.Select(sketch.Selection.ToList()); // what is still there stays selected (an undo may have taken a zone or a wall)
            Title = $"{(string.IsNullOrEmpty(path) ? "No project" : Path.GetFileName(path))}{(Dirty ? " *" : "")}{(viewOnly ? " (view only)" : "")} — Load Manager";
            UpdateLevelBar();
            RefreshSide();
            diagram.Highlight = selectedPoint;
            diagram.Show(foundation.Walls.FirstOrDefault(w => w.Id == selectedWall), selectedWall == null ? null : DiagramTitle());
            sketch.Refresh();
            legend3D.Text = $"The foundation's loads, under the building drawn see-through (the footings grey). Blue, on top of the footings: the loads from above, " +
                            $"arrows the way each acts (down; up where it lifts, beside it). Red, under the footings: the reactions, equal and opposite. " +
                            $"Line loads are rows of light arrows, their tails joined ({1.6 * lineDepthFt:0.#} ft tall for {maxLine:N0} plf); point loads are single full-colour arrows ({2.2 * Math.Max(4, lineDepthFt * 2):0} ft long for {maxPoint:N0} lb). The numbers are on the Foundation plan.";
            if (view3D.Visibility == Visibility.Visible) view3D.Show(B);
        }

        // ------------------------------------------------------------------ modes and levels

        private void SetMode(Mode m)
        {
            CancelZone();
            mode = m;
            loadsTab.IsChecked = m == Mode.Loads;
            foundationTab.IsChecked = m == Mode.Foundation;
            tab3D.IsChecked = m == Mode.Foundation3D;
            bool plan = m != Mode.Foundation3D;
            sketch.Visibility = plan ? Visibility.Visible : Visibility.Collapsed;
            levelBox.Visibility = m == Mode.Loads || m == Mode.Foundation ? Visibility.Visible : Visibility.Collapsed;
            diagramBox.Visibility = m == Mode.Foundation ? Visibility.Visible : Visibility.Collapsed;
            view3D.Visibility = plan ? Visibility.Collapsed : Visibility.Visible;
            legend3D.Visibility = fb == null ? view3D.Visibility : Visibility.Collapsed;
            title3DBox.Visibility = view3D.Visibility;
            ((FrameworkElement)sketch.Parent).Visibility = plan ? Visibility.Visible : Visibility.Collapsed;
            if (m == Mode.Loads)
            {
                if (loadsLevelId != null) sketch.ShowLevel(loadsLevelId);
            }
            else
            {
                // the foundation plan: the lowest level, showing the walls and columns standing on it and the hold-downs
                var ground = B.LevelsBottomUp.FirstOrDefault();
                if (ground != null) sketch.ShowLevel(ground.Id);
            }
            UpdateLevelBar(); // the Foundation page's bar has the foundation itself, the Loads page's the levels above
            if (m == Mode.Foundation3D) view3D.Show(B);
            if (plan) Dispatcher.BeginInvoke(new Action(() => sketch.ZoomExtents()), DispatcherPriority.Loaded);
            sketch.Select(selectedWall == null ? null : new[] { selectedWall });
            RefreshSide();
            sketch.Refresh();
        }

        /// <summary>The Foundation page shows the foundation's own plan (the lowest level), not a level above it.</summary>
        private bool OnFoundationPlan => sketch.ActiveLevel == null || sketch.ActiveLevel.Id == B.LevelsBottomUp.FirstOrDefault()?.Id;

        private void UpdateLevelBar()
        {
            levelBar.Children.Clear();
            levelBar.Children.Add(new TextBlock { Text = "Level:", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var roof = B.RoofLevel();
            var lowest = B.LevelsBottomUp.FirstOrDefault();
            bool fdn = mode == Mode.Foundation;
            foreach (var l in B.LevelsBottomUp.Where(l => l.Elevation > 0 || (fdn && l == lowest)).OrderByDescending(l => l.Elevation))
            {
                var id = l.Id;
                string tip = fdn
                    ? (l == lowest ? "The foundation: the footings' loads and the hold-downs on it" : $"{l.Name}'s {(l == roof ? "roof" : "floor")}: click its diaphragm for its free body in the wind, the hold-downs on it")
                    : $"The loads on {l.Name}'s {(l == roof ? "roof" : "floor")} ({l.Elevation:0.##} ft) and the walls under it";
                var t = Ui.Toggle("", fdn && l == lowest ? "Foundation" : l.Name + (l == roof ? " (roof)" : ""), tip, (s, e) => sketch.ShowLevel(id));
                t.IsChecked = sketch.ActiveLevel?.Id == id;
                levelBar.Children.Add(t);
            }
        }

        private void Say(string text) => status.Text = text;

        // ------------------------------------------------------------------ the side panel's pieces

        private static TextBlock Head(string text) => new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10.5, Foreground = Ui.Muted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        private static TextBlock Para(string text, bool muted = false, bool bold = false) =>
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Ui.Muted : Ui.Ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 4) };
        private static Button Link(string text, string tip, Action a)
        {
            var b = new Button { Content = text, ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 6, 4) };
            b.Click += (s, e) => a();
            return b;
        }

        /// <summary>A small table: rows of cells, the first row the header.</summary>
        private static Grid Mini(IList<string[]> rows, double[] widths)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < rows.Count; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < rows[r].Length && c < widths.Length; c++)
                {
                    var t = new TextBlock { Text = rows[r][c] ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 6, 1), FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal, Foreground = r == 0 ? Ui.Muted : Ui.Ink };
                    if (c > 0) t.TextAlignment = TextAlignment.Right;
                    Grid.SetRow(t, r); Grid.SetColumn(t, c);
                    g.Children.Add(t);
                }
            }
            return g;
        }

        private static TextBox NumberBox(double v, string name)
        {
            var t = new TextBox { Text = v == 0 ? "0" : v.ToString("0.##"), Width = 52, Margin = new Thickness(0, 0, 4, 0), TextAlignment = TextAlignment.Right };
            System.Windows.Automation.AutomationProperties.SetName(t, name);
            return t;
        }

        private static bool Number(TextBox t, out double v)
        {
            bool ok = double.TryParse(t.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out v) && v >= 0 && v < 1e6;
            t.Background = ok ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF6, 0xD5, 0xD5));
            return ok;
        }

        private static string Loads(GravityLoad g, string unit)
        {
            if (g == null) return "none";
            var parts = new List<string>();
            if (g.Dead > 0) parts.Add($"D {g.Dead:N0}");
            if (g.Live > 0) parts.Add($"L {g.Live:N0}");
            if (g.RoofLive > 0) parts.Add($"Lr {g.RoofLive:N0}");
            if (g.Snow > 0) parts.Add($"S {g.Snow:N0}");
            if (g.Wind != 0) parts.Add($"W {g.Wind:N0}");
            return parts.Count == 0 ? "none" : string.Join(" · ", parts) + " " + unit;
        }

        private static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double ToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            return Dist(p, new Point2(a.X + t * dx, a.Y + t * dy));
        }
    }
}

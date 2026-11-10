using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Sketch;

namespace LoadManager.App
{
    public sealed partial class MainWindow
    {
        private static readonly Color ZoneColor = Color.FromRgb(0xE6, 0x9F, 0x00);   // amber (Okabe-Ito)
        private static readonly Color HandColor = Color.FromRgb(0x8A, 0x5A, 0x00);

        private string selectedWall;
        private int selectedZone = -1;
        private FoundationPoint selectedPoint;
        /// <summary>The level whose diaphragm is picked alone on the Foundation plan (its free body in the wind), else null.</summary>
        private string selectedDiaphragm;

        private bool drawingZone;
        private Point2? zoneFrom;
        private Point2 zoneTo;
        private bool showWallLoads = true;

        /// <summary>The walls picked together (two or more), and the zones (by index).</summary>
        private List<string> selectedWalls = new List<string>();
        private List<int> selectedZones = new List<int>();

        // the Load Manager's own objects in the plan's selection (the shared sketch's: click, Shift+click, a window, Ctrl+A, Del)
        private const string ZonePrefix = "zone:", PointPrefix = "point:";
        private static string ZoneId(int i) => ZonePrefix + i;
        private static string PointId(FoundationPoint p) => PointPrefix + p.From;
        private static int ZoneIndex(string id) => id != null && id.StartsWith(ZonePrefix) && int.TryParse(id.Substring(ZonePrefix.Length), out int i) ? i : -1;
        private FoundationPoint PointOf(string id) => id != null && id.StartsWith(PointPrefix) ? foundation?.AllPoints.FirstOrDefault(p => PointPrefix + p.From == id) : null;

        /// <summary>What the plan's selection is, from the sketch's (the one selection mechanism of the suite).</summary>
        private void FromSketchSelection()
        {
            var host = sketch.SelectedHostIds;
            selectedWalls = sketch.SelectedOf<Wall>().Select(w => w.Id).OrderBy(x => x).ToList();
            selectedZones = host.Select(ZoneIndex).Where(i => i >= 0).OrderBy(i => i).ToList();
            var points = host.Select(PointOf).Where(p => p != null).ToList();
            selectedPoint = points.Count == 1 && selectedWalls.Count <= 1 ? points[0] : null;
            selectedWall = selectedWalls.Count == 1 ? selectedWalls[0] : selectedPoint?.WallId;
            selectedZone = selectedZones.Count == 1 && selectedWalls.Count == 0 ? selectedZones[0] : -1;
            var diaphragms = sketch.SelectedOf<Diaphragm>().Select(d => d.LevelId).Distinct().ToList();
            selectedDiaphragm = diaphragms.Count == 1 && selectedWalls.Count == 0 && selectedPoint == null ? diaphragms[0] : null;
            diagram.Highlight = selectedPoint;
            diagram.Show(foundation?.Walls.FirstOrDefault(w => w.Id == selectedWall), selectedWall == null ? null : DiagramTitle());
            RefreshSide();
            sketch.Refresh();
        }

        /// <summary>Selects one wall, zone or point (null / -1: none) in the plan, as a click does.</summary>
        private void Select(string wall, int zone, FoundationPoint point)
        {
            var ids = new List<string>();
            if (wall != null) ids.Add(wall);
            if (zone >= 0) ids.Add(ZoneId(zone));
            if (point != null) ids.Add(PointId(point));
            sketch.Select(ids); // SelectionChanged: FromSketchSelection
        }

        // ------------------------------------------------------------------ picking on the plan

        private static Point2 Snap(Point2 p) => new Point2(Math.Round(p.X * 2) / 2, Math.Round(p.Y * 2) / 2);

        /// <summary>
        /// Drawing a zone takes the click; otherwise the shared sketch picks (a click, Shift+click or Ctrl+click, a window, Ctrl+A):
        /// the walls, and this app's own zones and foundation points (<see cref="InitPicking"/>).
        /// </summary>
        private void PlanDown(object sender, MouseButtonEventArgs e)
        {
            sketch.Focus();
            if (!(mode == Mode.Loads && drawingZone)) return;
            e.Handled = true;
            zoneFrom = Snap(sketch.ToWorld(e.GetPosition(sketch)));
            zoneTo = zoneFrom.Value;
            sketch.CaptureMouse();
        }

        /// <summary>The shared sketch's selection, given this app's objects: walls only from the model; zones (Loads), points (Foundation).</summary>
        private void InitPicking()
        {
            sketch.GeometryLocked = true; // the plan is for looking and picking: no grips, nothing moved
            sketch.GeometryLockedMessage = "The building is edited in the Building Model.";
            sketch.PickFilter = o => o is Wall || (mode == Mode.Foundation && o is Diaphragm); // a diaphragm: its free body in the wind
            sketch.HostPickFirst = true; // a foundation point stands on its wall: the point first
            sketch.HostOwns = id => ZoneIndex(id) is int zi && zi >= 0 ? mode == Mode.Loads && zi < (LoadSection.Read(B).Zones?.Count ?? 0) : mode == Mode.Foundation && PointOf(id) != null;
            sketch.HostPick = (at, tol) =>
            {
                if (mode == Mode.Foundation)
                {
                    if (!OnFoundationPlan) return null; // a level above: its diaphragm and walls, not the foundation's points
                    var p = foundation?.AllPoints.Select(x => (x, d: Dist(x.Where, at))).Where(t => t.d <= 1.5 * tol).OrderBy(t => t.d).FirstOrDefault().x;
                    return p == null ? null : PointId(p);
                }
                var lv = sketch.ActiveLevel;
                if (lv == null) return null;
                // a zone is an area: picked only off the walls (the walls come before it)
                if (B.WallsUnder(lv).Any(w => ToSegment(at, w.Start, w.End) <= tol)) return null;
                var zones = LoadSection.Read(B).Zones ?? new List<LoadZone>();
                int zi = zones.Select((z, i) => (z, i)).Where(t => t.z.LevelId == lv.Id && t.z.IsValid && at.X >= t.z.X0 && at.X <= t.z.X1 && at.Y >= t.z.Y0 && at.Y <= t.z.Y1)
                              .OrderBy(t => (t.z.X1 - t.z.X0) * (t.z.Y1 - t.z.Y0)).Select(t => t.i).DefaultIfEmpty(-1).First();
                return zi < 0 ? null : ZoneId(zi);
            };
            sketch.HostInWindow = box =>
            {
                if (mode == Mode.Foundation)
                    return !OnFoundationPlan ? new List<string>() : (foundation?.AllPoints ?? Enumerable.Empty<FoundationPoint>()).Where(p => box.Contains(p.Where.X, p.Where.Y)).Select(PointId).ToList();
                var lv = sketch.ActiveLevel;
                return (LoadSection.Read(B).Zones ?? new List<LoadZone>()).Select((z, i) => (z, i))
                    .Where(t => lv != null && t.z.LevelId == lv.Id && t.z.IsValid && box.Contains(t.z.X0, t.z.Y0) && box.Contains(t.z.X1, t.z.Y1)).Select(t => ZoneId(t.i)).ToList();
            };
            sketch.HostDeleteRequested += (s, ids) => DeleteZones(ids.Select(ZoneIndex).Where(i => i >= 0).ToList());
            sketch.SelectionChanged += (s, e) => FromSketchSelection();
        }

        private void PlanMove(object sender, MouseEventArgs e)
        {
            if (zoneFrom == null) return;
            zoneTo = Snap(sketch.ToWorld(e.GetPosition(sketch)));
            Say($"Load zone {Math.Abs(zoneTo.X - zoneFrom.Value.X):0.#} × {Math.Abs(zoneTo.Y - zoneFrom.Value.Y):0.#} ft");
            sketch.Refresh();
        }

        private void PlanUp(object sender, MouseButtonEventArgs e)
        {
            if (zoneFrom == null) return; // the sketch's own: the end of a window
            e.Handled = true;
            sketch.ReleaseMouseCapture();
            var a = zoneFrom.Value;
            zoneFrom = null;
            drawingZone = false;
            sketch.Cursor = Cursors.Arrow;
            var lv = sketch.ActiveLevel;
            if (lv == null || Math.Abs(zoneTo.X - a.X) < 1 || Math.Abs(zoneTo.Y - a.Y) < 1) { Say("A load zone needs at least 1 × 1 ft: drag a rectangle."); sketch.Refresh(); return; }
            var s = LoadSection.Read(B);
            var zones = (s.Zones ?? new List<LoadZone>()).ToList();
            var start = s.Level(lv.Id) ?? new AreaLoad();
            string name = "Zone " + (zones.Count + 1);
            while (zones.Any(z => z.Name == name)) name += "'";
            zones.Add(new LoadZone
            {
                LevelId = lv.Id, Name = name, X0 = Math.Min(a.X, zoneTo.X), Y0 = Math.Min(a.Y, zoneTo.Y), X1 = Math.Max(a.X, zoneTo.X), Y1 = Math.Max(a.Y, zoneTo.Y),
                Load = new AreaLoad { Dead = start.Dead, Live = start.Live, RoofLive = start.RoofLive, Snow = start.Snow },
            });
            if (Edit($"{name} drawn on {lv.Name} (its loads start as the level's: set them in the panel).", b => LoadSection.SetZones(b, zones)))
                Select(null, zones.Count - 1, null);
        }

        private void StartZone()
        {
            if (string.IsNullOrEmpty(path)) { Say("Open a project first (File > Open)."); return; }
            if (viewOnly) { Say(ViewOnlyMessage); return; }
            if (mode != Mode.Loads) SetMode(Mode.Loads);
            drawingZone = true;
            sketch.Cursor = Cursors.Cross;
            Say($"Drag a rectangle on {sketch.ActiveLevel?.Name}: the area of the zone (Esc cancels). Inside it its loads replace the level's.");
        }

        private void CancelZone()
        {
            if (!drawingZone && zoneFrom == null) return;
            drawingZone = false;
            zoneFrom = null;
            sketch.ReleaseMouseCapture();
            sketch.Cursor = Cursors.Arrow;
            sketch.Refresh();
        }

        private void DeleteZone(int i) => DeleteZones(new List<int> { i });

        /// <summary>Deletes load zones (by index): one step to undo.</summary>
        private void DeleteZones(List<int> which)
        {
            var zones = (LoadSection.Read(B).Zones ?? new List<LoadZone>()).ToList();
            var gone = which.Where(i => i >= 0 && i < zones.Count).Distinct().OrderByDescending(i => i).ToList();
            if (gone.Count == 0) return;
            var names = gone.OrderBy(i => i).Select(i => zones[i].Name).ToList();
            foreach (int i in gone) zones.RemoveAt(i);
            string what = names.Count <= 3 ? string.Join(", ", names) : $"{names.Count} load zones";
            if (Edit($"{what} deleted.", b => LoadSection.SetZones(b, zones))) Select(null, -1, null);
        }

        // ------------------------------------------------------------------ windows

        private void AreaLoads()
        {
            if (string.IsNullOrEmpty(path)) { Say("Open a project first (File > Open)."); return; }
            var w = new Wpf.AreaLoadsWindow(B, readOnly: viewOnly) { Owner = this };
            if (w.ShowDialog() != true) return;
            var levels = w.Levels;
            double wallDead = w.WallDead;
            var zones = w.Zones;
            string what = "Floor and roof loads: " + string.Join("; ", B.LevelsBottomUp.Where(l => levels.ContainsKey(l.Id)).Select(l => $"{l.Name} {levels[l.Id]} psf")) + $"; walls {wallDead:0.##} psf" +
                          (zones.Count > 0 ? $"; zones {string.Join(", ", zones.Select(z => z.Name))}." : ".");
            if (!Edit(what, b => { LoadSection.SetAreaLoads(b, levels, wallDead); LoadSection.SetZones(b, zones); }) && !viewOnly) Say("No change.");
        }

        private void ShowUplift() => new Wpf.WindUpliftWindow(B, uplift, windWhy) { Owner = this }.Show();

        private void ShowCombined(string wallId) => new Wpf.FoundationAnalysisWindow(foundation, wallId) { Owner = this }.Show();

        // ------------------------------------------------------------------ the side panel

        private void RefreshSide()
        {
            side.Children.Clear();
            if (string.IsNullOrEmpty(path))
            {
                side.Children.Add(Para("No project open.", bold: true));
                side.Children.Add(Para("File > Open: a project's building model (.bldg.json). The loads are saved in the same file, beside the building, for every app to read.", true));
                return;
            }
            ModelErrorsSide();
            if (mode == Mode.Loads) LoadsSide(); else FoundationSide();
            SideReadOnly();
        }

        /// <summary>
        /// The building model's errors (owner 2026-11-10): with any, nothing is calculated here (the load path, the foundation,
        /// the free bodies all refuse a model that is not valid). A red bar over the plan says so, the errors are marked on the
        /// plan, and each in the list, clicked, goes to its level and selects its members (a beam that cannot stand, ...).
        /// </summary>
        private void ModelErrorsSide()
        {
            var errors = GravityLoadPath.ModelErrorsOf(B);
            sketch.Issues = errors;
            sketch.ShowIssues = errors.Count > 0;
            modelErrorBanner.Visibility = errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (errors.Count == 0) return;
            ((TextBlock)modelErrorBanner.Child).Text =
                $"The building model has {errors.Count} error{(errors.Count == 1 ? "" : "s")}: nothing is calculated (no loads, no foundation) until " +
                $"{(errors.Count == 1 ? "it is" : "they are")} fixed in the Building Model (Model > Edit building). Click an error in the side panel to see it on the plan.";
            side.Children.Add(Para("The building model is not valid", bold: true));
            var list = new IssueList(sketch) { Margin = new Thickness(0, 0, 0, 12) };
            list.Show(errors);
            list.Picked += (s, i) => { UpdateLevelBar(); Say($"Shown on the plan: {string.Join(", ", i.Ids)} ({B.Level(i.LevelId)?.Name})."); };
            side.Children.Add(list);
        }

        private void LoadsSide()
        {
            var lv = sketch.ActiveLevel;
            var s = LoadSection.Read(B);
            if (lv == null || lv.Elevation <= 0) { side.Children.Add(Para("Pick a level above the ground.", true)); return; }
            bool isRoof = B.RoofLevel()?.Id == lv.Id;
            side.Children.Add(Head($"{lv.Name}, {(isRoof ? "roof" : "floor")} at {lv.Elevation:0.##} ft"));
            var a = s.Level(lv.Id);
            side.Children.Add(Para("Area loads: " + Loads(a, "psf") + $"; walls' own weight {s.WallDead:0.##} psf of wall."));
            side.Children.Add(Link("Floor and roof loads…", "Every level's area loads, the zones and the walls' own weight", AreaLoads));

            // zones
            var zones = s.Zones ?? new List<LoadZone>();
            var here = zones.Select((z, i) => (z, i)).Where(t => t.z.LevelId == lv.Id).ToList();
            side.Children.Add(Head($"Load zones on {lv.Name} ({here.Count})"));
            side.Children.Add(Para("A rectangle with its own area loads: inside it they replace the level's (a heavier storage area, a mechanical pad, a snow drift).", true));
            var draw = Link("Draw a zone (R)", "Drag a rectangle on the plan", StartZone);
            side.Children.Add(draw);
            foreach (var (z, i) in here)
            {
                int k = i;
                var b = new Button
                {
                    Content = $"{z.Name}: {Loads(z.Load, "psf")}", HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 1, 0, 1),
                    Background = selectedZones.Contains(k) ? Ui.Checked : Brushes.White, BorderBrush = selectedZones.Contains(k) ? Ui.CheckedEdge : Ui.Line,
                };
                b.Click += (o, e) =>
                {
                    // Shift or Ctrl adds the zone to the selection (or takes it out), as on the plan
                    if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0) Select(null, k, null);
                    else if (selectedZones.Contains(k)) sketch.Select(sketch.Selection.Where(x => x != ZoneId(k)).ToList());
                    else sketch.Select(new[] { ZoneId(k) }, add: true);
                };
                side.Children.Add(b);
            }
            if (selectedZone >= 0 && selectedZone < zones.Count && zones[selectedZone].LevelId == lv.Id) ZoneEditor(zones, selectedZone);
            else if (selectedZones.Count > 1) ZonesPanel(zones);

            // the selected wall
            if (selectedWalls.Count > 1) WallsPanel(selectedWalls.Select(B.Find).OfType<Wall>().ToList(), lv);
            else if (selectedWall != null && B.Find(selectedWall) is Wall w) WallPanel(w, lv);
            else
            {
                side.Children.Add(Head("Walls"));
                side.Children.Add(Para("Click a wall to see what it carries, where it comes from, and to enter its top load by hand. Shift+click or drag a window to pick several: one top load for all.", true));
            }
            var cb = new CheckBox { Content = "Show each wall's top load on the plan", IsChecked = showWallLoads, Margin = new Thickness(0, 10, 0, 0) };
            cb.Click += (o, e) => { showWallLoads = cb.IsChecked == true; sketch.Refresh(); };
            side.Children.Add(cb);
            if (loadPath != null && loadPath.Notes.Count > 0)
            {
                side.Children.Add(Head("Load path notes"));
                foreach (var n in loadPath.Notes) side.Children.Add(Para("• " + n, true));
            }
        }

        private void ZoneEditor(List<LoadZone> zones, int i)
        {
            var z = zones[i];
            side.Children.Add(Head(z.Name));
            var name = new TextBox { Text = z.Name, Margin = new Thickness(0, 0, 0, 4) };
            System.Windows.Automation.AutomationProperties.SetName(name, "Zone name");
            side.Children.Add(name);
            var place = new WrapPanel();
            var x0 = NumberBox(z.X0, "Zone x0"); var y0 = NumberBox(z.Y0, "Zone y0"); var x1 = NumberBox(z.X1, "Zone x1"); var y1 = NumberBox(z.Y1, "Zone y1");
            place.Children.Add(new TextBlock { Text = "From (x, y)", Width = 70, VerticalAlignment = VerticalAlignment.Center });
            place.Children.Add(x0); place.Children.Add(y0);
            place.Children.Add(new TextBlock { Text = "to", Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            place.Children.Add(x1); place.Children.Add(y1);
            place.Children.Add(new TextBlock { Text = "ft", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
            side.Children.Add(place);
            var loads = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            var boxes = new[] { NumberBox(z.Load?.Dead ?? 0, "Zone D"), NumberBox(z.Load?.Live ?? 0, "Zone L"), NumberBox(z.Load?.RoofLive ?? 0, "Zone Lr"), NumberBox(z.Load?.Snow ?? 0, "Zone S") };
            string[] sym = { "D", "L", "Lr", "S" };
            for (int k = 0; k < 4; k++) { loads.Children.Add(new TextBlock { Text = sym[k], Width = 18, VerticalAlignment = VerticalAlignment.Center }); loads.Children.Add(boxes[k]); }
            loads.Children.Add(new TextBlock { Text = "psf", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
            side.Children.Add(loads);
            var buttons = new WrapPanel();
            buttons.Children.Add(Link("Apply", "Keep these values", () =>
            {
                var all = new[] { x0, y0, x1, y1 }.Concat(boxes).ToList();
                var v = new double[all.Count];
                bool ok = true;
                for (int k = 0; k < all.Count; k++) ok &= Number(all[k], out v[k]);
                if (!ok) { Say("Each value must be a number, 0 or more."); return; }
                if (v[2] - v[0] < 1 || v[3] - v[1] < 1) { Say("The zone must be at least 1 × 1 ft: \"to\" past \"from\"."); return; }
                var list = zones.ToList();
                list[i] = new LoadZone { LevelId = z.LevelId, Name = string.IsNullOrWhiteSpace(name.Text) ? z.Name : name.Text.Trim(), X0 = v[0], Y0 = v[1], X1 = v[2], Y1 = v[3], Load = new AreaLoad { Dead = v[4], Live = v[5], RoofLive = v[6], Snow = v[7] } };
                if (!Edit($"{list[i].Name}: {list[i]}.", b => LoadSection.SetZones(b, list)) && !viewOnly) Say("No change.");
            }));
            buttons.Children.Add(Link("Delete", "Delete this zone (Del)", () => DeleteZone(i)));
            side.Children.Add(buttons);
        }

        /// <summary>Several zones picked together: their names, and Delete them all (one step to undo).</summary>
        private void ZonesPanel(List<LoadZone> zones)
        {
            var mine = selectedZones.Where(i => i < zones.Count).ToList();
            side.Children.Add(Head($"{mine.Count} load zones selected"));
            side.Children.Add(Para(string.Join(", ", mine.Select(i => zones[i].Name)) + ". Click one alone to edit its loads.", true));
            side.Children.Add(Link($"Delete the {mine.Count} zones", "Delete every zone selected (Del)", () => DeleteZones(mine)));
        }

        /// <summary>
        /// Several walls picked together: what each carries at this level, and one top load by hand for all of them (or cleared
        /// on all of them): one step to undo.
        /// </summary>
        private void WallsPanel(List<Wall> walls, Level lv)
        {
            side.Children.Add(Head($"{walls.Count} walls selected"));
            var s = LoadSection.Read(B);
            var rows = new List<string[]> { new[] { "Wall", "D", "L", "Lr", "S" } };
            foreach (var w in walls)
            {
                var g = loadPath != null && loadPath.TryGetValue(GravityLoadPath.Key(w.Id, lv.Id), out var r) ? r.Load : null;
                bool hand = s.WallTop(w.Id, lv.Id) != null;
                rows.Add(new[] { w.Id + (hand ? " (by hand)" : ""), $"{g?.Dead ?? 0:N0}", $"{g?.Live ?? 0:N0}", $"{g?.RoofLive ?? 0:N0}", $"{g?.Snow ?? 0:N0}" });
            }
            side.Children.Add(Para($"Top loads at {lv.Name} (plf):", true));
            side.Children.Add(Mini(rows, new double[] { 130, 0, 0, 0, 0 }));

            side.Children.Add(Head("Their top load by hand"));
            side.Children.Add(Para("The same values for every wall selected, at this level. Clear them to use the load path again.", true));
            var tops = walls.Select(w => s.WallTop(w.Id, lv.Id)).ToList();
            var first = tops.FirstOrDefault(t => t != null);
            bool same = first != null && tops.All(t => t != null && t.ToString() == first.ToString());
            var boxes = new[] { NumberBox(same ? first.Dead : 0, "Walls top D"), NumberBox(same ? first.Live : 0, "Walls top L"),
                                NumberBox(same ? first.RoofLive : 0, "Walls top Lr"), NumberBox(same ? first.Snow : 0, "Walls top S") };
            var wp = new WrapPanel();
            string[] sym = { "D", "L", "Lr", "S" };
            for (int k = 0; k < 4; k++) { wp.Children.Add(new TextBlock { Text = sym[k], Width = 18, VerticalAlignment = VerticalAlignment.Center }); wp.Children.Add(boxes[k]); }
            wp.Children.Add(new TextBlock { Text = "plf", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
            side.Children.Add(wp);
            var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(Link($"Set on all {walls.Count}", "Use these values for the top of every wall selected", () =>
            {
                var v = new double[4];
                bool ok = true;
                for (int k = 0; k < 4; k++) ok &= Number(boxes[k], out v[k]);
                if (!ok) { Say("Each value must be a number, 0 or more."); return; }
                var load = new LineLoad { Dead = v[0], Live = v[1], RoofLive = v[2], Snow = v[3] };
                if (!Edit($"{walls.Count} walls at {lv.Name}: top load by hand {load} plf.", b => { foreach (var w in walls) LoadSection.SetWallTop(b, w.Id, lv.Id, load); }) && !viewOnly) Say("No change.");
            }));
            if (tops.Any(t => t != null))
                buttons.Children.Add(Link("Clear on all (use the load path)", "Remove the entered values of every wall selected", () =>
                    Edit($"{walls.Count} walls at {lv.Name}: top loads by hand cleared (the load path's apply).", b => { foreach (var w in walls) LoadSection.SetWallTop(b, w.Id, lv.Id, null); })));
            side.Children.Add(buttons);
        }

        private void WallPanel(Wall w, Level lv)
        {
            side.Children.Add(Head($"Wall {w.Id}"));
            if (loadPath == null || !loadPath.TryGetValue(GravityLoadPath.Key(w.Id, lv.Id), out var r))
            {
                side.Children.Add(Para($"Nothing reaches its top at {lv.Name} (it is not a bearing wall here, or it does not run W-E or S-N).", true));
                return;
            }
            side.Children.Add(Para($"Top load at {lv.Name}: {Loads(r.Load, "plf")}" + (r.Entered ? " (entered by hand)" : ""), bold: true));
            side.Children.Add(Para(r.Describe(), true));
            var rows = new List<string[]> { new[] { "", "D", "L", "Lr", "S" } };
            string[] Row(string n, GravityLoad g) => new[] { n, $"{g?.Dead ?? 0:N0}", $"{g?.Live ?? 0:N0}", $"{g?.RoofLive ?? 0:N0}", $"{g?.Snow ?? 0:N0}" };
            rows.Add(Row($"From {lv.Name} (plf)", r.FromLevel));
            rows.Add(Row("From above (plf)", r.FromAbove));
            rows.Add(Row(r.Entered ? "By hand (plf)" : "The load path (plf)", r.Computed));
            side.Children.Add(Mini(rows, new double[] { 130, 0, 0, 0, 0 }));
            if (r.Profile.Steps.Count > 1)
            {
                side.Children.Add(Para("Along it (from its start):", true));
                side.Children.Add(Mini(new[] { new[] { "ft", "D", "L", "Lr", "S" } }.Concat(r.Profile.Steps.Select(st => new[] { $"{st.A:0.#}–{st.B:0.#}", $"{st.Load.Dead:N0}", $"{st.Load.Live:N0}", $"{st.Load.RoofLive:N0}", $"{st.Load.Snow:N0}" })).ToList(),
                                       new double[] { 130, 0, 0, 0, 0 }));
            }
            if (r.Points.Count > 0)
            {
                side.Children.Add(Para("Concentrated loads on it:", true));
                foreach (var p in r.Points) side.Children.Add(Para($"• {p.From} at {p.At:0.##} ft: {Loads(p.Load, "lb")}", true));
            }
            foreach (var n in r.Notes) side.Children.Add(Para("• " + n, true));

            // by hand
            side.Children.Add(Head("Its top load by hand"));
            side.Children.Add(Para("Replaces what the load path finds for this wall's top at this level (a load the model cannot see). Clear it to use the load path again.", true));
            var entered = LoadSection.Read(B).WallTop(w.Id, lv.Id);
            var boxes = new[] { NumberBox(entered?.Dead ?? 0, "Wall top D"), NumberBox(entered?.Live ?? 0, "Wall top L"), NumberBox(entered?.RoofLive ?? 0, "Wall top Lr"), NumberBox(entered?.Snow ?? 0, "Wall top S") };
            var wp = new WrapPanel();
            string[] sym = { "D", "L", "Lr", "S" };
            for (int k = 0; k < 4; k++) { wp.Children.Add(new TextBlock { Text = sym[k], Width = 18, VerticalAlignment = VerticalAlignment.Center }); wp.Children.Add(boxes[k]); }
            wp.Children.Add(new TextBlock { Text = "plf", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
            side.Children.Add(wp);
            var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(Link("Set by hand", "Use these values for its top", () =>
            {
                var v = new double[4];
                bool ok = true;
                for (int k = 0; k < 4; k++) ok &= Number(boxes[k], out v[k]);
                if (!ok) { Say("Each value must be a number, 0 or more."); return; }
                var load = new LineLoad { Dead = v[0], Live = v[1], RoofLive = v[2], Snow = v[3] };
                if (!Edit($"Wall {w.Id} at {lv.Name}: top load by hand {load} plf.", b => LoadSection.SetWallTop(b, w.Id, lv.Id, load)) && !viewOnly) Say("No change.");
            }));
            if (entered != null)
                buttons.Children.Add(Link("Clear (use the load path)", "Remove the entered values", () =>
                    Edit($"Wall {w.Id} at {lv.Name}: top load by hand cleared (the load path's applies).", b => LoadSection.SetWallTop(b, w.Id, lv.Id, null))));
            side.Children.Add(buttons);
        }

        // ------------------------------------------------------------------ the loads plan

        private void DrawLoads(DrawingContext ctx)
        {
            var lv = sketch.ActiveLevel;
            if (lv == null) return;
            var fill = new SolidColorBrush(Color.FromArgb(0x30, ZoneColor.R, ZoneColor.G, ZoneColor.B));
            var edge = new Pen(new SolidColorBrush(ZoneColor), 1.5) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };
            var edgeSel = new Pen(new SolidColorBrush(SketchCanvas.Palette.Select), 2.5);
            var ink = new SolidColorBrush(Color.FromRgb(0x5C, 0x40, 0x00));
            var zones = LoadSection.Read(B).Zones ?? new List<LoadZone>();
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z.LevelId != lv.Id || !z.IsValid) continue;
                var r = sketch.ScreenRect(new Rect2(z.X0, z.Y0, z.X1, z.Y1));
                ctx.DrawRectangle(fill, selectedZones.Contains(i) ? edgeSel : edge, r);
                sketch.Label(ctx, $"{z.Name}: {Loads(z.Load, "psf")}", new Point(r.Left + 6, r.Top + 12), ink, 10.5);
            }
            if (zoneFrom != null)
            {
                var a = zoneFrom.Value;
                var r = sketch.ScreenRect(new Rect2(Math.Min(a.X, zoneTo.X), Math.Min(a.Y, zoneTo.Y), Math.Max(a.X, zoneTo.X), Math.Max(a.Y, zoneTo.Y)));
                ctx.DrawRectangle(fill, new Pen(new SolidColorBrush(SketchCanvas.Palette.Preview), 1.5), r);
            }
            if (!showWallLoads || loadPath == null) return;
            var muted = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x33));
            var hand = new SolidColorBrush(HandColor);
            foreach (var w in B.WallsUnder(lv))
            {
                if (!loadPath.TryGetValue(GravityLoadPath.Key(w.Id, lv.Id), out var r) || r.Load.IsEmpty) continue;
                var m = new Point2((w.Start.X + w.End.X) / 2, (w.Start.Y + w.End.Y) / 2);
                var sp = sketch.ToScreen(m);
                bool across = w.Direction == WallDirection.SouthNorth;
                string text = (r.Entered ? "by hand: " : "") + Loads(r.Load, "plf");
                double wide = text.Length * 5.4 * sketch.LabelScale;   // about: left of an upright wall (its name is on the right), over a level one
                var at = across ? new Point(sp.X - 10 - wide, sp.Y - 6) : new Point(sp.X - wide / 2, sp.Y - 16 * sketch.LabelScale);
                if (at.X < 4) at = new Point(sp.X + 10, sp.Y + 18 * sketch.LabelScale);   // no room on the left: under its name
                sketch.Label(ctx, text, at, r.Entered ? hand : muted, 10);
            }
        }
    }
}

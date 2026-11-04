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

        private bool drawingZone;
        private Point2? zoneFrom;
        private Point2 zoneTo;
        private bool showWallLoads = true;

        private void Select(string wall, int zone, FoundationPoint point)
        {
            selectedWall = wall;
            selectedZone = zone;
            selectedPoint = point;
            sketch.Select(wall == null ? null : new[] { wall });
            diagram.Highlight = point;
            diagram.Show(foundation?.Walls.FirstOrDefault(w => w.Id == wall), wall == null ? null : DiagramTitle());
            RefreshSide();
            sketch.Refresh();
        }

        // ------------------------------------------------------------------ picking on the plan

        private static Point2 Snap(Point2 p) => new Point2(Math.Round(p.X * 2) / 2, Math.Round(p.Y * 2) / 2);

        private void PlanDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;   // never the sketch's own tools
            sketch.Focus();
            var at = sketch.ToWorld(e.GetPosition(sketch));
            if (mode == Mode.Loads && drawingZone)
            {
                zoneFrom = Snap(at);
                zoneTo = zoneFrom.Value;
                sketch.CaptureMouse();
                return;
            }
            double near = 8 / sketch.Zoom;
            if (mode == Mode.Foundation && foundation != null)
            {
                var p = foundation.AllPoints.Select(x => (x, d: Dist(x.Where, at))).Where(t => t.d <= 1.5 * near).OrderBy(t => t.d).FirstOrDefault().x;
                if (p != null) { Select(p.WallId ?? selectedWall, -1, p); return; }
                var w = foundation.Walls.Select(x => (x, d: ToSegment(at, x.Start, x.End))).Where(t => t.d <= near).OrderBy(t => t.d).FirstOrDefault().x;
                Select(w?.Id, -1, null);
                return;
            }
            var lv = sketch.ActiveLevel;
            if (lv == null) return;
            var wall = B.WallsUnder(lv).Select(x => (x, d: ToSegment(at, x.Start, x.End))).Where(t => t.d <= near).OrderBy(t => t.d).FirstOrDefault().x;
            if (wall != null) { Select(wall.Id, -1, null); return; }
            var zones = LoadSection.Read(B).Zones ?? new List<LoadZone>();
            int zi = zones.Select((z, i) => (z, i)).Where(t => t.z.LevelId == lv.Id && t.z.IsValid && at.X >= t.z.X0 && at.X <= t.z.X1 && at.Y >= t.z.Y0 && at.Y <= t.z.Y1)
                          .OrderBy(t => (t.z.X1 - t.z.X0) * (t.z.Y1 - t.z.Y0)).Select(t => t.i).DefaultIfEmpty(-1).First();
            Select(null, zi, null);
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
            e.Handled = true;
            if (zoneFrom == null) return;
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

        private void DeleteZone(int i)
        {
            var zones = (LoadSection.Read(B).Zones ?? new List<LoadZone>()).ToList();
            if (i < 0 || i >= zones.Count) return;
            string name = zones[i].Name;
            zones.RemoveAt(i);
            if (Edit($"{name} deleted.", b => LoadSection.SetZones(b, zones))) Select(null, -1, null);
        }

        // ------------------------------------------------------------------ windows

        private void AreaLoads()
        {
            if (string.IsNullOrEmpty(path)) { Say("Open a project first (File > Open)."); return; }
            var w = new Wpf.AreaLoadsWindow(B) { Owner = this };
            if (w.ShowDialog() != true) return;
            var levels = w.Levels;
            double wallDead = w.WallDead;
            var zones = w.Zones;
            string what = "Floor and roof loads: " + string.Join("; ", B.LevelsBottomUp.Where(l => levels.ContainsKey(l.Id)).Select(l => $"{l.Name} {levels[l.Id]} psf")) + $"; walls {wallDead:0.##} psf" +
                          (zones.Count > 0 ? $"; zones {string.Join(", ", zones.Select(z => z.Name))}." : ".");
            if (!Edit(what, b => { LoadSection.SetAreaLoads(b, levels, wallDead); LoadSection.SetZones(b, zones); })) Say("No change.");
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
            if (mode == Mode.Loads) LoadsSide(); else FoundationSide();
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
                    Background = k == selectedZone ? Ui.Checked : Brushes.White, BorderBrush = k == selectedZone ? Ui.CheckedEdge : Ui.Line,
                };
                b.Click += (o, e) => Select(null, k, null);
                side.Children.Add(b);
            }
            if (selectedZone >= 0 && selectedZone < zones.Count && zones[selectedZone].LevelId == lv.Id) ZoneEditor(zones, selectedZone);

            // the selected wall
            if (selectedWall != null && B.Find(selectedWall) is Wall w) WallPanel(w, lv);
            else
            {
                side.Children.Add(Head("Walls"));
                side.Children.Add(Para("Click a wall to see what it carries, where it comes from, and to enter its top load by hand.", true));
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
                if (!Edit($"{list[i].Name}: {list[i]}.", b => LoadSection.SetZones(b, list))) Say("No change.");
            }));
            buttons.Children.Add(Link("Delete", "Delete this zone (Del)", () => DeleteZone(i)));
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
                if (!Edit($"Wall {w.Id} at {lv.Name}: top load by hand {load} plf.", b => LoadSection.SetWallTop(b, w.Id, lv.Id, load))) Say("No change.");
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
                ctx.DrawRectangle(fill, i == selectedZone ? edgeSel : edge, r);
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

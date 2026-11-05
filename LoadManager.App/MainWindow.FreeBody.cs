using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildingModel.Geometry;
using BuildingModel.Sketch;

namespace LoadManager.App
{
    public sealed partial class MainWindow
    {
        /// <summary>The element whose free body is drawn in 3D (null: the foundation).</summary>
        private string fbId;
        private FreeBody fb;
        private List<LoadCombination> fbCombos = new List<LoadCombination>();
        /// <summary>The combination the free body is drawn in (null: service loads, D + L + Lr + S as entered).</summary>
        private LoadCombination fbCombo;

        /// <summary>The own weight's colour on a free body: green (loads applied are blue, the supports red).</summary>
        private static readonly Color WeightColor = Color.FromRgb(0x2E, 0x7D, 0x32);

        private void Picked3D(string id)
        {
            if (mode != Mode.Foundation3D || string.IsNullOrEmpty(id)) return;
            if (id == fbId) return;
            OpenFreeBody(id);
        }

        private void OpenFreeBody(string id)
        {
            var body = FreeBody.Of(B, LoadSection.Read(B), loadPath, id);
            if (body == null) { status.Text = $"{id}: not a wall, beam or column the load path knows."; return; }
            fbId = id;
            fb = body;
            fbCombos = fb.Combinations(method);
            fbCombo = fbCombos.FirstOrDefault();
            view3D.Only = id;
            view3D.DrawOnly = !(fb.Kind == "wall" && fbFraming);
            legend3D.Visibility = Visibility.Collapsed;
            Title3D();
            RefreshSide();
            view3D.Show(B);
        }

        private void CloseFreeBody()
        {
            if (fbId == null) return;
            fbId = null; fb = null; fbCombos = new List<LoadCombination>(); fbCombo = null;
            view3D.Only = null;
            view3D.DrawOnly = true;
            legend3D.Visibility = view3D.Visibility;
            Title3D();
            RefreshSide();
            view3D.Show(B);
        }

        /// <summary>After a change of the loads or the building: the free body again, or back to the foundation if the element is gone.</summary>
        private void RefreshFreeBody()
        {
            if (fbId == null) return;
            var body = FreeBody.Of(B, LoadSection.Read(B), loadPath, fbId);
            if (body == null) { fbId = null; fb = null; view3D.Only = null; fbCombo = null; return; }
            fb = body;
            string was = fbCombo?.Name;
            fbCombos = fb.Combinations(method);
            fbCombo = fbCombos.FirstOrDefault(c => c.Name == was) ?? fbCombos.FirstOrDefault();
        }

        private void StepFreeBody(int by)
        {
            if (fbCombos.Count == 0) return;
            int i = fbCombo == null ? -1 : fbCombos.IndexOf(fbCombo);
            i = ((i + by) % fbCombos.Count + fbCombos.Count) % fbCombos.Count;
            ShowFreeBodyCombo(fbCombos[i]);
        }

        private void ShowFreeBodyCombo(LoadCombination c)
        {
            fbCombo = c;
            Title3D();
            if (comboList != null)
            {
                settingList = true;
                comboList.SelectedIndex = fbCombos.IndexOf(c);
                if (comboList.SelectedItem != null) comboList.ScrollIntoView(comboList.SelectedItem);
                settingList = false;
            }
            RefreshFreeBodyTable();
            view3D.Show(B);
        }

        private string FreeBodyName => fb == null ? "" : $"{char.ToUpper(fb.Kind[0])}{fb.Kind.Substring(1)} {fb.Id}";

        private void FreeBodyTitle()
        {
            title3D.Text = $"FREE BODY of {FreeBodyName}: " + (fbCombo == null ? "service loads" : fbCombo.Name);
            sub3D.Text = "The whole element, alone, as on the foundation: E the loads applied to it in blue, onto its top (and onto each floor it carries); " +
                         "S what holds it up in red, under it; W its own weight in green, through its middle; I none (it is not cut). Distributed loads are rows of " +
                         "light arrows, point loads single full-colour ones. What one story of a wall puts on the next is inside it and cancels; the numbers are in the side panel." +
                         (fbCombo == null ? "" : $" {fbCombo.Equation}.");
        }

        // ------------------------------------------------------------------ the side panel

        private StackPanel fbTable;

        private void FreeBodySide()
        {
            side.Children.Add(Head($"Free body: {FreeBodyName}"));
            var back = new Button { Content = "◀ Back to the foundation", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
            back.Click += (s, e) => CloseFreeBody();
            side.Children.Add(back);
            side.Children.Add(Para($"{(fb.Kind == "column" ? "" : $"{fb.Length:0.##} ft long, ")}from {fb.Bottom:0.##} to {fb.Top:0.##} ft. Click another wall, beam or column to draw its free body.", true));
            if (fb.Kind == "wall")
            {
                var framed = new CheckBox { Content = "Draw its framing (studs, plates, end posts)", IsChecked = fbFraming, Margin = new Thickness(0, 0, 0, 6),
                                            ToolTip = "Each story's studs at the model's size and spacing; off: a see-through block" };
                framed.Checked += (s, e) => { fbFraming = true; view3D.DrawOnly = false; view3D.Show(B); };
                framed.Unchecked += (s, e) => { fbFraming = false; view3D.DrawOnly = true; view3D.Show(B); };
                side.Children.Add(framed);
            }

            side.Children.Add(Head("Combination"));
            if (fbCombos.Count == 0)
            {
                comboList = null;
                side.Children.Add(Para("No gravity combination has the loads on it: drawn with the service loads.", true));
            }
            else
            {
                comboList = new ListBox { MaxHeight = 220, Margin = new Thickness(0, 0, 0, 6), BorderBrush = Ui.Line };
                ScrollViewer.SetHorizontalScrollBarVisibility(comboList, ScrollBarVisibility.Disabled);
                foreach (var c in fbCombos)
                    comboList.Items.Add(new TextBlock { Text = $"{c.Name}: {fb.Total(FreeBodyPart.Support, c):N0} lb held up", TextWrapping = TextWrapping.Wrap });
                comboList.SelectedIndex = Math.Max(0, fbCombos.IndexOf(fbCombo));
                comboList.SelectionChanged += (s, e) =>
                {
                    if (settingList || comboList.SelectedIndex < 0) return;
                    ShowFreeBodyCombo(fbCombos[comboList.SelectedIndex]);
                };
                side.Children.Add(comboList);
                side.Children.Add(Para($"The gravity combinations ({MethodName}) with the loads on it, the most held up first. The roof's wind is not in these (Wind uplift).", true));
            }

            fbTable = new StackPanel();
            side.Children.Add(fbTable);
            RefreshFreeBodyTable();
            foreach (var n in fb.Notes) side.Children.Add(Para("• " + n, true));
        }

        private void RefreshFreeBodyTable()
        {
            if (fbTable == null || fb == null) return;
            fbTable.Children.Clear();
            var c = fbCombo;
            string Where(FreeBodyForce f) => fb.Kind == "column" ? "" : f.IsLine ? $"{f.A:0.##}–{f.B:0.##}" : $"at {f.A:0.##}";
            void Part(FreeBodyPart part, string letter, string title, Brush ink)
            {
                var list = fb.Of(part).ToList();
                fbTable.Children.Add(new TextBlock { Text = $"{letter}  {title}", FontWeight = FontWeights.SemiBold, Foreground = ink, Margin = new Thickness(0, 6, 0, 2) });
                if (part == FreeBodyPart.Internal)
                {
                    fbTable.Children.Add(Para("None: the whole element is drawn, not cut. Cut it (a story, a span) and the forces at the cut appear here.", true));
                    return;
                }
                if (list.Count == 0) { fbTable.Children.Add(Para("None.", true)); return; }
                var rows = new List<string[]> { new[] { "", "Along (ft)", "Elev.", "Load", "Total (lb)" } };
                rows.AddRange(list.Select(f => new[]
                {
                    f.What, Where(f), $"{f.Z:0.##}", f.IsLine ? $"{f.Value(c):N0} plf" : $"{f.Value(c):N0} lb", $"{f.Resultant(c):N0}",
                }));
                rows.Add(new[] { "Sum", "", "", "", $"{fb.Total(part, c):N0}" });
                fbTable.Children.Add(Mini(rows, new double[] { 110, 0, 0, 0, 0 }));
            }
            Part(FreeBodyPart.Weight, "W", "Weight (down)", new SolidColorBrush(WeightColor));
            Part(FreeBodyPart.Internal, "I", "Internal forces at cuts", Ui.Ink);
            Part(FreeBodyPart.Support, "S", "Support reactions (up)", new SolidColorBrush(ReactionColor));
            Part(FreeBodyPart.External, "E", "External applied loads (down)", new SolidColorBrush(DownColor));
            double w = fb.Total(FreeBodyPart.Weight, c), e = fb.Total(FreeBodyPart.External, c), s = fb.Total(FreeBodyPart.Support, c);
            double off = fb.Imbalance(c);
            fbTable.Children.Add(Para($"ΣFy: W + E − S = {w:N0} + {e:N0} − {s:N0} = {off:N0} lb " + (Math.Abs(off) <= Math.Max(1, 1e-6 * s) ? "(in equilibrium)." : "— not in equilibrium: see the notes."), bold: true));
        }

        // ------------------------------------------------------------------ the free body in 3D

        /// <summary>
        /// The free body in 3D, the element drawn alone (by the view): the loads applied to it in blue, arrows onto its top (or onto
        /// its face at a level it passes); its own weight in green, arrows down at mid-height beside it; what holds it up in red,
        /// arrows up onto its bottom. Line loads are a plate with arrows, as tall as the load; point loads one arrow, as long as it.
        /// </summary>
        private Model3D FreeBody3D()
        {
            if (fb == null) return null;
            var g = new Model3DGroup();
            var c = fbCombo;
            var blue = Mat(Color.FromArgb(0x90, DownColor.R, DownColor.G, DownColor.B));
            var blueArrow = Mat(DownColor);
            var red = Mat(Color.FromArgb(0x90, ReactionColor.R, ReactionColor.G, ReactionColor.B));
            var redArrow = Mat(ReactionColor);
            var green = Mat(Color.FromArgb(0x90, WeightColor.R, WeightColor.G, WeightColor.B));
            var greenArrow = Mat(WeightColor);

            // the element as drawn, its top and bottom (ft) and a side of it in plan
            double top, bottom;
            Point2 nrm = new Point2(1, 0);
            if (fb.Kind == "wall")
            {
                top = fb.Top - Building3DView.SlabThickness; bottom = fb.Bottom;
                if (fb.Length > 0) nrm = new Point2(-(fb.End.Y - fb.Start.Y) / fb.Length, (fb.End.X - fb.Start.X) / fb.Length);
            }
            else if (fb.Kind == "beam")
            {
                top = fb.Top - Building3DView.SlabThickness; bottom = top - Building3DView.BeamDepth;
                if (fb.Length > 0) nrm = new Point2(-(fb.End.Y - fb.Start.Y) / fb.Length, (fb.End.X - fb.Start.X) / fb.Length);
            }
            else
            {
                var col = B.Find(fb.Id) as BuildingModel.Model.Column;
                top = col != null ? Building3DView.ColumnTop(B, col) : fb.Top; bottom = fb.Bottom;
            }
            if (fb.Kind == "wall" && fbFraming) WallFraming3D(g);
            Point2 At(double along, double side) =>
                new Point2(fb.Start.X + (fb.Length > 0 ? (fb.End.X - fb.Start.X) * along / fb.Length : 0) + nrm.X * side,
                           fb.Start.Y + (fb.Length > 0 ? (fb.End.Y - fb.Start.Y) * along / fb.Length : 0) + nrm.Y * side);

            // the scales: the largest line load 3 ft tall, the largest point load 5 ft long (at least a quarter of the element's height)
            double height = Math.Max(4, top - bottom);
            double maxL = Math.Max(1, fb.Forces.Where(f => f.IsLine).Select(f => Math.Abs(f.Value(c))).DefaultIfEmpty(0).Max());
            double maxP = Math.Max(1, fb.Forces.Where(f => !f.IsLine).Select(f => Math.Abs(f.Value(c))).DefaultIfEmpty(0).Max());
            double kL = Math.Max(2.5, 0.3 * height) / maxL, kP = Math.Max(4, 0.45 * height) / maxP;

            foreach (var f in fb.Forces)
            {
                double v = f.Value(c);
                if (Math.Abs(v) < 1e-6) continue;
                switch (f.Part)
                {
                    case FreeBodyPart.External:
                    {
                        // onto the top where it lands at the element's top; at a level it passes, onto the top of the story below (in its plane)
                        bool atTop = fb.Kind == "beam" || Math.Abs(f.Z - fb.Top) < 1e-6;
                        double tip = atTop ? top : f.Z - Building3DView.SlabThickness;
                        double side = 0;
                        Draw(g, f, tip, true, v, side, blue, blueArrow);
                        break;
                    }
                    case FreeBodyPart.Weight:
                    {
                        // through its middle, in its own plane: seen from either side through the see-through or framed wall
                        double mid = (top + bottom) / 2;
                        double len = f.IsLine ? v * kL : v * kP;
                        double side = 0;
                        if (f.IsLine)
                        {
                            LineArrows(g, At(f.A, side), At(f.B, side), mid + len / 2, mid - len / 2, greenArrow);
                        }
                        else Arrow(g, At(f.A, side), mid + len / 2, mid - len / 2, greenArrow, 0.12, 0.4);
                        break;
                    }
                    case FreeBodyPart.Support:
                        Draw(g, f, bottom, false, v, 0, red, redArrow);
                        break;
                }
            }
            return g;

            void Draw(Model3DGroup grp, FreeBodyForce f, double tip, bool down, double v, double side, Material plate, Material arrow)
            {
                if (f.IsLine)
                {
                    double h = v * kL;
                    var a = At(f.A, side); var b = At(f.B, side);
                    if (down) LineArrows(grp, a, b, tip + h, tip, arrow);
                    else LineArrows(grp, a, b, tip - h, tip, arrow);
                }
                else
                {
                    double len = Math.Max(1.0, v * kP);
                    var p = At(f.A, side);
                    if (down) Arrow(grp, p, tip + len, tip, arrow, 0.14, 0.45);
                    else Arrow(grp, p, tip - len, tip, arrow, 0.14, 0.45);
                }
            }
        }

        /// <summary>The wall drawn as framed (studs, plates, posts) rather than a see-through block.</summary>
        private bool fbFraming = true;

        /// <summary>
        /// The wall's framing, story by story, from the shared layout (WoodFraming): each story's studs at the model's size and
        /// spacing (the wall's own, else the project's), on the floor at its base up to the underside of the floor above.
        /// </summary>
        private void WallFraming3D(Model3DGroup g)
        {
            var w = B.Find(fb.Id) as BuildingModel.Model.Wall;
            if (w == null) return;
            double baseZ = fb.Bottom;
            foreach (var lv in BuildingModel.Model.BuildingQueries.LevelsAlong(B, w))
            {
                double h = lv.Elevation - baseZ - Building3DView.SlabThickness;
                if (h > 1)
                {
                    var f = WoodFraming.WallFraming.Build(fb.Length, h, B.StudSizeOf(w, lv.Id), B.StudSpacingOf(w, lv.Id), 0);
                    var m = WoodFraming.Wpf.FramingModel.Wall(f);
                    m.Transform = WoodFraming.Wpf.FramingModel.OnPlan(w.Start.X, w.Start.Y, w.End.X, w.End.Y, baseZ, f.StudDepth);
                    g.Children.Add(m);
                }
                baseZ = lv.Elevation;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Sketch;

namespace LoadManager.App
{
    public sealed partial class MainWindow
    {
        private static readonly Color DownColor = Wpf.EnvelopeDiagram.Down;
        private static readonly Color UpColor = Wpf.EnvelopeDiagram.Up;

        // how big the loads are drawn: the largest line load (plf) and point load (lb), and the diagram's depth for the largest (ft)
        private double maxLine = 1, maxPoint = 1, lineDepthFt = 3;

        private void FoundationScales()
        {
            maxLine = Math.Max(1, foundation.Walls.SelectMany(w => w.Segments).Select(s => Math.Max(s.Envelope.Max, -s.Envelope.Min)).DefaultIfEmpty(0).Max());
            maxPoint = Math.Max(1, foundation.AllPoints.Select(p => Math.Max(p.Envelope.Max, -p.Envelope.Min)).DefaultIfEmpty(0).Max());
            var fp = B.Footprint();
            double small = fp.IsEmpty ? 30 : Math.Min(fp.Bounds.Width, fp.Bounds.Height);
            lineDepthFt = Math.Max(1.5, Math.Min(6, 0.12 * small));
        }

        private string MethodName => method == DesignMethod.Asd ? "ASD" : "strength (LRFD)";

        private string DiagramTitle()
        {
            var w = foundation?.Walls.FirstOrDefault(x => x.Id == selectedWall);
            if (w == null) return null;
            var e = w.Line ?? new LoadEnvelope();
            return $"Wall {w.Id}, {w.Length:0.##} ft: envelope of the {MethodName} combinations along its footing. Most {e.Max:N0} plf ({e.MaxBy?.Name}); least {e.Min:N0} plf ({e.MinBy?.Name})" +
                   (w.Points.Count > 0 ? $"; {w.Points.Count} point load{(w.Points.Count == 1 ? "" : "s")} on it." : ".");
        }

        // ------------------------------------------------------------------ the side panel

        private void FoundationSide()
        {
            side.Children.Add(Head("Combinations"));
            var asd = new RadioButton { Content = "Allowable stress (ASD): ASCE 7-22 2.4 and IBC 1605.2", IsChecked = method == DesignMethod.Asd, Margin = new Thickness(0, 0, 0, 2) };
            var lrfd = new RadioButton { Content = "Strength (LRFD): ASCE 7-22 2.3", IsChecked = method == DesignMethod.Strength, Margin = new Thickness(0, 0, 0, 4) };
            asd.Checked += (s, e) => { method = DesignMethod.Asd; Changed(); };
            lrfd.Checked += (s, e) => { method = DesignMethod.Strength; Changed(); };
            side.Children.Add(asd);
            side.Children.Add(lrfd);
            side.Children.Add(Para($"{foundation.Combinations.Count} combinations with the loads found here: " + string.Join(", ", foundation.Combinations.Take(6).Select(c => c.Name)) + (foundation.Combinations.Count > 6 ? "…" : ""), true));
            var wrap = new WrapPanel();
            wrap.Children.Add(Link("All the numbers…", "Every combination with its load factors, each wall and point", () => ShowCombined(selectedWall)));
            wrap.Children.Add(Link("Wind uplift…", "The roof wind against the dead load", ShowUplift));
            side.Children.Add(wrap);

            // what came in
            side.Children.Add(Head("What comes down"));
            side.Children.Add(Para(uplift?.HasWind == true ? $"Roof wind: {uplift.Runs.Count} whole-building cases from the wind setup, down the load path." : "Roof wind: none. " + (windWhy ?? ""), true));
            if (lateral == null || lateral.IsEmpty)
                side.Children.Add(Para("Shear wall ends (hold-downs and their compression): none yet. The shear wall app writes them into the project when it saves.", true));
            else
            {
                side.Children.Add(Para($"Shear wall ends: {lateral.Points.Count} from {lateral.Source} ({lateral.Cases.Count} cases), saved {lateral.Saved} on revision {lateral.ModelRevision}." + (string.IsNullOrEmpty(lateral.Note) ? "" : " " + lateral.Note), true));
                string stale = lateral.Stale(B);
                if (stale != null) side.Children.Add(new TextBlock { Text = "They may be out of date: " + stale + " Save the shear wall design again.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Warning, Margin = new Thickness(0, 0, 0, 4) });
                else if (lateral.ModelRevision > 0 && doc.Revision > lateral.ModelRevision + 1)
                    side.Children.Add(Para($"The model has been saved since (revision {doc.Revision}): if the walls or the wind changed, save the shear wall design again.", true));
            }

            // the envelope over all
            var lines = foundation.Walls.Where(w => w.Segments.Count > 0).ToList();
            var points = foundation.AllPoints.ToList();
            var topLine = lines.OrderByDescending(w => w.Line.Max).FirstOrDefault();
            var lowLine = lines.OrderBy(w => w.Line.Min).FirstOrDefault();
            var topPoint = points.OrderByDescending(p => p.Envelope.Max).FirstOrDefault();
            var lowPoint = points.OrderBy(p => p.Envelope.Min).FirstOrDefault();
            side.Children.Add(Head("Envelope"));
            var rows = new List<string[]> { new[] { "", "Most down", "Most up" } };
            rows.Add(new[] { "Wall footing (plf)", topLine == null ? "" : $"{topLine.Line.Max:N0} ({topLine.Id})", lowLine == null || !lowLine.Line.Lifts ? "none" : $"{-lowLine.Line.Min:N0} ({lowLine.Id})" });
            rows.Add(new[] { "Point (lb)", topPoint == null ? "" : $"{topPoint.Envelope.Max:N0} ({topPoint.From})", lowPoint == null || !lowPoint.Envelope.Lifts ? "none" : $"{-lowPoint.Envelope.Min:N0} ({lowPoint.From})" });
            side.Children.Add(Mini(rows, new double[] { 110, 0, 0 }));
            side.Children.Add(Para($"{lines.Count} wall footings, {points.Count} point loads ({points.Count(p => p.Kind == FoundationPointKind.Column)} columns, {points.Count(p => p.HasLateral)} at shear wall ends). " +
                                   $"{lines.Count(w => w.Line.Lifts)} footings and {points.Count(p => p.Envelope.Lifts)} points are lifted in some combination.", true));

            // the selection
            if (selectedPoint != null) PointPanel(selectedPoint);
            var wall = foundation.Walls.FirstOrDefault(w => w.Id == selectedWall);
            if (wall != null) WallFoundationPanel(wall);
            if (selectedPoint == null && wall == null) side.Children.Add(Para("Click a wall's footing or a point on the plan: its loads, each combination, and the wall's envelope diagram below the plan.", true));

            side.Children.Add(Head("On the plan"));
            side.Children.Add(Para($"Beside each wall: the most combined line load down (blue) and, opposite, the most up where it lifts (vermilion), to scale: {lineDepthFt:0.#} ft for {maxLine:N0} plf. " +
                                   "Circles: point loads (posts, columns), sized by the load; squares: shear wall ends. Blue: down in every combination; vermilion: lifted in some.", true));
            foreach (var s in foundation.Skipped) side.Children.Add(Para("• " + s, true));
            foreach (var n in foundation.Notes) side.Children.Add(Para("• " + n, true));
        }

        private void PointPanel(FoundationPoint p)
        {
            side.Children.Add(Head(p.From));
            string where = p.WallId != null ? $"on wall {p.WallId}'s footing, {p.At:0.##} ft from its start" : $"at ({p.Where.X:0.##}, {p.Where.Y:0.##}) ft, on its own";
            side.Children.Add(Para($"{(p.Kind == FoundationPointKind.Column ? "Column" : p.Kind == FoundationPointKind.Post ? "Post" : "Shear wall end")} {where}.", true));
            var l = p.Load;
            side.Children.Add(Mini(new List<string[]>
            {
                new[] { "Service (lb)", "D", "L", "Lr", "S" },
                new[] { "", $"{l.Dead:N0}", $"{l.Live:N0}", $"{l.RoofLive:N0}", $"{l.Snow:N0}" },
            }, new double[] { 90, 0, 0, 0, 0 }));
            if (l.Has(LoadType.Wind))
                side.Children.Add(Para($"W from {l.WindMin:N0} to {l.WindMax:N0} lb (+ down): the roof's {p.RoofWindMin:N0} to {p.RoofWindMax:N0}" +
                                       (p.Lateral.Count > 0 ? "; the shear wall's " + string.Join(", ", p.Lateral.Select(kv => $"{kv.Key} {kv.Value:N0}")) : "") + ".", true));
            var e = p.Envelope;
            side.Children.Add(Para($"Most down {e.Max:N0} lb ({e.MaxBy?.Name}); " + (e.Lifts ? $"most up {-e.Min:N0} lb ({e.MinBy?.Name})." : $"least {e.Min:N0} lb ({e.MinBy?.Name}), never lifted."), bold: true));
            side.Children.Add(Combos(p.Combined, "lb"));
        }

        private void WallFoundationPanel(FoundationWall w)
        {
            side.Children.Add(Head($"Wall {w.Id} footing"));
            var e = w.Line;
            side.Children.Add(Para($"{w.Length:0.##} ft. Line load: most {e.Max:N0} plf ({e.MaxBy?.Name}); " + (e.Lifts ? $"most up {-e.Min:N0} plf ({e.MinBy?.Name})." : $"least {e.Min:N0} plf ({e.MinBy?.Name})."), bold: true));
            var t = w.Total();
            side.Children.Add(Para($"In all (service): D {t.Dead:N0}, L {t.Live:N0}, Lr {t.RoofLive:N0}, S {t.Snow:N0} lb" + (t.WindMin != 0 || t.WindMax != 0 ? $", W {t.WindMin:N0} to {t.WindMax:N0} lb" : "") + ", line loads and points.", true));
            if (w.Segments.Count > 0)
            {
                var rows = new List<string[]> { new[] { "Along (ft)", "D", "L", "Lr", "S", "Most", "Least" } };
                rows.AddRange(w.Segments.Select(s => new[] { $"{s.A:0.#}–{s.B:0.#}", $"{s.Load.Dead:N0}", $"{s.Load.Live:N0}", $"{s.Load.RoofLive:N0}", $"{s.Load.Snow:N0}", $"{s.Envelope.Max:N0}", $"{s.Envelope.Min:N0}" }));
                side.Children.Add(Mini(rows, new double[] { 62, 0, 0, 0, 0, 0, 0 }));
            }
            foreach (var p in w.Points)
            {
                var b = new Button
                {
                    Content = $"{p.From} at {p.At:0.##} ft: {p.Envelope.Max:N0}" + (p.Envelope.Lifts ? $" / {p.Envelope.Min:N0}" : "") + " lb",
                    HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 1, 0, 1),
                    Background = ReferenceEquals(p, selectedPoint) ? Ui.Checked : Brushes.White, BorderBrush = Ui.Line,
                };
                var pp = p;
                b.Click += (o, ev) => Select(w.Id, -1, pp);
                side.Children.Add(b);
            }
            if (w.Segments.Count > 0)
            {
                var list = foundation.Combinations.Select((c, i) => new CombinedLoad { Combination = c, Max = w.Segments.Max(s => s.Combined[i].Max), Min = w.Segments.Min(s => s.Combined[i].Min) }).ToList();
                side.Children.Add(Para("Each combination, the most and least along it:", true));
                side.Children.Add(Combos(list, "plf"));
            }
        }

        /// <summary>Each combination's most and least, the governing ones in bold.</summary>
        private UIElement Combos(IList<CombinedLoad> list, string unit)
        {
            var env = LoadEnvelope.Of(list);
            var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            void Cell(string text, int r, int c, bool bold = false, Brush b = null)
            {
                var t = new TextBlock { Text = text, Margin = new Thickness(0, 1, 6, 1), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = b ?? Ui.Ink, TextWrapping = TextWrapping.Wrap };
                if (c > 0) t.TextAlignment = TextAlignment.Right;
                Grid.SetRow(t, r); Grid.SetColumn(t, c);
                g.Children.Add(t);
            }
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell("Combination", 0, 0, false, Ui.Muted); Cell($"Most ({unit})", 0, 1, false, Ui.Muted); Cell($"Least", 0, 2, false, Ui.Muted);
            var up = new SolidColorBrush(UpColor);
            for (int i = 0; i < list.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var c = list[i];
                Cell(c.Combination.Name, i + 1, 0);
                Cell($"{c.Max:N0}", i + 1, 1, c.Combination == env.MaxBy);
                Cell($"{c.Min:N0}", i + 1, 2, c.Combination == env.MinBy, c.Min < 0 ? up : null);
            }
            return new Expander { Header = $"Every combination ({list.Count})", IsExpanded = false, Content = g };
        }

        // ------------------------------------------------------------------ the foundation plan

        private void DrawFoundation(DrawingContext ctx)
        {
            if (foundation == null) return;
            var downFill = new SolidColorBrush(Color.FromArgb(0x48, DownColor.R, DownColor.G, DownColor.B));
            var upFill = new SolidColorBrush(Color.FromArgb(0x55, UpColor.R, UpColor.G, UpColor.B));
            var downPen = new Pen(new SolidColorBrush(DownColor), 1.2);
            var upPen = new Pen(new SolidColorBrush(UpColor), 1.2);
            var downInk = new SolidColorBrush(DownColor);
            var upInk = new SolidColorBrush(UpColor);
            var sel = new Pen(new SolidColorBrush(BuildingModel.Sketch.SketchCanvas.Palette.Select), 3);
            double k = lineDepthFt / maxLine;   // ft of diagram per plf

            foreach (var w in foundation.Walls)
            {
                if (w.Length <= 0) continue;
                double ux = (w.End.X - w.Start.X) / w.Length, uy = (w.End.Y - w.Start.Y) / w.Length;
                // the down diagram on the side toward the footprint's middle (room there), the uplift on the outside
                var fp = B.Footprint();
                var mid = w.PointAt(w.Length / 2);
                double nx = -uy, ny = ux;
                if (!fp.IsEmpty)
                {
                    var c = new Point2((fp.Bounds.X0 + fp.Bounds.X1) / 2, (fp.Bounds.Y0 + fp.Bounds.Y1) / 2);
                    if ((c.X - mid.X) * nx + (c.Y - mid.Y) * ny < 0) { nx = -nx; ny = -ny; }
                }
                Point P(double along, double off) => sketch.ToScreen(new Point2(w.Start.X + ux * along + nx * off, w.Start.Y + uy * along + ny * off));
                foreach (var s in w.Segments)
                {
                    var e = s.Envelope;
                    if (e.Max > 0) Poly(ctx, downFill, downPen, P(s.A, 0), P(s.B, 0), P(s.B, e.Max * k), P(s.A, e.Max * k));
                    if (e.Min < 0) Poly(ctx, upFill, upPen, P(s.A, 0), P(s.B, 0), P(s.B, e.Min * k), P(s.A, e.Min * k));
                }
                var top = w.Segments.OrderByDescending(s => s.Envelope.Max).FirstOrDefault();
                if (top != null)
                {
                    var at = P((top.A + top.B) / 2, top.Envelope.Max * k + 0.3);
                    sketch.Label(ctx, $"{top.Envelope.Max:N0} plf", new Point(at.X - 18, at.Y - 7), downInk, 9.5);
                }
                var low = w.Segments.OrderBy(s => s.Envelope.Min).FirstOrDefault();
                if (low != null && low.Envelope.Lifts)
                {
                    var at = P((low.A + low.B) / 2, low.Envelope.Min * k - 0.3);
                    sketch.Label(ctx, $"{low.Envelope.Min:N0} plf", new Point(at.X - 18, at.Y - 7), upInk, 9.5, bold: true);
                }
                if (w.Id == selectedWall) ctx.DrawLine(sel, sketch.ToScreen(w.Start), sketch.ToScreen(w.End));
            }

            foreach (var p in foundation.AllPoints)
            {
                var e = p.Envelope;
                if (Math.Abs(e.Max) < 0.5 && !e.Lifts) continue;
                double r = 4 + 9 * Math.Sqrt(Math.Max(e.Max, -e.Min) / maxPoint);
                var c = sketch.ToScreen(p.Where);
                var fill = e.Lifts ? upFill : downFill;
                var pen = new Pen(e.Lifts ? upInk : downInk, ReferenceEquals(p, selectedPoint) ? 3 : 1.4);
                if (p.HasLateral) ctx.DrawRectangle(fill, pen, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
                else ctx.DrawEllipse(fill, pen, c, r, r);
                sketch.Label(ctx, $"{p.From}: {e.Max:N0}" + (e.Lifts ? $" / {e.Min:N0}" : "") + " lb", new Point(c.X + r + 3, c.Y - r - 4), e.Lifts ? upInk : downInk, 9.5);
            }
        }

        private static void Poly(DrawingContext ctx, Brush fill, Pen pen, params Point[] pts)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(pts[0], true, true);
                for (int i = 1; i < pts.Length; i++) s.LineTo(pts[i], true, false);
            }
            g.Freeze();
            ctx.DrawGeometry(fill, pen, g);
        }

        // ------------------------------------------------------------------ the foundation in 3D

        /// <summary>
        /// The foundation's loads in 3D, under the see-through building: along each wall's footing a load diagram standing on it, its
        /// height the most combined line load down (blue), and hanging under it the most up where it lifts (vermilion); at each point
        /// an arrow down onto the foundation for its most down, and one pulling up beside it where it lifts.
        /// </summary>
        private Model3D Foundation3D()
        {
            if (foundation == null) return null;
            var g = new Model3DGroup();
            var down = Mat(Color.FromArgb(0xC0, DownColor.R, DownColor.G, DownColor.B));
            var up = Mat(Color.FromArgb(0xD0, UpColor.R, UpColor.G, UpColor.B));
            var footing = Mat(Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF));
            double k = 1.6 * lineDepthFt / maxLine;
            double arrow = 2.2 * Math.Max(4, lineDepthFt * 2);   // the largest point load's arrow (ft)
            foreach (var w in foundation.Walls)
            {
                if (w.Length <= 0) continue;
                // the footing itself, a strip under the wall
                g.Children.Add(Strip(w.Start, w.End, 1.2, -0.9, -0.2, footing));
                foreach (var s in w.Segments)
                {
                    var a = w.PointAt(s.A); var b = w.PointAt(s.B);
                    if (s.Envelope.Max > 0) g.Children.Add(Strip(a, b, 0.12, 0, s.Envelope.Max * k, down));
                    if (s.Envelope.Min < 0) g.Children.Add(Strip(a, b, 0.12, s.Envelope.Min * k - 0.2, -0.2, up));
                }
            }
            foreach (var p in foundation.AllPoints)
            {
                var e = p.Envelope;
                if (e.Max > 0) Arrow(g, p.Where, e.Max / maxPoint * arrow, true, down);
                if (e.Lifts) Arrow(g, new Point2(p.Where.X + 0.7, p.Where.Y + 0.7), -e.Min / maxPoint * arrow, false, up);
            }
            return g;
        }

        private static Material Mat(Color c)
        {
            var m = new DiffuseMaterial(new SolidColorBrush(c));
            m.Freeze();
            return m;
        }

        private static Point3D P3(double x, double y, double z) => new Point3D(x, z, -y);

        /// <summary>A thin upright plate along a - b (plan), <paramref name="w"/> thick, from z0 to z1.</summary>
        private static GeometryModel3D Strip(Point2 a, Point2 b, double w, double z0, double z1, Material m)
        {
            double len = Dist(a, b);
            double nx = len <= 0 ? w / 2 : -(b.Y - a.Y) / len * w / 2, ny = len <= 0 ? 0 : (b.X - a.X) / len * w / 2;
            return Hexa(new[]
            {
                P3(a.X - nx, a.Y - ny, z0), P3(b.X - nx, b.Y - ny, z0), P3(b.X + nx, b.Y + ny, z0), P3(a.X + nx, a.Y + ny, z0),
                P3(a.X - nx, a.Y - ny, z1), P3(b.X - nx, b.Y - ny, z1), P3(b.X + nx, b.Y + ny, z1), P3(a.X + nx, a.Y + ny, z1),
            }, m);
        }

        /// <summary>An arrow at a point: down onto the ground from <paramref name="length"/> above it, or up from the ground that far.</summary>
        private static void Arrow(Model3DGroup g, Point2 at, double length, bool downward, Material m)
        {
            length = Math.Max(1.2, length);
            double tip = Math.Min(1.6, length * 0.4), h = 0.2, hw = 0.65;
            double zTip = downward ? 0 : length, zBase = downward ? tip : length - tip;
            double z0 = downward ? tip : 0, z1 = downward ? length : length - tip;
            g.Children.Add(Hexa(new[]
            {
                P3(at.X - h, at.Y - h, z0), P3(at.X + h, at.Y - h, z0), P3(at.X + h, at.Y + h, z0), P3(at.X - h, at.Y + h, z0),
                P3(at.X - h, at.Y - h, z1), P3(at.X + h, at.Y - h, z1), P3(at.X + h, at.Y + h, z1), P3(at.X - h, at.Y + h, z1),
            }, m));
            var mesh = new MeshGeometry3D();
            var apex = P3(at.X, at.Y, zTip);
            var c = new[] { P3(at.X - hw, at.Y - hw, zBase), P3(at.X + hw, at.Y - hw, zBase), P3(at.X + hw, at.Y + hw, zBase), P3(at.X - hw, at.Y + hw, zBase) };
            for (int i = 0; i < 4; i++)
            {
                int n = mesh.Positions.Count;
                mesh.Positions.Add(apex); mesh.Positions.Add(c[i]); mesh.Positions.Add(c[(i + 1) % 4]);
                mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 1); mesh.TriangleIndices.Add(n + 2);
            }
            int b0 = mesh.Positions.Count;
            foreach (var p in c) mesh.Positions.Add(p);
            mesh.TriangleIndices.Add(b0); mesh.TriangleIndices.Add(b0 + 1); mesh.TriangleIndices.Add(b0 + 2);
            mesh.TriangleIndices.Add(b0); mesh.TriangleIndices.Add(b0 + 2); mesh.TriangleIndices.Add(b0 + 3);
            mesh.Freeze();
            g.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m });
        }

        private static GeometryModel3D Hexa(Point3D[] c, Material m)
        {
            var mesh = new MeshGeometry3D();
            int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            foreach (var f in faces)
            {
                int i = mesh.Positions.Count;
                foreach (var k in f) mesh.Positions.Add(c[k]);
                mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
                mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
            }
            mesh.Freeze();
            return new GeometryModel3D(mesh, m) { BackMaterial = m };
        }
    }
}

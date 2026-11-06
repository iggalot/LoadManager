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
            if (mode == Mode.Foundation3D && fb != null) { FreeBodySide(); return; }
            if (mode == Mode.Foundation3D) Foundation3DSide(); else comboList = null;
            side.Children.Add(Head("Combinations"));
            var asd = new RadioButton { Content = "Allowable stress (ASD): ASCE 7-22 2.4 and IBC 1605.2", IsChecked = method == DesignMethod.Asd, Margin = new Thickness(0, 0, 0, 2) };
            var lrfd = new RadioButton { Content = "Strength (LRFD): ASCE 7-22 2.3", IsChecked = method == DesignMethod.Strength, Margin = new Thickness(0, 0, 0, 4) };
            asd.Checked += (s, e) => { method = DesignMethod.Asd; Changed(); };
            lrfd.Checked += (s, e) => { method = DesignMethod.Strength; Changed(); };
            side.Children.Add(asd);
            side.Children.Add(lrfd);
            side.Children.Add(Para($"{foundation.Combinations.Count} code combinations with the loads found here" + (foundation.WindCases.Count > 0 ? $"; each with W taken once in each of the {foundation.WindCases.Count} wind cases: {foundation.Cases.Count} in all" : "") + ". " + string.Join(", ", foundation.Combinations.Take(6).Select(c => c.Name)) + (foundation.Combinations.Count > 6 ? "…" : ""), true));
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

            // the selection (several picked: say so; one at a time shows its loads and diagram)
            int picked = sketch.Selection.Count;
            if (picked > 1 && selectedPoint == null && selectedWalls.Count != 1)
                side.Children.Add(Para($"{picked} selected: click one wall or point alone to see its loads and diagram.", true));
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
                var list = foundation.Cases.Select((c, i) => new CombinedLoad { Case = c, Max = w.Segments.Max(s => s.Combined[i].Max), Min = w.Segments.Min(s => s.Combined[i].Min) }).ToList();
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
                Cell(c.Case.Name, i + 1, 0);
                Cell($"{c.Max:N0}", i + 1, 1, c.Case == env.MaxBy);
                Cell($"{c.Min:N0}", i + 1, 2, c.Case == env.MinBy, c.Min < 0 ? up : null);
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

        // ------------------------------------------------------------------ the foundation in 3D: which combination

        /// <summary>The combination drawn in 3D; null: the envelope of them all.</summary>
        private FoundationCase combo3D;
        private ListBox comboList;
        private bool settingList;
        private readonly TextBlock title3D = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Ink };
        private readonly TextBlock sub3D = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(0, 2, 0, 4) };
        private Border title3DBox;

        /// <summary>The label over the 3D view saying what is drawn, with the way back to the envelope and to the next and previous combination.</summary>
        private UIElement Title3DBox()
        {
            var buttons = new WrapPanel();
            Button B3(string text, string tip, Action a)
            {
                var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
                b.Click += (s, e) => a();
                return b;
            }
            buttons.Children.Add(B3("Envelope", "Back to the envelope of every combination", () => { CloseFreeBody(); Show3DCombo(null); }));
            buttons.Children.Add(B3("◀ Previous", "The combination before this one", () => { if (fb != null) StepFreeBody(-1); else Step3D(-1); }));
            buttons.Children.Add(B3("Next ▶", "The next combination", () => { if (fb != null) StepFreeBody(1); else Step3D(1); }));
            var stack = new StackPanel();
            stack.Children.Add(title3D);
            stack.Children.Add(sub3D);
            stack.Children.Add(buttons);
            title3DBox = new Border
            {
                Child = stack, MaxWidth = 560, Background = new SolidColorBrush(Color.FromArgb(0xEC, 0xFF, 0xFF, 0xFF)), BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 8), Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
            };
            return title3DBox;
        }

        private void Step3D(int by)
        {
            var all = foundation?.Cases;
            if (all == null || all.Count == 0) return;
            int i = combo3D == null ? (by > 0 ? -1 : all.Count) : all.IndexOf(combo3D);
            i += by;
            Show3DCombo(i < 0 || i >= all.Count ? null : all[i]);
        }

        private void Show3DCombo(FoundationCase c, bool keepJump = false)
        {
            combo3D = c;
            if (!keepJump) { jumpFrom = null; jumpNote = null; }
            Title3D();
            if (comboList != null)
            {
                settingList = true;
                comboList.SelectedIndex = c == null ? 0 : foundation.Cases.IndexOf(c) + 1;
                comboList.ScrollIntoView(comboList.SelectedItem);
                settingList = false;
            }
            if (view3D.Visibility == Visibility.Visible) view3D.Show(B);
        }

        private void Title3D()
        {
            if (fb != null) { FreeBodyTitle(); return; }
            int n = foundation?.Cases.Count ?? 0;
            if (combo3D == null)
            {
                title3D.Text = $"ENVELOPE of all {n} {MethodName} combinations";
                sub3D.Text = "Each piece of footing and each point shows its own most down and most up over every combination: the combination that governs one place " +
                             "is not, in general, the one that governs another, and the most down and the most up at one place come from different ones. " +
                             "Click a load (an arrow or a row of them) to go to the case that gives it there; or pick one in the list, or step through them. Click a wall, beam or column for its free body.";
            }
            else
            {
                int i = foundation.Cases.IndexOf(combo3D);
                title3D.Text = $"Combination {i + 1} of {n}: {combo3D.Combination.Name}";
                sub3D.Text = (combo3D.Wind != null ? $"Wind case: {combo3D.Wind.Name}, the same case everywhere. " : "") +
                             $"{combo3D.Equation}. This one combination alone, everywhere." +
                             (combo3D.Combination.Types.Any(t => t == LoadType.Live || t == LoadType.RoofLive || t == LoadType.Snow)
                                 ? " Its gravity variable loads are each taken in or left out, whichever is worse, so a place can show both a most down and a most up." : "") +
                             (jumpNote == null ? "" : "\n" + jumpNote + " (gold on the footing)");
            }
        }

        /// <summary>The side panel's list of every combination for the 3D view, the envelope first; the arrow keys step through it.</summary>
        private void Foundation3DSide()
        {
            side.Children.Add(Head("Drawn in 3D"));
            var all = foundation.Cases;
            comboList = new ListBox { MaxHeight = 420, Margin = new Thickness(0, 0, 0, 6), BorderBrush = Ui.Line };
            ScrollViewer.SetHorizontalScrollBarVisibility(comboList, ScrollBarVisibility.Disabled);
            TextBlock Item(string head, string body, bool bold)
            {
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
                t.Inlines.Add(new System.Windows.Documents.Run(head) { FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal });
                if (!string.IsNullOrEmpty(body)) t.Inlines.Add(new System.Windows.Documents.Run("\n" + body) { Foreground = Ui.Muted, FontSize = 11 });
                return t;
            }
            var segs = foundation.Walls.SelectMany(w => w.Segments).ToList();
            var pts = foundation.AllPoints.ToList();
            string Range(int i)
            {
                double lMax = segs.Select(s => i < s.Combined.Count ? s.Combined[i].Max : 0).DefaultIfEmpty(0).Max();
                double lMin = segs.Select(s => i < s.Combined.Count ? s.Combined[i].Min : 0).DefaultIfEmpty(0).Min();
                double pMax = pts.Select(p => i < p.Combined.Count ? p.Combined[i].Max : 0).DefaultIfEmpty(0).Max();
                double pMin = pts.Select(p => i < p.Combined.Count ? p.Combined[i].Min : 0).DefaultIfEmpty(0).Min();
                return $"footings: most down {lMax:N0} plf" + (lMin < -1e-9 ? $", most up {-lMin:N0} plf" : "") +
                       $"; points: most down {pMax:N0} lb" + (pMin < -1e-9 ? $", most up {-pMin:N0} lb" : "");
            }
            comboList.Items.Add(Item($"Envelope of all {all.Count}", "the most down and most up at each place, whichever combination and wind case gives it", true));
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                var t = Item($"{i + 1}. {c.Combination.Name}", (c.Wind != null ? $"W: {c.Wind.Name}\n" : "") + $"{c.Equation}; {Range(i)}", false);
                comboList.Items.Add(new ListBoxItem { Content = t, Tag = c });
            }
            comboList.SelectedIndex = combo3D == null ? 0 : all.IndexOf(combo3D) + 1;
            comboList.SelectionChanged += (s, e) =>
            {
                if (settingList || comboList.SelectedIndex < 0) return;
                int k = comboList.SelectedIndex - 1;
                Show3DCombo(k < 0 || k >= all.Count ? null : all[k]);
            };
            // a filter: the list runs to hundreds with the wind cases ("0.6D", "torsion", "flexible", "from the west")
            var filter = new TextBox { Margin = new Thickness(0, 0, 0, 4), ToolTip = "Show only the combinations whose name or wind case has these words" };
            var hint = new TextBlock { Text = "Filter: e.g. 0.6D + 0.6W, Case 4, flexible, west", Foreground = Ui.Muted, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) };
            filter.TextChanged += (s, e) =>
            {
                var words = filter.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
                foreach (var item in comboList.Items.OfType<ListBoxItem>())
                {
                    if (!(item.Tag is FoundationCase c)) continue;
                    string text = c.Name + " " + c.Equation;
                    bool show = words.All(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                    item.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            side.Children.Add(hint);
            side.Children.Add(filter);
            side.Children.Add(comboList);
            side.Children.Add(Para("Click one to draw it alone; the arrow keys move up and down the list. Envelope, above the view, comes back to them all. " +
                                   "Every combination is drawn to the envelope's scale, so they compare by eye.", true));
        }

        // ------------------------------------------------------------------ the foundation in 3D

        /// <summary>The top and the bottom of the footing as drawn in 3D (ft, the ground at 0).</summary>
        private const double FootingTop = -0.2, FootingBottom = -0.9;

        /// <summary>The reactions' colour in 3D: red, under the footing (the loads from above are blue, on top of it).</summary>
        internal static readonly Color ReactionColor = Color.FromRgb(0xC6, 0x28, 0x28);

        /// <summary>
        /// The foundation's loads in 3D, under the see-through building. The loads from above are blue and act on the top of the
        /// footing, their arrows pointing the way the load acts: along each footing a plate as tall as the most combined line load
        /// down with arrows down onto it, and where a footing lifts a second plate (beside it, outward) with arrows up; at each point
        /// an arrow down onto it for its most down, and one pulling up beside it where it lifts. The reactions are red and act on the
        /// bottom of the footing, equal and opposite: the ground pushing up under a load down, the anchorage pulling down under an uplift.
        /// </summary>
        private Model3D Foundation3D()
        {
            if (foundation == null) return null;
            picks3D.Clear();
            var g = new Model3DGroup();
            var load = Mat(Color.FromArgb(0x90, DownColor.R, DownColor.G, DownColor.B));
            var loadArrow = Mat(Color.FromArgb(0xFF, DownColor.R, DownColor.G, DownColor.B));
            var react = Mat(Color.FromArgb(0x90, ReactionColor.R, ReactionColor.G, ReactionColor.B));
            var reactArrow = Mat(Color.FromArgb(0xFF, ReactionColor.R, ReactionColor.G, ReactionColor.B));
            var footing = Mat(Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF));
            double k = 1.6 * lineDepthFt / maxLine;
            // the envelope, or one combination (its own most down and most up: variable loads in or out, the wind either way)
            int ci = combo3D == null ? -1 : foundation.Cases.IndexOf(combo3D);
            (double Max, double Min) Of(LoadEnvelope e, List<CombinedLoad> c) => ci >= 0 && ci < c.Count ? (c[ci].Max, c[ci].Min) : (e.Max, e.Min);
            double arrow = 2.2 * Math.Max(4, lineDepthFt * 2);   // the largest point load's arrow (ft)
            // the middle of the footprint: an uplift plate stands beside the footing on the side away from it
            var mids = foundation.Walls.Where(w => w.Length > 0).Select(w => w.PointAt(w.Length / 2)).ToList();
            var centre = mids.Count == 0 ? new Point2(0, 0) : new Point2(mids.Average(m => m.X), mids.Average(m => m.Y));
            foreach (var w in foundation.Walls)
            {
                if (w.Length <= 0) continue;
                // the footing itself, a strip under the wall
                g.Children.Add(Strip(w.Start, w.End, 1.2, FootingBottom, FootingTop, footing));
                double ux = -(w.End.Y - w.Start.Y) / w.Length, uy = (w.End.X - w.Start.X) / w.Length;   // a unit normal in plan
                var mid = w.PointAt(w.Length / 2);
                if ((mid.X - centre.X) * ux + (mid.Y - centre.Y) * uy < 0) { ux = -ux; uy = -uy; }   // outward
                foreach (var s in w.Segments)
                {
                    var a = w.PointAt(s.A); var b = w.PointAt(s.B);
                    var v = Of(s.Envelope, s.Combined);
                    string where = $"wall {w.Id}'s footing, {s.A:0.#} to {s.B:0.#} ft";
                    if (v.Max > 0)
                    {
                        double h = v.Max * k;
                        LineArrows(g, a, b, FootingTop + h, FootingTop, loadArrow);
                        LineArrows(g, a, b, FootingBottom - h, FootingBottom, reactArrow);
                        Pickable(s.Envelope.MaxBy, $"the most down on {where}, {s.Envelope.Max:N0} plf", a, b, FootingTop, FootingTop + h);
                        Pickable(s.Envelope.MaxBy, $"the reaction under the most down on {where}, {s.Envelope.Max:N0} plf", a, b, FootingBottom - h, FootingBottom);
                        if (ReferenceEquals(jumpFrom, s) && !jumpUp) Mark(g, a, b);
                    }
                    if (v.Min < -1e-9)
                    {
                        double h = -v.Min * k, o = 0.75;
                        var ao = new Point2(a.X + ux * o, a.Y + uy * o); var bo = new Point2(b.X + ux * o, b.Y + uy * o);
                        LineArrows(g, ao, bo, FootingTop, FootingTop + h, loadArrow);
                        LineArrows(g, ao, bo, FootingBottom, FootingBottom - h, reactArrow);
                        Pickable(s.Envelope.MinBy, $"the most up on {where}, {-s.Envelope.Min:N0} plf", ao, bo, FootingTop, FootingTop + h);
                        Pickable(s.Envelope.MinBy, $"the anchorage under the most up on {where}, {-s.Envelope.Min:N0} plf", ao, bo, FootingBottom - h, FootingBottom);
                        if (ReferenceEquals(jumpFrom, s) && jumpUp) Mark(g, ao, bo);
                    }
                    void Pickable(FoundationCase c, string what, Point2 pa, Point2 pb, double z0, double z1) =>
                        picks3D.Add(new Pick3D { Case = c, What = what, Of = s, Up = what.Contains("most up"), Corners = new[] { P3(pa.X, pa.Y, z0), P3(pb.X, pb.Y, z0), P3(pb.X, pb.Y, z1), P3(pa.X, pa.Y, z1) } });
                }
            }
            foreach (var p in foundation.AllPoints)
            {
                var e = Of(p.Envelope, p.Combined);
                if (e.Max > 0)
                {
                    double len = Math.Max(1.2, e.Max / maxPoint * arrow);
                    Arrow(g, p.Where, FootingTop + len, FootingTop, loadArrow, 0.2, 0.65);                  // the load, down onto the top
                    Arrow(g, p.Where, FootingBottom - len, FootingBottom, reactArrow, 0.2, 0.65);           // the reaction, up under it
                    picks3D.Add(new Pick3D { Case = p.Envelope.MaxBy, Of = p, What = $"the most down at {p.From}, {p.Envelope.Max:N0} lb", Corners = new[] { P3(p.Where.X, p.Where.Y, FootingTop + len), P3(p.Where.X, p.Where.Y, FootingTop) } });
                    picks3D.Add(new Pick3D { Case = p.Envelope.MaxBy, Of = p, What = $"the reaction under the most down at {p.From}, {p.Envelope.Max:N0} lb", Corners = new[] { P3(p.Where.X, p.Where.Y, FootingBottom - len), P3(p.Where.X, p.Where.Y, FootingBottom) } });
                    if (ReferenceEquals(jumpFrom, p) && !jumpUp) Mark(g, p.Where, p.Where);
                }
                if (e.Min < -1e-9)
                {
                    double len = Math.Max(1.2, -e.Min / maxPoint * arrow);
                    var at = new Point2(p.Where.X + 0.9, p.Where.Y + 0.9);
                    Arrow(g, at, FootingTop, FootingTop + len, loadArrow, 0.2, 0.65);                       // the uplift, pulling the top up
                    Arrow(g, at, FootingBottom, FootingBottom - len, reactArrow, 0.2, 0.65);                // the reaction, holding it down
                    picks3D.Add(new Pick3D { Case = p.Envelope.MinBy, Of = p, Up = true, What = $"the most up at {p.From}, {-p.Envelope.Min:N0} lb", Corners = new[] { P3(at.X, at.Y, FootingTop), P3(at.X, at.Y, FootingTop + len) } });
                    picks3D.Add(new Pick3D { Case = p.Envelope.MinBy, Of = p, Up = true, What = $"the anchorage under the most up at {p.From}, {-p.Envelope.Min:N0} lb", Corners = new[] { P3(at.X, at.Y, FootingBottom), P3(at.X, at.Y, FootingBottom - len) } });
                    if (ReferenceEquals(jumpFrom, p) && jumpUp) Mark(g, at, at);
                }
            }
            return g;
        }

        // ------------------------------------------------------------------ the foundation in 3D: clicking a load goes to its case

        /// <summary>A load drawn in 3D that can be clicked: its outline (a line for an arrow, four corners for a row) and the case that governs it.</summary>
        private sealed class Pick3D
        {
            public Point3D[] Corners;
            public FoundationCase Case;
            public string What;
            public object Of;
            public bool Up;
        }

        private readonly List<Pick3D> picks3D = new List<Pick3D>();
        /// <summary>The place a click jumped from (a segment or a point) and whether its uplift: marked in gold while its case is drawn.</summary>
        private object jumpFrom;
        private bool jumpUp;
        private string jumpNote;

        /// <summary>A click on the 3D view: on (or within 10 px of) a load, to the case that governs it there.</summary>
        private bool Pick3DAt(Point click)
        {
            if (mode != Mode.Foundation3D || fb != null) return false;
            Pick3D best = null;
            double bestD = 10;
            foreach (var t in picks3D)
            {
                var pts = t.Corners.Select(c => view3D.Project(c)).ToList();
                if (pts.Any(x => x == null)) continue;
                var s = pts.Select(x => x.Value).ToList();
                // an arrow within a few pixels wins over the inside of a row of them it stands in front of
                double edge = Enumerable.Range(0, s.Count == 2 ? 1 : s.Count).Min(i => SegDist(click, s[i], s[(i + 1) % s.Count]));
                double d = s.Count >= 3 && Inside(s, click) ? Math.Min(edge, 6) : edge;
                if (d < bestD) { bestD = d; best = t; }
            }
            if (best == null || best.Case == null) return false;
            jumpFrom = best.Of; jumpUp = best.Up;
            int i0 = foundation.Cases.IndexOf(best.Case);
            jumpNote = $"Jumped here from {best.What}: this case gives it.";
            status.Text = $"{char.ToUpper(best.What[0])}{best.What.Substring(1)}: combination {i0 + 1}, {best.Case.Name}.";
            Show3DCombo(best.Case, keepJump: true);
            return true;
        }

        private static bool Inside(List<Point> poly, Point p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
            return c;
        }

        private static double SegDist(Point p, Point a, Point b)
        {
            var ab = b - a;
            double l2 = ab.LengthSquared;
            double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((p - a) * ab) / l2));
            return (p - (a + t * ab)).Length;
        }

        /// <summary>The place a click jumped from: a gold band on the footing there.</summary>
        private static void Mark(Model3DGroup g, Point2 a, Point2 b)
        {
            var gold = Mat(Color.FromRgb(0xF5, 0xB0, 0x00));
            if (Dist(a, b) < 1e-6) { a = new Point2(a.X - 0.6, a.Y); b = new Point2(b.X + 0.6, b.Y); }
            g.Children.Add(Strip(a, b, 1.5, FootingTop - 0.02, FootingTop + 0.12, gold));
        }

        /// <summary>
        /// A distributed load as drawn by hand, from z <paramref name="tail"/> to the tips at <paramref name="tip"/>: a row of thin
        /// arrows, one at each end and about every 1.5 ft between, their tails joined by a slim bar; in a light tint of the colour,
        /// so the point loads (full colour) stand apart from it.
        /// </summary>
        private static void LineArrows(Model3DGroup g, Point2 a, Point2 b, double tail, double tip, Material m)
        {
            double len = Dist(a, b);
            if (len <= 0 || Math.Abs(tip - tail) < 0.05) return;
            var light = Light(m);
            int n = Math.Max(1, (int)Math.Ceiling(len / 1.5));
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                Arrow(g, new Point2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t), tail, tip, light, 0.06, 0.22);
            }
            g.Children.Add(Strip(a, b, 0.12, tail - 0.06, tail + 0.06, light));
        }

        /// <summary>A lighter tint of a material's colour (25 % toward white), for the distributed loads: set apart from the point loads, still easy to see.</summary>
        private static Material Light(Material m)
        {
            if (!(m is DiffuseMaterial d && d.Brush is SolidColorBrush s)) return m;
            var c = s.Color;
            byte T(byte x) => (byte)(x + (255 - x) * 0.25);
            return Mat(Color.FromRgb(T(c.R), T(c.G), T(c.B)));
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

        /// <summary>
        /// A vertical arrow at a plan point, its tail at z <paramref name="zTail"/> and its tip at <paramref name="zTip"/> (up or down);
        /// <paramref name="h"/> is half the shaft's width, <paramref name="hw"/> half the head's.
        /// </summary>
        private static void Arrow(Model3DGroup g, Point2 at, double zTail, double zTip, Material m, double h, double hw)
        {
            double length = Math.Abs(zTip - zTail), dir = Math.Sign(zTip - zTail);
            if (length <= 0) return;
            double head = Math.Min(hw * 2.5, length * 0.4);
            double zBase = zTip - dir * head;                       // where the head meets the shaft
            double z0 = Math.Min(zTail, zBase), z1 = Math.Max(zTail, zBase);
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

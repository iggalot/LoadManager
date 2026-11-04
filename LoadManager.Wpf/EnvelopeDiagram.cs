using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace LoadManager.Wpf
{
    /// <summary>
    /// The envelope load diagram of one wall's footing: along the wall (from its start), the most combined line load down (blue, filled
    /// from zero) and the least (vermilion where it lifts), each step labeled; the points on it (posts, columns, shear wall ends) as
    /// arrows with their most down and most up. Line loads are to scale (plf); the points are labeled, not to that scale (lb).
    /// </summary>
    public sealed class EnvelopeDiagram : FrameworkElement
    {
        public static readonly Color Down = Color.FromRgb(0x1D, 0x4E, 0xD8);
        public static readonly Color Up = Color.FromRgb(0xD5, 0x5E, 0x00);
        private static readonly Brush Ink = Frozen(Color.FromRgb(0x24, 0x29, 0x33));
        private static readonly Brush Muted = Frozen(Color.FromRgb(0x6B, 0x72, 0x80));
        private static readonly Brush DownFill = Frozen(Color.FromArgb(0x40, Down.R, Down.G, Down.B));
        private static readonly Brush UpFill = Frozen(Color.FromArgb(0x50, Up.R, Up.G, Up.B));
        private static readonly Pen DownPen = FrozenPen(Down, 1.6);
        private static readonly Pen UpPen = FrozenPen(Up, 1.6);
        private static readonly Pen LeastPen = FrozenPen(Color.FromRgb(0x6B, 0x72, 0x80), 1, true);
        private static readonly Pen Axis = FrozenPen(Color.FromRgb(0x24, 0x29, 0x33), 1.2);
        private static readonly Pen Tick = FrozenPen(Color.FromRgb(0xC9, 0xCD, 0xD3), 1);

        private FoundationWall wall;
        private string title;

        public FoundationPoint Highlight { get; set; }

        public void Show(FoundationWall w, string heading)
        {
            wall = w;
            title = heading;
            InvalidateVisual();
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double t, bool dash = false)
        {
            var p = new Pen(Frozen(c), t);
            if (dash) p.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
            p.Freeze();
            return p;
        }

        private FormattedText Text(string s, double size, Brush b, bool bold = false) =>
            new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                              size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            if (wall == null || wall.Length <= 0)
            {
                dc.DrawText(Text("Pick a wall on the plan to see its envelope load diagram.", 12, Muted), new Point(14, 12));
                return;
            }
            dc.DrawText(Text(title ?? $"Wall {wall.Id}", 12.5, Ink, true), new Point(14, 6));
            double left = 70, right = 30, top = 86, bottom = 34;
            double pw = Math.Max(40, W - left - right), ph = Math.Max(30, H - top - bottom);
            double max = Math.Max(1, wall.Segments.Select(s => s.Envelope.Max).DefaultIfEmpty(0).Max());
            double min = Math.Min(0, wall.Segments.Select(s => s.Envelope.Min).DefaultIfEmpty(0).Min());
            double span = max - min;
            double X(double ft) => left + ft / wall.Length * pw;
            // down is drawn up the page: the most down at the top, uplift under the axis
            double Y(double v) => top + (max - v) / span * ph;

            // axis, ends, ticks
            dc.DrawLine(Tick, new Point(left, Y(max)), new Point(left + pw, Y(max)));
            if (min < 0) dc.DrawLine(Tick, new Point(left, Y(min)), new Point(left + pw, Y(min)));
            var maxLabel = Text($"{max:N0} plf", 10.5, Muted);
            dc.DrawText(maxLabel, new Point(left - 6 - maxLabel.Width, Y(max) - maxLabel.Height / 2));
            if (min < 0) { var t = Text($"{min:N0} plf", 10.5, Muted); dc.DrawText(t, new Point(left - 6 - t.Width, Y(min) - t.Height / 2)); }

            foreach (var s in wall.Segments)
            {
                double x0 = X(s.A), x1 = X(s.B);
                var e = s.Envelope;
                // the most: filled from zero
                dc.DrawRectangle(e.Max >= 0 ? DownFill : UpFill, null, new Rect(new Point(x0, Y(Math.Max(0, e.Max))), new Point(x1, Y(Math.Min(0, e.Max)))));
                dc.DrawLine(e.Max >= 0 ? DownPen : UpPen, new Point(x0, Y(e.Max)), new Point(x1, Y(e.Max)));
                // the least: filled where it lifts, a dashed line where it is still down
                if (e.Min < 0)
                {
                    dc.DrawRectangle(UpFill, null, new Rect(new Point(x0, Y(0)), new Point(x1, Y(e.Min))));
                    dc.DrawLine(UpPen, new Point(x0, Y(e.Min)), new Point(x1, Y(e.Min)));
                }
                else dc.DrawLine(LeastPen, new Point(x0, Y(e.Min)), new Point(x1, Y(e.Min)));
            }
            // each value once over the run of steps that share it
            foreach (var (a, b, v, most) in Runs(true).Concat(Runs(false)))
            {
                double x0 = X(a), x1 = X(b);
                if (x1 - x0 < 40) continue;
                var t = Text($"{v:N0}", most ? 10.5 : 10, most ? Ink : v < 0 ? Frozen(Up) : Muted);
                if (!most && v >= 0 && wall.Segments.Any(s => s.A < b && s.B > a && Math.Abs(Y(s.Envelope.Max) - Y(v)) < 2 * t.Height)) continue;
                dc.DrawText(t, new Point((x0 + x1 - t.Width) / 2, most ? Y(v) - t.Height - 1 : Y(v) + 1));
            }
            dc.DrawLine(Axis, new Point(left, Y(0)), new Point(left + pw, Y(0)));
            dc.DrawLine(Axis, new Point(left, Y(0) - 5), new Point(left, Y(0) + 5));
            dc.DrawLine(Axis, new Point(left + pw, Y(0) - 5), new Point(left + pw, Y(0) + 5));
            var a0 = Text("0", 10.5, Muted);
            dc.DrawText(a0, new Point(left - a0.Width / 2, H - bottom + 8));
            var a1 = Text($"{wall.Length:0.##} ft", 10.5, Muted);
            dc.DrawText(a1, new Point(left + pw - a1.Width / 2, H - bottom + 8));

            // the points: arrows down onto the footing from above the diagram, labeled with their most down and most up
            double lane = 0;
            foreach (var p in wall.Points)
            {
                double x = X(p.At);
                var e = p.Envelope ?? new LoadEnvelope();
                bool hi = ReferenceEquals(p, Highlight);
                var pen = new Pen(Frozen(e.Lifts ? Up : Down), hi ? 2.6 : 1.6);
                pen.Freeze();
                double yTop = top - 30, yTip = Y(Math.Max(0, wall.Segments.Where(s => p.At >= s.A - 1e-6 && p.At <= s.B + 1e-6).Select(s => s.Envelope.Max).DefaultIfEmpty(0).Max()));
                dc.DrawLine(pen, new Point(x, yTop), new Point(x, yTip));
                var head = new StreamGeometry();
                using (var g = head.Open())
                {
                    g.BeginFigure(new Point(x, yTip), true, true);
                    g.LineTo(new Point(x - 4, yTip - 8), true, false);
                    g.LineTo(new Point(x + 4, yTip - 8), true, false);
                }
                head.Freeze();
                dc.DrawGeometry(pen.Brush, null, head);
                string label = $"{Short(p.From)}: {e.Max:N0}" + (e.Lifts ? $" / {e.Min:N0}" : "") + " lb";
                var t = Text(label, 10, e.Lifts ? Frozen(Up) : Ink, hi);
                double lx = Math.Min(Math.Max(2, x - t.Width / 2), W - t.Width - 2);
                double ly = yTop - t.Height - (lane % 2) * (t.Height + 1);
                dc.DrawText(t, new Point(lx, ly));
                lane++;
                // the arrow's place along the wall
                var at = Text($"{p.At:0.#}", 9.5, Muted);
                dc.DrawText(at, new Point(x - at.Width / 2, H - bottom + 20));
            }
        }

        /// <summary>The runs of adjacent steps with the same most (or least): from, to, the value.</summary>
        private IEnumerable<(double a, double b, double v, bool most)> Runs(bool most)
        {
            double? a = null, b = 0, v = 0;
            foreach (var s in wall.Segments)
            {
                double x = most ? s.Envelope.Max : s.Envelope.Min;
                if (a != null && Math.Abs(s.A - b.Value) < 1e-6 && Math.Abs(x - v.Value) < 0.5) { b = s.B; continue; }
                if (a != null) yield return (a.Value, b.Value, v.Value, most);
                a = s.A; b = s.B; v = x;
            }
            if (a != null) yield return (a.Value, b.Value, v.Value, most);
        }

        private static string Short(string from) => from == null ? "" : from.Length <= 22 ? from : from.Substring(0, 21) + "…";
    }
}

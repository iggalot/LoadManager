using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LoadManager.Wpf
{
    /// <summary>
    /// The foundation's combined loads, all the numbers (read only): the combinations used with their load factors, each wall's line
    /// loads along it and the points on it at service level with their envelope and governing combinations, and (collapsed, on demand)
    /// every combination's value on each.
    /// </summary>
    public sealed class FoundationAnalysisWindow : Window
    {
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));

        /// <param name="focusWallId">A wall to open its per-combination table for (null: all collapsed).</param>
        public FoundationAnalysisWindow(FoundationAnalysis a, string focusWallId = null)
        {
            Title = "Foundation loads, combined";
            Width = 1180; Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new StackPanel { Margin = new Thickness(14) };
            void Text(string t, bool muted = false) => root.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Muted : Brushes.Black, Margin = new Thickness(0, 0, 0, 6) });
            string Sym(LoadType t) => LoadCombinations.Symbol(t);
            string F(double v) => v == 0 ? "" : v.ToString("0.###");
            string Pick(FoundationCase c) => c?.Name ?? "";

            bool asd = a.Method == DesignMethod.Asd;
            Text((asd ? "Allowable stress design: ASCE 7-22 2.4 and the IBC 2024 1605.2 alternative combinations. "
                      : "Strength design (LRFD): ASCE 7-22 2.3. ") +
                 "Each combination is investigated with its variable loads set to zero where that is worse: \"most\" is the most down it can give " +
                 "(wind at its most pressing down), \"least\" the most up (wind at its most lifting, the gravity variable loads off). Wind is the roof's " +
                 "whole-building cases carried down the load path and the shear walls' end forces, W at strength level as ASCE 7 gives it.");
            foreach (var s in a.Skipped) Text(s, true);

            root.Children.Add(FoundationLoadsWindow.Head($"Combinations and their load factors ({a.Combinations.Count})"));
            var types = new[] { LoadType.Dead, LoadType.Live, LoadType.RoofLive, LoadType.Snow, LoadType.Wind };
            root.Children.Add(FoundationLoadsWindow.Table(new[] { "Combination", "From" }.Concat(types.Select(Sym)).ToArray(),
                a.Combinations.Select(c => new[] { c.Name, c.Equation }.Concat(types.Select(t => F(c.Factor(t)))).ToArray()), "Combinations"));

            string W(ServiceLoad l) => l.Has(LoadType.Wind) ? $"{l.WindMin:N0} to {l.WindMax:N0}" : "";
            string[] Service(ServiceLoad l) => new[] { $"{l.Dead:N0}", l.Live > 0 ? $"{l.Live:N0}" : "", l.RoofLive > 0 ? $"{l.RoofLive:N0}" : "", l.Snow > 0 ? $"{l.Snow:N0}" : "", W(l) };
            string[] Env(LoadEnvelope e) => new[] { $"{e.Max:N0}", Pick(e.MaxBy), e.Lifts ? $"{e.Min:N0} up" : $"{e.Min:N0}", Pick(e.MinBy) };

            var order = new FoundationLoadsWindow.IdOrder();
            foreach (var w in a.Walls.OrderBy(x => x.Id, order))
            {
                var e = w.Line ?? new LoadEnvelope();
                root.Children.Add(FoundationLoadsWindow.Head($"Wall {w.Id}, {w.Length:0.##} ft: most {e.Max:N0} plf ({Pick(e.MaxBy)}), least {e.Min:N0} plf ({Pick(e.MinBy)})"));
                if (w.Segments.Count > 0)
                    root.Children.Add(FoundationLoadsWindow.Table(new[] { "Along it (ft)", "D (plf)", "L", "Lr", "S", "W range", "Most (plf)", "by", "Least (plf)", "by" },
                        w.Segments.Select(s => new[] { $"{s.A:0.##} to {s.B:0.##}" }.Concat(Service(s.Load)).Concat(Env(s.Envelope)).ToArray()), $"Wall {w.Id} line loads"));
                else Text("No line load on it.", true);
                if (w.Points.Count > 0)
                    root.Children.Add(FoundationLoadsWindow.Table(new[] { "Point", "At (ft)", "D (lb)", "L", "Lr", "S", "W range", "Most (lb)", "by", "Least (lb)", "by" },
                        w.Points.Select(p => new[] { p.From, $"{p.At:0.##}" }.Concat(Service(p.Load)).Concat(Env(p.Envelope)).ToArray()), $"Wall {w.Id} points"));
                // every combination, on demand
                var heads = new List<string> { "Combination" };
                if (w.Segments.Count > 0) heads.AddRange(new[] { "Line most (plf)", "Line least (plf)" });
                heads.AddRange(w.Points.Select(p => $"{p.From}: most / least (lb)"));
                var rows = a.Cases.Select((c, i) =>
                {
                    var r = new List<string> { c.Name };
                    if (w.Segments.Count > 0)
                    {
                        r.Add($"{w.Segments.Max(s => s.Combined[i].Max):N0}");
                        r.Add($"{w.Segments.Min(s => s.Combined[i].Min):N0}");
                    }
                    r.AddRange(w.Points.Select(p => $"{p.Combined[i].Max:N0} / {p.Combined[i].Min:N0}"));
                    return r.ToArray();
                });
                root.Children.Add(new Expander
                {
                    Header = "Every combination on it", IsExpanded = w.Id == focusWallId, Margin = new Thickness(0, 4, 0, 0),
                    Content = FoundationLoadsWindow.Table(heads.ToArray(), rows, $"Wall {w.Id} combinations"),
                });
            }

            if (a.Points.Count > 0)
            {
                root.Children.Add(FoundationLoadsWindow.Head($"Points on their own ({a.Points.Count}): columns on pads, and forces where no footing is drawn"));
                root.Children.Add(FoundationLoadsWindow.Table(new[] { "Point", "Where (ft)", "D (lb)", "L", "Lr", "S", "W range", "Most (lb)", "by", "Least (lb)", "by" },
                    a.Points.OrderBy(p => p.From, order).Select(p => new[] { p.From, $"({p.Where.X:0.##}, {p.Where.Y:0.##})" }.Concat(Service(p.Load)).Concat(Env(p.Envelope)).ToArray()), "Points on their own"));
            }
            if (a.Notes.Count > 0)
            {
                root.Children.Add(FoundationLoadsWindow.Head("Notes"));
                foreach (var n in a.Notes) Text("• " + n, true);
            }
            var close = new Button { Content = "Close", Width = 80, IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            close.Click += (o, e) => Close();
            root.Children.Add(close);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
}

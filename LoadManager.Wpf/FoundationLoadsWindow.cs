using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingModel.Model;

namespace LoadManager.Wpf
{
    /// <summary>
    /// What the gravity load path brings to the foundation (read only): each wall on the ground (its line load at the base and
    /// the posts in it) and each column standing on the foundation, at service level by kind and the largest ASD combination.
    /// </summary>
    public sealed class FoundationLoadsWindow : Window
    {
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
        private static readonly Brush Band = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF2));

        public FoundationLoadsWindow(Building b)
        {
            Title = "Foundation loads";
            Width = 1100; Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            string Kinds(GravityLoad g) => $"D {g.Dead:N0}, L {g.Live:N0}, Lr {g.RoofLive:N0}, S {g.Snow:N0}";
            string Most(GravityLoad g)
            {
                var m = LoadCombinations.Largest(g);
                return m == null ? "" : $"{m.Value.total:N0} ({m.Value.combination.Name})";
            }

            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "What the gravity load path brings down to the foundation, from the floor and roof loads (Loads > Floor and roof loads) and the walls' own weight. " +
                       "Service loads by kind, and the largest allowable stress (ASD) combination of them (ASCE 7-22 2.4 and IBC 1605.2; wind and earthquake left out). Footings are not sized here.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });

            var walls = r.Foundation.Where(f => f.IsWall).OrderBy(f => f.Id, new IdOrder()).ToList();
            root.Children.Add(Head($"Walls on the ground ({walls.Count})"));
            root.Children.Add(Table(new[] { "Wall", "Length", "Line load at the base (plf)", "Largest ASD (plf)", "Posts in it (lb, each to the foundation)" },
                walls.Select(f => new[]
                {
                    f.Id, $"{f.Length:0.##} ft", Kinds(f.Line), Most(f.Line),
                    f.Points.Count == 0 ? "none" : string.Join("; ", f.Points.OrderBy(p => p.At).Select(p => $"{p.From} at {p.At:0.##} ft: {Kinds(p.Load)}, largest {Most(p.Load)}")),
                }), "Foundation walls"));

            var cols = r.Foundation.Where(f => !f.IsWall).OrderBy(f => f.Id, new IdOrder()).ToList();
            root.Children.Add(Head($"Columns on the foundation ({cols.Count})"));
            root.Children.Add(Table(new[] { "Column", "At (x, y)", "Load (lb)", "Largest ASD (lb)", "Carries" },
                cols.Select(f => new[]
                {
                    f.Id, $"{f.Where.X:0.##}, {f.Where.Y:0.##}", Kinds(f.Point), Most(f.Point),
                    r.Columns.TryGetValue(f.Id, out var c) && c.From.Count > 0 ? string.Join(", ", c.From) : "nothing",
                }), "Foundation columns"));

            var entered = r.Values.Where(w => w.Entered).Select(w => $"{w.WallId} at {b.Level(w.LevelId)?.Name}: its top load is the one entered by hand ({Kinds(w.Load)} plf; the load path finds {Kinds(w.Computed)}), and it goes on down from there.");
            var notes = entered.Concat(r.Notes).Concat(r.Values.SelectMany(w => w.Notes.Select(n => $"{w.WallId}: {n}")))
                               .Concat(r.Beams.Values.SelectMany(m => m.Notes.Select(n => $"{m.BeamId}: {n}"))).Distinct().ToList();
            if (notes.Count > 0)
            {
                root.Children.Add(Head("Notes"));
                foreach (var n in notes) root.Children.Add(new TextBlock { Text = "• " + n, TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(0, 0, 0, 3) });
            }
            root.Children.Add(new TextBlock
            {
                Text = "A wall's line load is the most along it (a uniform load as large as its largest part). Each post is its concentrated load alone; the wall's line load around it is in the wall's line load.",
                TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 10, 0, 0),
            });
            var close = new Button { Content = "Close", Width = 80, IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            close.Click += (o, e) => Close();
            root.Children.Add(close);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static TextBlock Head(string text) => new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 10, 0, 4) };

        private static Grid Table(string[] heads, IEnumerable<string[]> rows, string name)
        {
            var g = new Grid();
            AutomationProperties.SetName(g, name);
            for (int c = 0; c < heads.Length; c++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = c == heads.Length - 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            void Row(string[] cells, bool head, int r)
            {
                g.RowDefinitions.Add(new RowDefinition());
                for (int c = 0; c < cells.Length; c++)
                {
                    var t = new TextBlock { Text = cells[c], TextWrapping = TextWrapping.Wrap, Padding = new Thickness(4, 3, 14, 3), FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal };
                    var border = new Border { Child = t, Background = !head && r % 2 == 0 ? Band : null };
                    Grid.SetRow(border, r); Grid.SetColumn(border, c);
                    g.Children.Add(border);
                }
            }
            Row(heads, true, 0);
            int i = 1;
            foreach (var row in rows) Row(row, false, i++);
            if (i == 1) Row(heads.Select((h, k) => k == 0 ? "none" : "").ToArray(), false, i);
            return g;
        }

        /// <summary>W2 before W10: the letters, then the number.</summary>
        private sealed class IdOrder : IComparer<string>
        {
            public int Compare(string a, string b)
            {
                string pa = new string((a ?? "").TakeWhile(ch => !char.IsDigit(ch)).ToArray()), pb = new string((b ?? "").TakeWhile(ch => !char.IsDigit(ch)).ToArray());
                int c = string.CompareOrdinal(pa, pb);
                if (c != 0) return c;
                int.TryParse((a ?? "").Substring(pa.Length), out int na);
                int.TryParse((b ?? "").Substring(pb.Length), out int nb);
                return na != nb ? na.CompareTo(nb) : string.CompareOrdinal(a, b);
            }
        }
    }
}

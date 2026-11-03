using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingModel.Model;

namespace LoadManager.Wpf
{
    /// <summary>
    /// The floor and roof loads: each level's area loads (D, L, Lr, S psf) and the walls' own weight. The host applies the
    /// result as one edit (<see cref="LoadSection.SetAreaLoads"/>) when the window closes with OK.
    /// </summary>
    public sealed class AreaLoadsWindow : Window
    {
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
        private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xF6, 0xD5, 0xD5));
        private readonly List<(Level level, TextBox[] boxes)> rows = new List<(Level, TextBox[])>();
        private readonly TextBox wallBox;

        /// <summary>The loads entered, by level ID (when OK).</summary>
        public Dictionary<string, AreaLoad> Levels { get; } = new Dictionary<string, AreaLoad>();
        /// <summary>The walls' own weight entered (psf of wall), when OK.</summary>
        public double WallDead { get; private set; }

        public AreaLoadsWindow(Building b)
        {
            Title = "Floor and roof loads";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var s = LoadSection.Read(b);
            var roof = b.RoofLevel();

            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "Area loads on each level's floor or roof (psf, service level, on the plan). The gravity load path takes them to the walls under each level by tributary width, then down the walls standing on walls.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Margin = new Thickness(0, 0, 0, 10),
            });

            var grid = new Grid();
            foreach (var w in new[] { 110.0, 250, 64, 64, 64, 64 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            void Cell(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); grid.Children.Add(e); }
            grid.RowDefinitions.Add(new RowDefinition());
            string[] heads = { "Level", "Framing", "D", "L", "Lr", "S" };
            for (int c = 0; c < heads.Length; c++) Cell(new TextBlock { Text = heads[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) }, 0, c);

            foreach (var lv in b.LevelsBottomUp.Where(l => l.Elevation > 0).OrderByDescending(l => l.Elevation))
            {
                int r = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition());
                Cell(new TextBlock { Text = $"{lv.Name} ({lv.Elevation:0.##} ft)", VerticalAlignment = VerticalAlignment.Center }, r, 0);
                Cell(new TextBlock { Text = Framing(b, lv, roof), Foreground = Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, r, 1);
                var a = s.Level(lv.Id);
                var boxes = new TextBox[4];
                double[] v = { a?.Dead ?? 0, a?.Live ?? 0, a?.RoofLive ?? 0, a?.Snow ?? 0 };
                string[] sym = { "D", "L", "Lr", "S" };
                for (int k = 0; k < 4; k++)
                {
                    boxes[k] = Box(v[k]);
                    AutomationProperties.SetName(boxes[k], $"Area {sym[k]} {lv.Name}");
                    Cell(boxes[k], r, 2 + k);
                }
                rows.Add((lv, boxes));
            }
            root.Children.Add(grid);

            var wall = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            wall.Children.Add(new TextBlock { Text = "Walls' own weight (psf of wall):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            wallBox = Box(s.WallDead);
            AutomationProperties.SetName(wallBox, "Wall dead");
            wall.Children.Add(wallBox);
            wall.Children.Add(new TextBlock { Text = "carried down to the wall or footing under each wall", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            root.Children.Add(wall);

            root.Children.Add(new TextBlock
            {
                Text = "Live loads: IBC Table 1607.1. Snow: ASCE 7-22 Chapter 7 (the roof snow load on the plan). Roof dead load on the plan: a sloped roof's dead load along the slope divided by cos(slope). " +
                       "The framing's span is the building model's (each diaphragm's, Auto: its shorter way; a sloped roof's rafters run across the ridge).",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 10, 0, 0),
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            ok.Click += (o, e) => Accept();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        private static TextBox Box(double v) => new TextBox
        {
            Text = v > 0 ? v.ToString("0.##", CultureInfo.InvariantCulture) : "", Width = 56, Margin = new Thickness(0, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>A blank box is 0; anything else must be a number not below 0. Null when it is not.</summary>
        private static double? Value(TextBox t)
        {
            string x = (t.Text ?? "").Trim();
            if (x.Length == 0) return 0;
            return double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 && !double.IsInfinity(v) ? v : (double?)null;
        }

        private void Accept()
        {
            bool good = true;
            foreach (var t in rows.SelectMany(r => r.boxes).Concat(new[] { wallBox }))
            {
                bool ok = Value(t) != null;
                t.Background = ok ? Brushes.White : Bad;
                good &= ok;
            }
            if (!good) return;
            Levels.Clear();
            foreach (var (lv, b) in rows)
                Levels[lv.Id] = new AreaLoad { Dead = Value(b[0]).Value, Live = Value(b[1]).Value, RoofLive = Value(b[2]).Value, Snow = Value(b[3]).Value };
            WallDead = Value(wallBox).Value;
            DialogResult = true;
        }

        /// <summary>"Floor: joists span S-N (Auto)", "Roof: gable, rafters span S-N (across the ridge)".</summary>
        private static string Framing(Building b, Level lv, Level roof)
        {
            bool isRoof = roof != null && lv.Id == roof.Id;
            string Dir(SpanDirection d) => d == SpanDirection.WestEast ? "W-E" : "S-N";
            if (isRoof && b.Roof != null && b.Roof.IsSloped)
                return $"Roof: {b.Roof.Shape.ToString().ToLowerInvariant()}, rafters span {(b.EffectiveRidge() == RidgeAxis.WestEast ? "S-N" : "W-E")} (across the ridge)";
            var ds = b.Diaphragms.Where(d => d.LevelId == lv.Id).ToList();
            if (ds.Count == 0) return (isRoof ? "Roof" : "Floor") + ": no diaphragm drawn (nothing to load)";
            var spans = ds.Select(d => Dir(d.EffectiveSpan) + (d.Span == SpanDirection.Auto ? " (Auto)" : "")).Distinct().ToList();
            return $"{(isRoof ? "Roof" : "Floor")}: {(isRoof ? "rafters" : "joists")} span {string.Join(", ", spans)}";
        }
    }
}

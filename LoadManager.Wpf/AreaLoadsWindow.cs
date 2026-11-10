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
        /// <summary>The load zones entered (when OK).</summary>
        public List<LoadZone> Zones { get; } = new List<LoadZone>();
        /// <summary>The walls' own weight entered (psf of wall), when OK.</summary>
        public double WallDead { get; private set; }

        /// <param name="readOnly">View only (someone else is working on the project): the loads shown, nothing typed, Close only.</param>
        public AreaLoadsWindow(Building b, bool readOnly = false)
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

            // load zones: parts of a level with their own loads
            root.Children.Add(new TextBlock { Text = "Load zones", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            root.Children.Add(new TextBlock
            {
                Text = "A rectangle of a level whose loads take the place of the level's there: a deck, a corridor, a storage room (ft from the drawing origin, x to the east, y to the north; psf). Where zones overlap, the larger of each kind.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 760, Foreground = Muted, Margin = new Thickness(0, 0, 0, 4),
            });
            levelsDown = b.LevelsBottomUp.Where(l => l.Elevation > 0).OrderByDescending(l => l.Elevation).ToList();
            zoneRows = new StackPanel();
            zoneRows.Children.Add(ZoneRow(null, header: true));
            foreach (var z in s.Zones ?? new List<LoadZone>()) zoneRows.Children.Add(ZoneRow(z));
            root.Children.Add(zoneRows);
            var add = new Button { Content = "Add zone", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            add.Click += (o, e) =>
            {
                // a new zone on the top level with that level's loads as typed, to be changed
                var lv = levelsDown.FirstOrDefault();
                var boxes = rows.FirstOrDefault(r => r.level == lv).boxes;
                double V(int k) => boxes == null ? 0 : Value(boxes[k]) ?? 0;
                zoneRows.Children.Add(ZoneRow(new LoadZone { LevelId = lv?.Id, Name = $"Zone {zoneRows.Children.Count}", Load = new AreaLoad { Dead = V(0), Live = V(1), RoofLive = V(2), Snow = V(3) } }));
            };
            root.Children.Add(add);

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
            if (readOnly) MakeReadOnly(root, cancel);
        }

        /// <summary>View only: every box read only, the choices off, no Add or Remove or OK; Cancel is Close.</summary>
        private void MakeReadOnly(DependencyObject root, Button close)
        {
            Title += " (view only)";
            void Walk(DependencyObject d)
            {
                if (d is TextBox t) t.IsReadOnly = true;
                else if (d is ComboBox c) c.IsEnabled = false;
                else if (d is CheckBox k) k.IsEnabled = false;
                else if (d is Button bt && bt != close) bt.Visibility = Visibility.Collapsed;
                foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
            }
            Walk(root);
            close.Content = "Close";
            close.IsDefault = true;
        }

        private readonly List<Level> levelsDown;
        private readonly StackPanel zoneRows;
        private static readonly double[] ZoneWidths = { 130, 110, 56, 56, 56, 56, 52, 52, 52, 52, 70 };

        /// <summary>One zone's row (its controls kept in the row's Tag), or the header.</summary>
        private Grid ZoneRow(LoadZone z, bool header = false)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            foreach (var w in ZoneWidths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            void Put(UIElement e, int c) { Grid.SetColumn(e, c); g.Children.Add(e); }
            if (header)
            {
                string[] heads = { "Level", "Name", "West x", "South y", "Width", "Depth", "D", "L", "Lr", "S", "" };
                for (int c = 0; c < heads.Length; c++) Put(new TextBlock { Text = heads[c], FontWeight = FontWeights.SemiBold }, c);
                return g;
            }
            int k = zoneRows.Children.Count;     // 1 for the first zone (the header is 0)
            var level = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
            foreach (var lv in levelsDown) level.Items.Add(new ComboBoxItem { Content = lv.Name, Tag = lv.Id, IsSelected = lv.Id == z?.LevelId });
            if (level.SelectedIndex < 0 && level.Items.Count > 0) level.SelectedIndex = 0;
            var name = new TextBox { Text = z?.Name ?? "", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var nums = new[] { z?.X0 ?? 0, z?.Y0 ?? 0, z == null ? 0 : z.X1 - z.X0, z == null ? 0 : z.Y1 - z.Y0, z?.Load?.Dead ?? 0, z?.Load?.Live ?? 0, z?.Load?.RoofLive ?? 0, z?.Load?.Snow ?? 0 }
                .Select(v => new TextBox { Text = v.ToString("0.##", CultureInfo.InvariantCulture), Width = 48, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }).ToArray();
            string[] names = { "x", "y", "width", "depth", "D", "L", "Lr", "S" };
            AutomationProperties.SetName(level, $"Zone {k} level");
            AutomationProperties.SetName(name, $"Zone {k} name");
            for (int c = 0; c < nums.Length; c++) AutomationProperties.SetName(nums[c], $"Zone {k} {names[c]}");
            var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(remove, $"Zone {k} remove");
            remove.Click += (o, e) => zoneRows.Children.Remove(g);
            Put(level, 0); Put(name, 1);
            for (int c = 0; c < nums.Length; c++) Put(nums[c], 2 + c);
            Put(remove, 10);
            g.Tag = (level, name, nums);
            return g;
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

        /// <summary>A coordinate: any number (blank is 0); null when it is not one.</summary>
        private static double? Signed(TextBox t)
        {
            string x = (t.Text ?? "").Trim();
            if (x.Length == 0) return 0;
            return double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsInfinity(v) && !double.IsNaN(v) ? v : (double?)null;
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
            // the zones: every number not below 0, and a width and depth
            var zones = new List<LoadZone>();
            foreach (var row in zoneRows.Children.OfType<Grid>().Where(r => r.Tag != null))
            {
                var (level, name, nums) = ((ComboBox, TextBox, TextBox[]))row.Tag;
                var v = new double?[nums.Length];
                for (int c = 0; c < nums.Length; c++)
                {
                    v[c] = c < 2 ? Signed(nums[c]) : Value(nums[c]);
                    bool ok = v[c] != null && (c < 2 || c > 3 || v[c] > 0);
                    nums[c].Background = ok ? Brushes.White : Bad;
                    good &= ok;
                }
                if (v.All(x => x != null) && level.SelectedItem is ComboBoxItem li)
                    zones.Add(new LoadZone
                    {
                        LevelId = (string)li.Tag, Name = name.Text, X0 = v[0].Value, Y0 = v[1].Value, X1 = v[0].Value + v[2].Value, Y1 = v[1].Value + v[3].Value,
                        Load = new AreaLoad { Dead = v[4].Value, Live = v[5].Value, RoofLive = v[6].Value, Snow = v[7].Value },
                    });
            }
            if (!good) return;
            Zones.Clear();
            Zones.AddRange(zones);
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

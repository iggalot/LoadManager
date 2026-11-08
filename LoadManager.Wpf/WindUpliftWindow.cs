using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingModel.Model;

namespace LoadManager.Wpf
{
    /// <summary>
    /// Wind uplift (read only), from the wind calculator's roof pressures: the rafter or truss ties at the roof by components and
    /// cladding, and by the whole-building cases what the load path brings to each wall top, beam end, column and the foundation,
    /// each at 0.6D + 0.6W with the case that lifts it most.
    /// </summary>
    public sealed class WindUpliftWindow : Window
    {
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));

        /// <param name="noWind">Why there are no wind cases (shown instead of the tables), or null.</param>
        public WindUpliftWindow(Building b, WindUplift u, string noWind)
        {
            Title = "Wind uplift";
            Width = 1150; Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new StackPanel { Margin = new Thickness(14) };
            void Text(string t, bool muted = false) => root.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Muted : Brushes.Black, Margin = new Thickness(0, 0, 0, 6) });

            Text("The wind on the roof lifting against its dead load, with the wind calculator's pressures: the allowable stress combination " +
                 "0.6D + 0.6W (ASCE 7-22 2.4 Eq. 7a; the IBC 1605.2 alternative counts two-thirds of the dead load, less severe). Only dead loads " +
                 "resist: the floors' and roof's (Loads > Floor and roof loads, their zones included), the walls' own weight, and those entered by hand. " +
                 "A negative net is uplift; a positive one is held down by the dead load.");
            if (u == null || (!u.HasWind && u.Cladding == null))
            {
                Text(noWind ?? "No wind loads: link the wind loads to the wind calculator (Wind setup) to see the uplift.");
                AddClose(root);
                Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                return;
            }
            string Lvl(string id) => b.Level(id)?.Name ?? id;
            string Net(double v) => v < -0.5 ? $"{-v:N0} up" : v > 0.5 ? $"{v:N0} down (held)" : "0";
            var order = new FoundationLoadsWindow.IdOrder();

            // ---- the ties (C&C)
            root.Children.Add(FoundationLoadsWindow.Head("Rafter and truss ties at the roof (the worse of C&C and MWFRS)"));
            if (u.Ties.Count > 0)
            {
                Text("Each tie takes the components and cladding uplift where it is higher than the whole-building (MWFRS) uplift at the same wall or beam, " +
                     "else the MWFRS case lifting it most: the worst case. " +
                     (u.Cladding != null
                        ? "C&C: the wind calculator's roof zones (ASCE 7 Ch. 30 Part 1, its edition and inputs), read at each rafter's effective wind area: its span " +
                          "(to the next wall or beam, from the load path) × the larger of its spacing and a third of its span (26.2), with internal pressure and the " +
                          "16 psf minimum (30.2.2); the eaves at the overhang coefficients. "
                        : "No components and cladding results here: the MWFRS alone. ") +
                     "The roof's share on each wall or beam (its tributary strip, the eaves included) against the roof's dead load; per rafter at the roof framing " +
                     $"spacing, {u.Ties.Values.Select(t => t.SpacingIn).DefaultIfEmpty(24).First():0.#} in. (the model's default).", true);
                var ties = u.Ties.Values.OrderBy(t => t.IsBeam).ThenBy(t => t.Id, order).ToList();
                root.Children.Add(FoundationLoadsWindow.Table(new[] { "At the roof", "Rafter span, area", "C&C: 0.6D + 0.6W (plf)", "MWFRS: 0.6D + 0.6W (plf)", "Governs", "Roof dead D (plf)", "Wind W (plf)", "0.6D + 0.6W (plf)", "Per rafter (lb)" },
                    ties.Select(t => new[]
                    {
                        (t.IsBeam ? "beam " : "") + t.Id, $"{t.SpanFt:0.#} ft, {t.Area:0} sq ft",
                        t.Cladding == null ? "none" : Net(t.Cladding.Net), t.Mwfrs == null ? "none" : $"{Net(t.Mwfrs.Net)} ({t.MwfrsCase?.Name})",
                        t.Governs, $"{t.Line.Dead:N0}", $"{t.Line.Wind:N0}", Net(t.Line.Net), Net(t.PerRafter),
                    }), "Roof ties"));
            }
            else Text("No wind at the roof: the ties are not found.", true);

            if (u.HasWind)
            {
                Text("Whole-building wind (MWFRS), " + u.Runs.Count + " cases: " + string.Join("; ", u.Runs.Select(x => x.Case.Name)) + ". " + u.Runs[0].Case.Source + ". " +
                     "Each case goes down the load path as the gravity loads do; on each wall, beam, column and footing the case lifting it most is shown, " +
                     "its dead and wind at the same place along the wall.", true);
                string Case(RoofWindCase c) => c?.Name ?? "";
                string CaseOf<T>(Governing<T> g) where T : GravityLoad => g == null || g.Load.IsEmpty ? "" : Case(g.Case);

                // ---- wall tops
                var path = u.Runs[0].Path;
                var tops = path.Values.OrderByDescending(w => b.Level(w.LevelId)?.Elevation ?? 0).ThenBy(w => w.WallId, order)
                               .Select(w => (w, g: u.WallTop(GravityLoadPath.Key(w.WallId, w.LevelId)))).Where(x => x.g != null && !x.g.Load.IsEmpty).ToList();
                root.Children.Add(FoundationLoadsWindow.Head($"Wall tops ({tops.Count})"));
                root.Children.Add(FoundationLoadsWindow.Table(new[] { "Wall", "Top level", "D (plf)", "W (plf)", "0.6D + 0.6W (plf)", "Governing case" },
                    tops.Select(x => new[] { x.w.WallId, Lvl(x.w.LevelId), $"{x.g.Load.Dead:N0}", $"{x.g.Load.Wind:N0}", Net(x.g.Net), CaseOf(x.g) }), "Uplift wall tops"));

                // ---- beams
                var beams = path.Beams.Values.OrderBy(m => m.BeamId, order).ToList();
                if (beams.Count > 0)
                {
                    root.Children.Add(FoundationLoadsWindow.Head($"Beam ends ({beams.Count} beams)"));
                    root.Children.Add(FoundationLoadsWindow.Table(new[] { "Beam", "Level", "Start: 0.6D + 0.6W (lb)", "End: 0.6D + 0.6W (lb)", "Governing case" },
                        beams.Select(m =>
                        {
                            var s = u.BeamEnd(m.BeamId, true); var e = u.BeamEnd(m.BeamId, false);
                            var worse = s == null ? e : e == null ? s : e.Net < s.Net ? e : s;
                            return new[] { m.BeamId, Lvl(m.LevelId), $"{Net(s?.Net ?? 0)} on {m.StartOn ?? "nothing drawn"}", $"{Net(e?.Net ?? 0)} on {m.EndOn ?? "nothing drawn"}", CaseOf(worse) };
                        }), "Uplift beams"));
                }

                // ---- columns
                var cols = path.Columns.Values.OrderBy(c => c.ColumnId, order).ToList();
                if (cols.Count > 0)
                {
                    root.Children.Add(FoundationLoadsWindow.Head($"Columns ({cols.Count})"));
                    root.Children.Add(FoundationLoadsWindow.Table(new[] { "Column", "Stands on", "0.6D + 0.6W at its base (lb)", "Governing case" },
                        cols.Select(c => { var g = u.Column(c.ColumnId); return new[] { c.ColumnId, c.BaseOn ?? "nothing drawn", Net(g?.Net ?? 0), CaseOf(g) }; }), "Uplift columns"));
                }

                // ---- the foundation
                var fw = path.Foundation.Where(f => f.IsWall).OrderBy(f => f.Id, order).ToList();
                root.Children.Add(FoundationLoadsWindow.Head($"At the foundation: walls ({fw.Count})"));
                root.Children.Add(FoundationLoadsWindow.Table(new[] { "Wall", "D (plf)", "W (plf)", "0.6D + 0.6W (plf)", "Governing case", "Posts in it, 0.6D + 0.6W (lb)" },
                    fw.Select(f =>
                    {
                        var g = u.FoundationWall(f.Id);
                        var posts = f.Points.Count == 0 ? "none" : string.Join("; ", f.Points.OrderBy(p => p.At).Select(p => $"{p.From} at {p.At:0.##} ft: {Net(u.FoundationPost(f.Id, p.From)?.Net ?? 0)}"));
                        return new[] { f.Id, $"{g?.Load.Dead ?? 0:N0}", $"{g?.Load.Wind ?? 0:N0}", Net(g?.Net ?? 0), CaseOf(g), posts };
                    }), "Uplift foundation walls"));
            }
            else Text("No whole-building (MWFRS) results: the uplift is not carried down the load path.", true);

            Text("The tie is a component (components and cladding, ASCE 7 26.2 and Ch. 30); the load path carries the whole-building wind (Ch. 27), as ASCE 7 " +
                 "assigns them. The wind on a lower roof or a floor is not included: only the top roof's. A line load is at the place along the wall with the most net uplift.", true);
            AddClose(root);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void AddClose(StackPanel root)
        {
            var close = new Button { Content = "Close", Width = 80, IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            close.Click += (o, e) => Close();
            root.Children.Add(close);
        }
    }
}

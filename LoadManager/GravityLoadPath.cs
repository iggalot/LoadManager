using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>What lands on a wall's top on one story, by the gravity load path.</summary>
    public sealed class WallTopLoad
    {
        public string WallId { get; set; }
        /// <summary>The level on top of the story (the one the wall carries).</summary>
        public string LevelId { get; set; }
        /// <summary>The most along the wall (plf): <see cref="FromLevel"/> + <see cref="FromAbove"/>.</summary>
        public LineLoad Load { get; set; } = new LineLoad();
        /// <summary>Its share of the level's floor or roof (plf, the most along the wall).</summary>
        public LineLoad FromLevel { get; set; } = new LineLoad();
        /// <summary>What the wall or walls standing on it bring down, with their own weight (plf, the most along it).</summary>
        public LineLoad FromAbove { get; set; } = new LineLoad();
        /// <summary>The tributary width of the level's framing (ft), the least and the most along the wall.</summary>
        public double TributaryMin { get; set; }
        public double TributaryMax { get; set; }
        /// <summary>The walls standing on it ("W7").</summary>
        public List<string> Above { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();

        /// <summary>One line: "trib 6–10 ft; from the wall above W7".</summary>
        public string Describe()
        {
            string t = TributaryMax <= 0 ? "no floor or roof bears on it" :
                Math.Abs(TributaryMax - TributaryMin) < 0.005 ? $"tributary {TributaryMax:0.##} ft" : $"tributary {TributaryMin:0.##} to {TributaryMax:0.##} ft";
            return t + (Above.Count > 0 ? "; plus the wall" + (Above.Count > 1 ? "s" : "") + " above " + string.Join(", ", Above) : "");
        }
    }

    /// <summary>
    /// The gravity load path, a first pass: each level's area loads (<see cref="LoadSection.Levels"/>) go to the walls and
    /// beams under it by tributary width — the framing spans one way (the diaphragm's <see cref="Diaphragm.EffectiveSpan"/>;
    /// a sloped roof across its ridge) and each bearing line takes half the distance to the next one on each side, or all
    /// of it to the edge where there is none (an eave, a cantilever, or a support not drawn). Walls standing on a wall
    /// below bring their load and their own weight (<see cref="LoadSection.WallDead"/>) down to it; the levels are taken
    /// from the top down. The most along each wall is kept (a uniform load as large as the largest part).
    /// Not traced yet: beam reactions, columns, and a wall standing on the floor framing instead of on a wall (noted).
    /// </summary>
    public static class GravityLoadPath
    {
        /// <summary>How finely a wall is sampled along its length (ft).</summary>
        public const double Step = 0.25;
        private const double Tol = 0.01;

        public static string Key(string wallId, string levelId) => wallId + "|" + levelId;

        /// <summary>The project's loads on a wall's top on a story: entered by hand, else the load path's (or null).</summary>
        public static LineLoad Effective(Building b, LoadSection s, string wallId, string levelId, out WallTopLoad computed)
        {
            computed = null;
            var manual = s?.WallTop(wallId, levelId);
            if (b == null || s == null) return manual;
            Run(b, s).TryGetValue(Key(wallId, levelId), out computed);
            return manual ?? computed?.Load;
        }

        /// <summary>Global remarks (a wall standing on nothing under it, and so on).</summary>
        public sealed class Result : Dictionary<string, WallTopLoad>
        {
            public List<string> Notes { get; } = new List<string>();
        }

        public static Result Run(Building b, LoadSection s)
        {
            var result = new Result();
            if (b == null) return result;
            s = s ?? new LoadSection();
            var levels = b.LevelsBottomUp.Where(l => l.Elevation > 0).OrderByDescending(l => l.Elevation).ToList();
            var roof = b.RoofLevel();
            string Name(Wall w) => w.Id;

            foreach (var lv in levels)
            {
                var area = s.Level(lv.Id) ?? new AreaLoad();
                var plates = Plates(b, lv, roof);
                var bearing = b.Walls.Where(w => w.Direction != WallDirection.NotOrthogonal && b.LevelsAlong(w).Any(x => x.Id == lv.Id)).ToList();
                var beams = b.Beams.Where(m => m.LevelId == lv.Id).ToList();

                foreach (var w in bearing)
                {
                    var r = new WallTopLoad { WallId = w.Id, LevelId = lv.Id };
                    // the level's floor or roof, by tributary width
                    double tMin = double.MaxValue, tMax = 0, edgeMax = 0;
                    foreach (var p in plates)
                    {
                        var (lo, hi, edge) = Tributary(w, p, bearing, beams);
                        if (hi <= 0) continue;
                        tMin = Math.Min(tMin, lo); tMax = Math.Max(tMax, hi); edgeMax = Math.Max(edgeMax, edge - p.Overhang);
                    }
                    r.TributaryMax = tMax;
                    r.TributaryMin = tMax > 0 ? tMin : 0;
                    r.FromLevel = area.Times(tMax);
                    if (edgeMax > Tol)
                        r.Notes.Add($"No wall or beam found past it on one side: the framing to the edge of the {(lv == roof ? "roof" : "floor")} ({edgeMax:0.##} ft past any eave) is all on this wall — a cantilever, or a support not drawn.");

                    // what stands on it: the same wall going on up, or walls whose base is this level on its line
                    var up = b.LevelsAlong(w).Where(x => x.Elevation > lv.Elevation).OrderBy(x => x.Elevation).FirstOrDefault();
                    if (up != null)
                    {
                        if (result.TryGetValue(Key(w.Id, up.Id), out var cont))
                            r.FromAbove = cont.Load + new LineLoad { Dead = s.WallDead * (up.Elevation - lv.Elevation) };
                        r.Above.Add(Name(w) + " (its story above)");
                    }
                    else
                    {
                        foreach (var a in b.Walls.Where(a => a.BaseLevelId == lv.Id && a.Id != w.Id && Overlap(a, w) > Tol))
                        {
                            var aUp = b.LevelsAlong(a).FirstOrDefault();
                            if (aUp == null || !result.TryGetValue(Key(a.Id, aUp.Id), out var ar)) continue;
                            r.FromAbove = LineLoad.Max(r.FromAbove, ar.Load + new LineLoad { Dead = s.WallDead * (aUp.Elevation - lv.Elevation) });
                            r.Above.Add(Name(a));
                        }
                    }
                    r.Load = r.FromLevel + r.FromAbove;
                    result[Key(w.Id, lv.Id)] = r;
                }

                // walls standing on this level with nothing under them
                foreach (var a in b.Walls.Where(a => a.BaseLevelId == lv.Id))
                    if (!bearing.Any(w => w.Id != a.Id && Overlap(a, w) > Tol))
                        result.Notes.Add($"{a.Id} stands on the {lv.Name} framing, not on a wall: its load is not traced down (a beam or the floor carries it).");
            }
            return result;
        }

        /// <summary>A floor or roof on a level: where it is and which way its framing spans.</summary>
        private sealed class Plate
        {
            public Region Region;
            public SpanDirection Span;
            public double Overhang;
        }

        private static List<Plate> Plates(Building b, Level lv, Level roofLevel)
        {
            var list = new List<Plate>();
            bool isRoof = roofLevel != null && lv.Id == roofLevel.Id;
            double o = isRoof ? Math.Max(0, b.Roof?.Overhang ?? 0) : 0;
            Rect2 Grow(Rect2 r) => new Rect2(r.X0 - o, r.Y0 - o, r.X1 + o, r.Y1 + o);
            if (isRoof && b.Roof != null && b.Roof.IsSloped)
            {
                var plan = b.RoofPlan();
                if (plan.IsEmpty) return list;
                // rafters or trusses run across the ridge
                var span = b.EffectiveRidge() == RidgeAxis.WestEast ? SpanDirection.SouthNorth : SpanDirection.WestEast;
                list.Add(new Plate { Region = Region.Of(new[] { Grow(plan) }), Span = span, Overhang = o });
                return list;
            }
            var holes = b.OpeningsCutting(lv).SelectMany(x => x.Pieces).ToList();
            foreach (var d in b.Diaphragms.Where(d => d.LevelId == lv.Id && d.Pieces != null && d.Pieces.Count > 0))
                list.Add(new Plate { Region = Region.Of(d.Pieces.Select(Grow)).Minus(holes), Span = d.EffectiveSpan, Overhang = o });
            return list;
        }

        /// <summary>
        /// The tributary width of a plate's framing on a wall (ft): the least and the most along it, and the most taken to
        /// an edge with no support (0, 0 when the framing runs along the wall or the plate does not reach it).
        /// </summary>
        private static (double lo, double hi, double edge) Tributary(Wall w, Plate p, List<Wall> walls, List<Beam> beams)
        {
            // joists running W-E bear on S-N lines, and the other way round
            bool alongX = p.Span == SpanDirection.WestEast;
            if (alongX ? w.Direction != WallDirection.SouthNorth : w.Direction != WallDirection.WestEast) return (0, 0, 0);
            double c0 = alongX ? w.Start.X : w.Start.Y;                                   // the wall's line across the span
            double s0 = alongX ? Math.Min(w.Start.Y, w.End.Y) : Math.Min(w.Start.X, w.End.X);
            double s1 = alongX ? Math.Max(w.Start.Y, w.End.Y) : Math.Max(w.Start.X, w.End.X);

            // the other bearing lines across the span: (position, from, to)
            var lines = new List<(double c, double a, double b)>();
            foreach (var o in walls)
                if (o.Id != w.Id && o.Direction == w.Direction)
                    lines.Add(alongX ? (o.Start.X, Math.Min(o.Start.Y, o.End.Y), Math.Max(o.Start.Y, o.End.Y))
                                     : (o.Start.Y, Math.Min(o.Start.X, o.End.X), Math.Max(o.Start.X, o.End.X)));
            foreach (var m in beams)
            {
                bool sn = Math.Abs(m.Start.X - m.End.X) < Tol, we = Math.Abs(m.Start.Y - m.End.Y) < Tol;
                if (alongX && sn) lines.Add((m.Start.X, Math.Min(m.Start.Y, m.End.Y), Math.Max(m.Start.Y, m.End.Y)));
                if (!alongX && we) lines.Add((m.Start.Y, Math.Min(m.Start.X, m.End.X), Math.Max(m.Start.X, m.End.X)));
            }
            var cells = p.Region.Cells().ToList();
            int n = Math.Max(1, (int)Math.Ceiling((s1 - s0) / Step));
            double lo = double.MaxValue, hi = 0, edge = 0;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                double s = s0 + (i + 0.5) * (s1 - s0) / n;
                // the plate's extent across the span through this point of the wall
                var spans = cells.Where(c => alongX ? c.Y0 <= s && s <= c.Y1 : c.X0 <= s && s <= c.X1)
                                 .Select(c => alongX ? (c.X0, c.X1) : (c.Y0, c.Y1)).OrderBy(x => x.Item1).ToList();
                var merged = new List<(double a, double b)>();
                foreach (var (x0, x1) in spans)
                    if (merged.Count > 0 && x0 <= merged[merged.Count - 1].b + Tol) merged[merged.Count - 1] = (merged[merged.Count - 1].a, Math.Max(merged[merged.Count - 1].b, x1));
                    else merged.Add((x0, x1));
                var hit = merged.Where(m => c0 >= m.a - Tol && c0 <= m.b + Tol).ToList();
                if (hit.Count == 0) { lo = 0; continue; }
                double a = hit.Min(m => m.a), bb = hit.Max(m => m.b);
                any = true;
                var here = lines.Where(l => l.a - Tol <= s && s <= l.b + Tol && Math.Abs(l.c - c0) > Tol).Select(l => l.c).ToList();
                var below = here.Where(c => c < c0 && c >= a - Tol).DefaultIfEmpty(double.NaN).Max();
                var above = here.Where(c => c > c0 && c <= bb + Tol).DefaultIfEmpty(double.NaN).Min();
                double left = double.IsNaN(below) ? c0 - a : (c0 - below) / 2;
                double right = double.IsNaN(above) ? bb - c0 : (above - c0) / 2;
                if (double.IsNaN(below)) edge = Math.Max(edge, c0 - a);
                if (double.IsNaN(above)) edge = Math.Max(edge, bb - c0);
                double t = Math.Max(0, left) + Math.Max(0, right);
                lo = Math.Min(lo, t); hi = Math.Max(hi, t);
            }
            return any ? (lo, hi, edge) : (0, 0, 0);
        }

        /// <summary>How long two walls run on the same line together (ft); 0 when they are not on one line.</summary>
        private static double Overlap(Wall a, Wall b)
        {
            if (a.Direction == WallDirection.NotOrthogonal || a.Direction != b.Direction) return 0;
            bool we = a.Direction == WallDirection.WestEast;
            if (Math.Abs(we ? a.Start.Y - b.Start.Y : a.Start.X - b.Start.X) > Tol) return 0;
            double a0 = we ? Math.Min(a.Start.X, a.End.X) : Math.Min(a.Start.Y, a.End.Y), a1 = we ? Math.Max(a.Start.X, a.End.X) : Math.Max(a.Start.Y, a.End.Y);
            double b0 = we ? Math.Min(b.Start.X, b.End.X) : Math.Min(b.Start.Y, b.End.Y), b1 = we ? Math.Max(b.Start.X, b.End.X) : Math.Max(b.Start.Y, b.End.Y);
            return Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));
        }
    }
}

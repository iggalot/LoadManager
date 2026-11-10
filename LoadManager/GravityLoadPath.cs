using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Statics;

namespace LoadManager
{
    /// <summary>A concentrated load on a wall's top or along a beam: a beam's end, a column's base.</summary>
    public sealed class ConcentratedLoad
    {
        /// <summary>Where it is on the plan.</summary>
        public Point2 Where { get; set; }
        /// <summary>How far along the wall or beam from its start (ft).</summary>
        public double At { get; set; }
        public PointLoad Load { get; set; } = new PointLoad();
        /// <summary>What brings it ("BM1", "C2"), and the wall it came down through when it came from above ("BM1 via W7").</summary>
        public string From { get; set; }
        /// <summary>The member it comes straight from: the wall whose post it is, the beam whose end, the column whose base (the other end of its pair).</summary>
        public string Source { get; set; }
        /// <summary>A post at the bottom of a wall: the member it lands on (null: nothing drawn, or the foundation).</summary>
        public string Onto { get; set; }

        public override string ToString() => $"{From} at {At:0.##} ft: {Load}";
    }

    /// <summary>What lands on a wall's top on one story, by the gravity load path.</summary>
    public sealed class WallTopLoad
    {
        public string WallId { get; set; }
        /// <summary>The level on top of the story (the one the wall carries).</summary>
        public string LevelId { get; set; }
        /// <summary>The most along the wall (plf), or the load entered by hand (<see cref="Entered"/>).</summary>
        public LineLoad Load { get; set; } = new LineLoad();
        /// <summary>Its share of the level's floor or roof (plf, the most along the wall).</summary>
        public LineLoad FromLevel { get; set; } = new LineLoad();
        /// <summary>What the wall or walls standing on it bring down, with their own weight (plf, the most along it).</summary>
        public LineLoad FromAbove { get; set; } = new LineLoad();
        /// <summary>How <see cref="FromLevel"/>, <see cref="FromAbove"/> and <see cref="Load"/> vary along the wall (from its start), for the foundation.</summary>
        public LoadProfile FromLevelProfile { get; set; } = new LoadProfile();
        public LoadProfile FromAboveProfile { get; set; } = new LoadProfile();
        public LoadProfile Profile { get; set; } = new LoadProfile();
        /// <summary>What the load path finds: <see cref="FromLevel"/> + <see cref="FromAbove"/>.</summary>
        public LineLoad Computed { get; set; } = new LineLoad();
        /// <summary>True when <see cref="Load"/> is the one entered by hand (it replaces <see cref="Computed"/>, and goes on down from there).</summary>
        public bool Entered { get; set; }
        /// <summary>The tributary width of the level's framing (ft), the least and the most along the wall.</summary>
        public double TributaryMin { get; set; }
        public double TributaryMax { get; set; }
        /// <summary>The longest span of the framing landing on it (ft): to the next wall or beam across the span (to the edge with none on either side).</summary>
        public double FramingSpan { get; set; }
        /// <summary>The walls standing on it ("W7").</summary>
        public List<string> Above { get; } = new List<string>();
        /// <summary>Each wall standing on it (its id): what it brings down onto this wall, along this wall (from its start), with its weight.</summary>
        public Dictionary<string, LoadProfile> AboveParts { get; } = new Dictionary<string, LoadProfile>();
        /// <summary>The load zones its tributary strip reaches ("Deck").</summary>
        public List<string> Zones { get; } = new List<string>();
        /// <summary>Beam ends and columns landing on it, here or on a wall above that stands on it (each needs a post).</summary>
        public List<ConcentratedLoad> Points { get; } = new List<ConcentratedLoad>();
        /// <summary>Those of <see cref="Points"/> landing at this level (not brought down through the wall's own story above).</summary>
        public List<ConcentratedLoad> Here { get; } = new List<ConcentratedLoad>();
        public List<string> Notes { get; } = new List<string>();

        /// <summary>One line: "tributary 6 to 10 ft; plus the wall above W7; 2 concentrated loads".</summary>
        public string Describe()
        {
            string t = TributaryMax <= 0 ? "no floor or roof bears on it" :
                Math.Abs(TributaryMax - TributaryMin) < 0.005 ? $"tributary {TributaryMax:0.##} ft" : $"tributary {TributaryMin:0.##} to {TributaryMax:0.##} ft";
            return t + (Above.Count > 0 ? "; plus the wall" + (Above.Count > 1 ? "s" : "") + " above " + string.Join(", ", Above) : "")
                     + (Zones.Count > 0 ? "; load zone" + (Zones.Count > 1 ? "s " : " ") + string.Join(", ", Zones) : "")
                     + (Points.Count > 0 ? $"; {Points.Count} concentrated load{(Points.Count > 1 ? "s" : "")}" : "");
        }
    }

    /// <summary>A part-length line load on a beam: a wall standing on it.</summary>
    public sealed class PartialLoad
    {
        public string From { get; set; }
        /// <summary>From and to, along the beam from its start (ft).</summary>
        public double A { get; set; }
        public double B { get; set; }
        public LineLoad Load { get; set; } = new LineLoad();
    }

    /// <summary>A beam's loads and its end reactions (simple span), by the gravity load path.</summary>
    public sealed class BeamLoad
    {
        public string BeamId { get; set; }
        public string LevelId { get; set; }
        public double Span { get; set; }
        /// <summary>Its share of the level's floor or roof (plf, the most along it: for the tables).</summary>
        public LineLoad FromLevel { get; set; } = new LineLoad();
        /// <summary>
        /// How that share varies along it (from its start): each step a load of its own in the reactions (owner: every load
        /// on a beam taken on its own, never the largest over the span). Empty: none found (then <see cref="FromLevel"/> over the span).
        /// </summary>
        public LoadProfile FromLevelProfile { get; set; } = new LoadProfile();
        public double TributaryMin { get; set; }
        public double TributaryMax { get; set; }
        /// <summary>The longest span of the framing landing on it (ft).</summary>
        public double FramingSpan { get; set; }
        /// <summary>The load zones its tributary strip reaches.</summary>
        public List<string> Zones { get; } = new List<string>();
        /// <summary>Walls standing on it.</summary>
        public List<PartialLoad> Partials { get; } = new List<PartialLoad>();
        /// <summary>Beams framing into it and columns standing on it.</summary>
        public List<ConcentratedLoad> Points { get; } = new List<ConcentratedLoad>();
        /// <summary>
        /// What holds it up, in order along it: every wall it crosses or rests on, every column under it, and at an end with
        /// neither, a beam (owner 2026-11-10: supports anywhere, an end past the last one a cantilever). Each with its reaction.
        /// </summary>
        public List<BeamSupport> Supports { get; } = new List<BeamSupport>();
        /// <summary>False: fewer than two supports, it cannot stand (a mechanism); its load is not traced down.</summary>
        public bool Stands { get; set; } = true;
        /// <summary>The support at its start, if one is there (else null).</summary>
        public BeamSupport AtStart => Supports.FirstOrDefault(x => x.Name == "start");
        public BeamSupport AtEnd => Supports.FirstOrDefault(x => x.Name == "end");
        /// <summary>The reaction at its start, nothing when its start is a cantilever's tip.</summary>
        public PointLoad StartReaction => AtStart?.Reaction ?? new PointLoad();
        public PointLoad EndReaction => AtEnd?.Reaction ?? new PointLoad();
        /// <summary>What its start or end bears on (a wall, a column, a beam), or null when nothing is under it.</summary>
        public string StartOn => AtStart?.On;
        public string EndOn => AtEnd?.On;
        /// <summary>The support nearest a point (within <paramref name="within"/> ft), or null.</summary>
        public BeamSupport SupportNear(Point2 p, double within = 1.0) =>
            Supports.Where(x => Math.Sqrt((x.Where.X - p.X) * (x.Where.X - p.X) + (x.Where.Y - p.Y) * (x.Where.Y - p.Y)) <= within)
                    .OrderBy(x => (x.Where.X - p.X) * (x.Where.X - p.X) + (x.Where.Y - p.Y) * (x.Where.Y - p.Y)).FirstOrDefault();
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>One support of a beam and its reaction.</summary>
    public sealed class BeamSupport
    {
        /// <summary>"start", "end", or "at 12.5 ft" (from its start): as the shear wall app names its points there.</summary>
        public string Name { get; set; }
        /// <summary>ft from the beam's start.</summary>
        public double At { get; set; }
        public Point2 Where { get; set; }
        /// <summary>The wall, column or beam under it.</summary>
        public string On { get; set; }
        /// <summary>+ up on the beam: what it carries down. − : the beam pulls it up (it must be held down).</summary>
        public PointLoad Reaction { get; set; } = new PointLoad();
    }

    /// <summary>What a wall on the ground or a column puts on the foundation (service level).</summary>
    public sealed class FoundationLoad
    {
        public string Id { get; set; }
        public bool IsWall { get; set; }
        /// <summary>A wall's start, a column's place.</summary>
        public Point2 Where { get; set; }
        public double Length { get; set; }
        /// <summary>A wall's line load at its base (plf, the most along it): its top load and its bottom story's own weight.</summary>
        public LineLoad Line { get; set; } = new LineLoad();
        /// <summary>How a wall's line load at its base varies along it (from its start): <see cref="Line"/> is its largest.</summary>
        public LoadProfile Profile { get; set; } = new LoadProfile();
        /// <summary>The posts in a wall, each down to the foundation.</summary>
        public List<ConcentratedLoad> Points { get; set; } = new List<ConcentratedLoad>();
        /// <summary>A column's load (lb).</summary>
        public PointLoad Point { get; set; } = new PointLoad();
    }

    /// <summary>A beam end or a column landing on a column at a level.</summary>
    public sealed class ColumnLanding
    {
        public string LevelId { get; set; }
        public string From { get; set; }
        /// <summary>The member it comes straight from (a beam, a column, a wall).</summary>
        public string Source { get; set; }
        public PointLoad Load { get; set; } = new PointLoad();
    }

    /// <summary>What a column carries down to its base.</summary>
    public sealed class ColumnLoad
    {
        public string ColumnId { get; set; }
        public PointLoad Load { get; set; } = new PointLoad();
        /// <summary>What lands on it ("BM1 at Level 2").</summary>
        public List<string> From { get; } = new List<string>();
        /// <summary>The same, each with its load and the level it lands at.</summary>
        public List<ColumnLanding> Landings { get; } = new List<ColumnLanding>();
        /// <summary>What it stands on: a wall, a beam, a column, or "the foundation"; null when nothing is drawn under it.</summary>
        public string BaseOn { get; set; }
    }

    /// <summary>
    /// The gravity load path, a first pass: each level's area loads (<see cref="LoadSection.Levels"/>) go to the walls and
    /// beams under it by tributary width — the framing spans one way (the diaphragm's <see cref="Diaphragm.EffectiveSpan"/>;
    /// a sloped roof across its ridge) and each bearing line takes half the distance to the next one on each side, or all
    /// of it to the edge where there is none (an eave, a cantilever, or a support not drawn). Walls standing on a wall
    /// below bring their load and their own weight (<see cref="LoadSection.WallDead"/>) down to it; a wall standing on a
    /// beam loads the beam over its length. Beams are simple spans: their end reactions land on the wall, column or beam
    /// under each end, and columns carry what lands on them down to their base. Beam ends and columns on a wall are
    /// concentrated loads on it, carried on down the walls under it. The levels are taken from the top down. The most
    /// along each wall or beam is kept (a uniform load as large as the largest part).
    /// </summary>
    public static class GravityLoadPath
    {
        /// <summary>How finely a wall is sampled along its length (ft).</summary>
        public const double Step = 0.25;
        /// <summary>How near a beam end or a column must be to what carries it (ft), as the building model finds column bases.</summary>
        public const double Bearing = 0.25;
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

        /// <summary>The walls' top loads (by <see cref="Key"/>), the beams', the columns', and global remarks.</summary>
        public sealed class Result : Dictionary<string, WallTopLoad>
        {
            public Dictionary<string, BeamLoad> Beams { get; } = new Dictionary<string, BeamLoad>();
            public Dictionary<string, ColumnLoad> Columns { get; } = new Dictionary<string, ColumnLoad>();
            /// <summary>What reaches the foundation: the walls on the ground and the columns standing on it.</summary>
            public List<FoundationLoad> Foundation { get; } = new List<FoundationLoad>();
            public List<string> Notes { get; } = new List<string>();
        }

        /// <summary>A bearing line on a level: a wall under it or a beam on it.</summary>
        private sealed class Line
        {
            public string Id;
            public Point2 Start, End;
            public WallDirection Dir;
            public bool IsBeam;
            public double Length => Dist(Start, End);
        }

        /// <summary>A concentrated load going down to what is under it.</summary>
        private sealed class Landing
        {
            public string Target;
            public Point2 Where;
            public PointLoad Load;
            public string From;
            public string Source;
        }

        /// <summary>A beam's direction: square to the plan when its ends are within <see cref="Bearing"/> of one line (drawn by hand).</summary>
        private static WallDirection DirOf(Point2 a, Point2 b) =>
            Dist(a, b) <= Bearing ? WallDirection.NotOrthogonal
            : Math.Abs(a.Y - b.Y) <= Bearing && Math.Abs(a.X - b.X) > Bearing ? WallDirection.WestEast
            : Math.Abs(a.X - b.X) <= Bearing && Math.Abs(a.Y - b.Y) > Bearing ? WallDirection.SouthNorth
            : WallDirection.NotOrthogonal;

        public static Result Run(Building b, LoadSection s) => Walk(b, s, null);

        /// <summary>
        /// The uplift pass: the same load path with the dead loads only (the floors' and roof's, the zones', the walls' own weight,
        /// and those entered by hand) and the wind on the roof (<paramref name="wind"/>, taken across each tributary strip of the
        /// roof and its eaves) as <see cref="GravityLoad.Wind"/>, carried down alike. Along each wall or beam the place with the
        /// most net uplift (0.6D + 0.6W, <see cref="GravityLoad.Net"/>) is kept, its dead and wind together.
        /// </summary>
        public static Result Uplift(Building b, LoadSection s, RoofWindCase wind)
        {
            s = s ?? new LoadSection();
            LineLoad DeadOnly(LineLoad x) => x == null ? null : new LineLoad { Dead = x.Dead };
            var dead = new LoadSection
            {
                WallDead = s.WallDead,
                Levels = s.Levels?.ToDictionary(k => k.Key, k => new AreaLoad { Dead = k.Value?.Dead ?? 0 }),
                Zones = s.Zones?.Select(z => new LoadZone { LevelId = z.LevelId, Name = z.Name, X0 = z.X0, Y0 = z.Y0, X1 = z.X1, Y1 = z.Y1, Load = new AreaLoad { Dead = z.Load?.Dead ?? 0 } }).ToList(),
                WallTops = s.WallTops?.ToDictionary(k => k.Key, k => k.Value?.ToDictionary(v => v.Key, v => DeadOnly(v.Value))),
            };
            return Walk(b, dead, wind ?? new RoofWindCase { Name = "no wind", PressureAt = (x, y) => 0 });
        }

        private static Result Walk(Building b, LoadSection s, RoofWindCase wind)
        {
            var result = new Result();
            if (b == null) return result;
            s = s ?? new LoadSection();
            // the most along a line: each kind's largest, or in the uplift pass the place with the most net uplift
            LineLoad Most(LineLoad x, LineLoad y) => wind == null ? LineLoad.Max(x, y) : LineLoad.MoreUplift(x, y);
            var levels = b.LevelsBottomUp.Where(l => l.Elevation > 0).OrderByDescending(l => l.Elevation).ToList();
            var roof = b.RoofLevel();
            ColumnLoad Col(string id) => result.Columns.TryGetValue(id, out var c) ? c : result.Columns[id] = new ColumnLoad { ColumnId = id };
            double Elev(string levelId) => b.Level(levelId)?.Elevation ?? 0;
            foreach (var c in b.Columns) Col(c.Id);

            foreach (var lv in levels)
            {
                var area = s.Level(lv.Id) ?? new AreaLoad();
                var zones = s.ZonesOn(lv.Id).ToList();
                var plates = Plates(b, lv, roof, wind);
                var bearing = b.Walls.Where(w => w.Direction != WallDirection.NotOrthogonal && b.LevelsAlong(w).Any(x => x.Id == lv.Id)).ToList();
                var beams = b.Beams.Where(m => m.LevelId == lv.Id).ToList();
                // the columns under this level: base below it, top at or above it
                var columns = b.Columns.Where(c => Elev(c.BaseLevelId) < lv.Elevation - Tol && Elev(c.TopLevelId) >= lv.Elevation - Tol).ToList();
                var lines = bearing.Select(w => new Line { Id = w.Id, Start = w.Start, End = w.End, Dir = w.Direction })
                                   .Concat(beams.Select(m => new Line { Id = m.Id, Start = m.Start, End = m.End, Dir = DirOf(m.Start, m.End), IsBeam = true })).ToList();
                var landings = new List<Landing>();

                // what carries a point at this level: a wall under it, a column under it, else a beam it lands on partway
                // along, else a beam whose end is there (the load goes straight to that beam's support: a header hung at a
                // trimmer's end)
                string SupportOf(Point2 at, string exceptBeam, string exceptColumn)
                {
                    var w = bearing.FirstOrDefault(x => DistanceToSegment(at, x.Start, x.End) <= Bearing);
                    if (w != null) return w.Id;
                    var c = columns.FirstOrDefault(x => x.Id != exceptColumn && Dist(x.At, at) <= Bearing);
                    if (c != null) return c.Id;
                    return beams.FirstOrDefault(x => x.Id != exceptBeam && DistanceToSegment(at, x.Start, x.End) <= Bearing && Dist(at, x.Start) > Bearing && Dist(at, x.End) > Bearing)?.Id
                        ?? beams.FirstOrDefault(x => x.Id != exceptBeam && (Dist(at, x.Start) <= Bearing || Dist(at, x.End) <= Bearing))?.Id;
                }

                // (a) the columns standing on this level put what they carry on what is under them
                foreach (var c in b.Columns.Where(c => c.BaseLevelId == lv.Id))
                {
                    var cl = Col(c.Id);
                    cl.BaseOn = SupportOf(c.At, null, c.Id);
                    if (cl.BaseOn == null) result.Notes.Add($"{c.Id} stands on nothing drawn at {lv.Name}: what it carries is not traced down.");
                    else if (!cl.Load.IsEmpty) landings.Add(new Landing { Target = cl.BaseOn, Where = c.At, Load = cl.Load, From = c.Id, Source = c.Id });
                }

                // (a2) the concentrated loads in the walls standing on this level go down to one support each: the nearest wall or
                // beam under that wall on its line, else whatever is under the point (a cross wall, a column, a beam)
                foreach (var a in b.Walls.Where(a => a.BaseLevelId == lv.Id))
                {
                    var aUp = b.LevelsAlong(a).FirstOrDefault();
                    if (aUp == null || !result.TryGetValue(Key(a.Id, aUp.Id), out var ar)) continue;
                    var onLine = lines.Where(l => { var o = OverlapAlong(l, a.Start, a.End, a.Direction); return o.b - o.a > Tol; }).ToList();
                    foreach (var p in ar.Points)
                    {
                        var to = onLine.Where(l => DistanceToSegment(p.Where, l.Start, l.End) <= Bearing)
                                       .OrderBy(l => DistanceToSegment(p.Where, l.Start, l.End)).ThenBy(l => l.IsBeam).ThenBy(l => l.Id).FirstOrDefault()?.Id
                                 ?? SupportOf(p.Where, null, null);
                        p.Onto = to;
                        if (to == null) result.Notes.Add($"{p.From} on {a.Id} at ({p.Where.X:0.##}, {p.Where.Y:0.##}): nothing drawn under it at {lv.Name}, not traced down.");
                        else landings.Add(new Landing { Target = to, Where = p.Where, Load = p.Load, From = Via(p.From, a.Id), Source = a.Id });
                    }
                }

                // (b) the beams. What holds each up (SupportsOf: walls and columns anywhere along it, a beam at an end) decides
                // the order: a beam framing into a girder is solved before the girder, and its reaction is a point load on it.
                // Beams that bear on each other round a loop (reciprocal framing) are solved together (SolveBeams).
                var supports = beams.ToDictionary(m => m.Id, m => SupportsOf(m, bearing, columns, BeamAt));
                var ends = supports.ToDictionary(k => k.Key, k => k.Value.Select(x => x.On).Where(x => x != null).ToList());
                string BeamAt(Point2 at, string exceptBeam) =>
                    beams.FirstOrDefault(x => x.Id != exceptBeam && DistanceToSegment(at, x.Start, x.End) <= Bearing && Dist(at, x.Start) > Bearing && Dist(at, x.End) > Bearing)?.Id
                    ?? beams.FirstOrDefault(x => x.Id != exceptBeam && (Dist(at, x.Start) <= Bearing || Dist(at, x.End) <= Bearing))?.Id;
                var order = new List<Beam>();
                var left = beams.ToList();
                while (left.Count > 0)
                {
                    var ready = left.Where(m => !left.Any(o => o.Id != m.Id && ends[o.Id].Contains(m.Id))).ToList();
                    if (ready.Count == 0) ready = left.ToList(); // a loop: solved together below, in any order
                    order.AddRange(ready);
                    left.RemoveAll(ready.Contains);
                }
                // a beam carries another when that one's ends lead to it, directly or through others: it takes none of the
                // floor off what it carries (a girder off its beams, a trimmer off its headers)
                bool Carries(Line by, Line w)
                {
                    if (!by.IsBeam || !w.IsBeam || by.Id == w.Id || !ends.ContainsKey(w.Id)) return false;
                    var seen = new HashSet<string>();
                    var next = new Stack<string>(new[] { w.Id });
                    while (next.Count > 0)
                        if (ends.TryGetValue(next.Pop(), out var e))
                            foreach (var on in e)
                            {
                                if (on == by.Id) return true;
                                if (seen.Add(on)) next.Push(on);
                            }
                    return false;
                }
                // every bearing line's share of the floor or roof, beams and walls (used in (b) and (c))
                var shares = Tributaries(lines.Where(l => l.Dir != WallDirection.NotOrthogonal).ToList(), plates, area, zones, Most, Carries);
                var fixedPoints = new Dictionary<string, List<ConcentratedLoad>>();
                foreach (var m in order)
                {
                    var me = lines.First(l => l.IsBeam && l.Id == m.Id);
                    var r = new BeamLoad { BeamId = m.Id, LevelId = lv.Id, Span = me.Length };
                    result.Beams[m.Id] = r;
                    if (me.Dir == WallDirection.NotOrthogonal) r.Notes.Add("Not square to the plan: the floor or roof on it is not found (only what stands or lands on it).");
                    else
                    {
                        var (lo, hi, edge, load, met, span, beamProfile) = shares[me];
                        r.TributaryMax = hi; r.TributaryMin = lo; r.FramingSpan = span;
                        r.FromLevel = load;
                        r.FromLevelProfile = beamProfile;
                        r.Zones.AddRange(met);
                        if (edge > Bearing) r.Notes.Add($"No wall or beam found past it on one side: the framing to the edge ({edge:0.##} ft) is all on this beam.");
                    }
                    // walls standing on it (their concentrated loads came as landings, above)
                    foreach (var a in b.Walls.Where(a => a.BaseLevelId == lv.Id))
                    {
                        var (o0, o1) = OverlapAlong(me, a.Start, a.End, a.Direction);
                        if (o1 - o0 <= Tol) continue;
                        var aUp = b.LevelsAlong(a).FirstOrDefault();
                        if (aUp == null || !result.TryGetValue(Key(a.Id, aUp.Id), out var ar)) continue;
                        // its profile along the beam where they overlap, with its own weight there (as onto a wall under
                        // it): one part-length load a step, so the beam takes what the wall's base gives, place by place
                        double la = Dist(a.Start, a.End);
                        var wa = a;
                        var part = ar.Profile.Moved(x => Along(me, la < 1e-9 ? wa.Start : new Point2(wa.Start.X + (wa.End.X - wa.Start.X) * x / la, wa.Start.Y + (wa.End.Y - wa.Start.Y) * x / la)), o0, o1)
                                     .Plus(LoadProfile.Uniform(o0, o1, new LineLoad { Dead = s.WallDead * (aUp.Elevation - lv.Elevation) }));
                        foreach (var st in part.Steps)
                            r.Partials.Add(new PartialLoad { From = a.Id, A = st.A, B = st.B, Load = st.Load });
                    }
                    // the columns and the walls' posts landing on it (the beams' ends come in the solve below)
                    fixedPoints[m.Id] = landings.Where(g => g.Target == m.Id)
                        .Select(g => new ConcentratedLoad { Where = g.Where, At = Along(me, g.Where), Load = g.Load, From = g.From, Source = g.Source }).ToList();
                    r.Supports.AddRange(supports[m.Id]);
                }
                SolveBeams(order, lines, result.Beams, fixedPoints, lv, result.Notes);
                foreach (var m in order)
                {
                    var r = result.Beams[m.Id];
                    if (!r.Stands)
                    {
                        r.Notes.Add(r.Supports.Count == 0
                            ? "Nothing drawn under it (no wall, column or beam): it cannot stand, and its load is not traced down."
                            : $"Only one support ({r.Supports[0].On ?? "nothing drawn"} at its {r.Supports[0].Name}): it cannot stand (a cantilever needs a second support behind it), and its load is not traced down. Draw what holds it up.");
                        continue;
                    }
                    double first = r.Supports.Min(x => x.At), last = r.Supports.Max(x => x.At);
                    if (first > Bearing) r.Notes.Add($"Its start runs {first:0.##} ft past its first support: a cantilever (an overhang).");
                    if (r.Span - last > Bearing) r.Notes.Add($"Its end runs {r.Span - last:0.##} ft past its last support: a cantilever (an overhang).");
                    foreach (var x in r.Supports)
                    {
                        if (x.On == null) { r.Notes.Add($"Nothing drawn under its {x.Name} ({x.Where.X:0.##}, {x.Where.Y:0.##}): its reaction is not traced down."); continue; }
                        var up = Lifts(x.Reaction);
                        if (up != null) r.Notes.Add($"It pulls up on {x.On} at its {x.Name} ({up}): hold it down there.");
                        if (!IsNil(x.Reaction)) landings.Add(new Landing { Target = x.On, Where = x.Where, Load = x.Reaction, From = m.Id, Source = m.Id });
                    }
                }

                // (c) the walls
                foreach (var w in bearing)
                {
                    var me = lines.First(l => !l.IsBeam && l.Id == w.Id);
                    var r = new WallTopLoad { WallId = w.Id, LevelId = lv.Id };
                    var (lo, hi, edge, load, met, span, levelProfile) = shares[me];
                    double len = me.Length;
                    r.FromLevelProfile = levelProfile;
                    var aboveProfile = new LoadProfile();
                    r.TributaryMax = hi;
                    r.FramingSpan = span;
                    r.TributaryMin = lo;
                    r.FromLevel = load;
                    r.Zones.AddRange(met);
                    if (edge > Bearing)
                        r.Notes.Add($"No wall or beam found past it on one side: the framing to the edge of the {(lv == roof ? "roof" : "floor")} ({edge:0.##} ft past any eave) is all on this wall — a cantilever, or a support not drawn.");

                    // what stands on it: the same wall going on up, or walls whose base is this level on its line
                    var up = b.LevelsAlong(w).Where(x => x.Elevation > lv.Elevation).OrderBy(x => x.Elevation).FirstOrDefault();
                    if (up != null)
                    {
                        if (result.TryGetValue(Key(w.Id, up.Id), out var cont))
                        {
                            r.FromAbove = cont.Load + new LineLoad { Dead = s.WallDead * (up.Elevation - lv.Elevation) };
                            aboveProfile = cont.Profile.Plus(LoadProfile.Uniform(0, len, new LineLoad { Dead = s.WallDead * (up.Elevation - lv.Elevation) }));
                            r.Points.AddRange(cont.Points.Select(p => new ConcentratedLoad { Where = p.Where, Load = p.Load, From = p.From }));
                        }
                        r.Above.Add(w.Id + " (its story above)");
                    }
                    else
                    {
                        foreach (var a in b.Walls.Where(a => a.BaseLevelId == lv.Id && a.Id != w.Id && Overlap(a, w) > Tol))
                        {
                            var aUp = b.LevelsAlong(a).FirstOrDefault();
                            if (aUp == null || !result.TryGetValue(Key(a.Id, aUp.Id), out var ar)) continue;
                            r.FromAbove = Most(r.Above.Count == 0 ? null : r.FromAbove, ar.Load + new LineLoad { Dead = s.WallDead * (aUp.Elevation - lv.Elevation) });
                            // its profile, onto this wall where they overlap, with its own weight there
                            var (o0, o1) = OverlapAlong(me, a.Start, a.End, a.Direction);
                            double la = Dist(a.Start, a.End);
                            var wa = a;
                            var part = ar.Profile.Moved(x => Along(me, la < 1e-9 ? wa.Start : new Point2(wa.Start.X + (wa.End.X - wa.Start.X) * x / la, wa.Start.Y + (wa.End.Y - wa.Start.Y) * x / la)), o0, o1)
                                         .Plus(LoadProfile.Uniform(o0, o1, new LineLoad { Dead = s.WallDead * (aUp.Elevation - lv.Elevation) }));
                            aboveProfile = aboveProfile.Combine(part, Most);
                            r.AboveParts[a.Id] = part;
                            r.Above.Add(a.Id);
                        }
                    }
                    foreach (var g in landings.Where(g => g.Target == w.Id))
                    {
                        var here = new ConcentratedLoad { Where = g.Where, Load = g.Load, From = g.From, Source = g.Source };
                        r.Points.Add(here);
                        r.Here.Add(here);
                    }
                    foreach (var p in r.Points) p.At = Along(me, p.Where);
                    r.Computed = r.FromLevel + r.FromAbove;
                    // a load entered by hand replaces the computed one, here and in everything under the wall
                    var entered = s.WallTop(w.Id, lv.Id);
                    r.Entered = entered != null;
                    r.Load = entered != null ? entered + new LineLoad { Wind = r.Computed.Wind } : r.Computed;
                    r.FromAboveProfile = aboveProfile;
                    r.Profile = entered != null ? LoadProfile.Uniform(0, len, r.Load) : levelProfile.Plus(aboveProfile);
                    result[Key(w.Id, lv.Id)] = r;
                }

                // (d) beam ends and columns landing on a column under this level
                foreach (var g in landings.Where(g => columns.Any(c => c.Id == g.Target)))
                {
                    var cl = Col(g.Target);
                    cl.Load = cl.Load + g.Load;
                    cl.From.Add($"{g.From} at {lv.Name}");
                    cl.Landings.Add(new ColumnLanding { LevelId = lv.Id, From = g.From, Load = g.Load, Source = g.Source });
                }

                // walls standing on this level with no wall or beam under them
                foreach (var a in b.Walls.Where(a => a.BaseLevelId == lv.Id))
                    if (!bearing.Any(w => w.Id != a.Id && Overlap(a, w) > Tol) &&
                        !lines.Any(l => l.IsBeam && OverlapAlong(l, a.Start, a.End, a.Direction).b - OverlapAlong(l, a.Start, a.End, a.Direction).a > Tol))
                        result.Notes.Add($"{a.Id} stands on the {lv.Name} framing, not on a wall or beam: its load is not traced down (the floor carries it).");
            }
            foreach (var c in b.Columns.Where(c => Elev(c.BaseLevelId) <= Tol)) Col(c.Id).BaseOn = "the foundation";

            // what reaches the foundation: each wall standing on the ground (its top load and its bottom story's weight, and
            // the posts in it), each column standing on it
            foreach (var w in b.Walls.Where(w => Elev(w.BaseLevelId) <= Tol && w.Direction != WallDirection.NotOrthogonal))
            {
                var first = b.LevelsAlong(w).FirstOrDefault();
                if (first == null) continue;
                result.TryGetValue(Key(w.Id, first.Id), out var top);
                result.Foundation.Add(new FoundationLoad
                {
                    Id = w.Id, IsWall = true, Length = Dist(w.Start, w.End), Where = w.Start,
                    Line = (top?.Load ?? new LineLoad()) + new LineLoad { Dead = s.WallDead * first.Elevation },
                    Points = top?.Points.ToList() ?? new List<ConcentratedLoad>(),
                    Profile = (top?.Profile ?? new LoadProfile()).Plus(LoadProfile.Uniform(0, Dist(w.Start, w.End), new LineLoad { Dead = s.WallDead * first.Elevation })),
                });
            }
            foreach (var c in b.Columns.Where(c => Elev(c.BaseLevelId) <= Tol))
                result.Foundation.Add(new FoundationLoad { Id = c.Id, Where = c.At, Point = Col(c.Id).Load });
            return result;
        }

        /// <summary>"BM1 via W7": a load brought down through a wall (the first wall it came through is kept).</summary>
        private static string Via(string from, string wall) => from.Contains(" via ") ? from : $"{from} via {wall}";
        /// <summary>
        /// What holds a beam up, in order along it: every bearing wall under it (crossing it, ending under it, or under one of
        /// its ends; a wall along it, the ends of where they overlap), every column under it, and at an end with neither, a
        /// beam (<paramref name="beamAt"/>: one it lands on partway along, else one whose end is there). Two within
        /// <see cref="Bearing"/> of each other are one (a wall before a column); one within it of an end is that end.
        /// </summary>
        private static List<BeamSupport> SupportsOf(Beam m, List<Wall> bearing, List<Column> columns, Func<Point2, string, string> beamAt)
        {
            double L = Dist(m.Start, m.End);
            Point2 At(double t) => L < 1e-9 ? m.Start : new Point2(m.Start.X + (m.End.X - m.Start.X) * t / L, m.Start.Y + (m.End.Y - m.Start.Y) * t / L);
            double AlongM(Point2 p) => L < 1e-9 ? 0 : Math.Max(0, Math.Min(L, ((p.X - m.Start.X) * (m.End.X - m.Start.X) + (p.Y - m.Start.Y) * (m.End.Y - m.Start.Y)) / L));
            double Off(Point2 p) => DistanceToSegment(p, m.Start, m.End);
            var found = new List<(double at, string on, int rank)>();
            foreach (var w in bearing)
            {
                if (DistanceToSegment(m.Start, w.Start, w.End) <= Bearing) found.Add((0, w.Id, 0));
                if (DistanceToSegment(m.End, w.Start, w.End) <= Bearing) found.Add((L, w.Id, 0));
                foreach (var p in new[] { w.Start, w.End }) if (Off(p) <= Bearing) found.Add((AlongM(p), w.Id, 0));
                if (Crossing(m.Start, m.End, w.Start, w.End, out var t)) found.Add((t * L, w.Id, 0));
            }
            foreach (var c in columns) if (Off(c.At) <= Bearing) found.Add((AlongM(c.At), c.Id, 1));
            // one each place: the ends first, then along it
            var list = new List<BeamSupport>();
            var taken = new List<(double at, string on, int rank)>();
            foreach (var f in found.Select(f => (at: f.at <= Bearing ? 0 : f.at >= L - Bearing ? L : f.at, f.on, f.rank)).OrderBy(f => f.at).ThenBy(f => f.rank))
            {
                if (taken.Any(x => Math.Abs(x.at - f.at) <= Bearing)) continue;
                taken.Add(f);
            }
            foreach (var (at, on, _) in taken) list.Add(new BeamSupport { At = at, On = on });
            // an end with no wall or column under it: a beam there
            if (!list.Any(x => x.At <= Bearing)) { var on = beamAt(m.Start, m.Id); if (on != null) list.Insert(0, new BeamSupport { At = 0, On = on }); }
            if (!list.Any(x => x.At >= L - Bearing)) { var on = beamAt(m.End, m.Id); if (on != null) list.Add(new BeamSupport { At = L, On = on }); }
            foreach (var x in list)
            {
                x.Name = x.At <= Bearing ? "start" : x.At >= L - Bearing ? "end" : $"at {x.At:0.##} ft";
                x.Where = x.Name == "start" ? m.Start : x.Name == "end" ? m.End : At(x.At);
            }
            return list;
        }

        /// <summary>Where two segments cross, as a share of the first (0 to 1); false when they do not, or run alongside.</summary>
        private static bool Crossing(Point2 a, Point2 b, Point2 c, Point2 d, out double t)
        {
            t = 0;
            double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12) return false;
            double qx = c.X - a.X, qy = c.Y - a.Y;
            t = (qx * sy - qy * sx) / den;
            double u = (qx * ry - qy * rx) / den;
            return t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9;
        }

        private static bool IsNil(PointLoad p) =>
            Math.Abs(p.Dead) < 1e-9 && Math.Abs(p.Live) < 1e-9 && Math.Abs(p.RoofLive) < 1e-9 && Math.Abs(p.Snow) < 1e-9 && Math.Abs(p.Wind) < 1e-9;

        /// <summary>The gravity loads pulling a support up (− reactions), said; null when none does.</summary>
        private static string Lifts(PointLoad p)
        {
            var up = new List<string>();
            if (p.Dead < -0.5) up.Add($"D {-p.Dead:0}");
            if (p.Live < -0.5) up.Add($"L {-p.Live:0}");
            if (p.RoofLive < -0.5) up.Add($"Lr {-p.RoofLive:0}");
            if (p.Snow < -0.5) up.Add($"S {-p.Snow:0}");
            return up.Count == 0 ? null : string.Join(", ", up) + " lb up";
        }

        /// <summary>
        /// A beam's reactions at its supports, by its own free body (<see cref="BeamStatics"/>): each load on it at its place (its
        /// floor or roof share step by step along it, each wall standing on it step by step, each point load), each kind alike
        /// (owner: never the largest over the span). Two supports: statics, an overhang past either; more: a continuous beam,
        /// EI the same along it (owner 2026-11-10). Fewer than two: it cannot stand (<see cref="BeamLoad.Stands"/>).
        /// </summary>
        private static void Reactions(BeamLoad r)
        {
            foreach (var x in r.Supports) x.Reaction = new PointLoad();
            double L = r.Span;
            if (L <= Tol) { r.Stands = r.Supports.Count > 0; return; }
            var sets = Enumerable.Range(0, 5).Select(_ => new BeamLoads()).ToArray();
            double[] Kinds(GravityLoad g) => new[] { g.Dead, g.Live, g.RoofLive, g.Snow, g.Wind };
            void Spread(double a, double e, LineLoad q)
            {
                a = Math.Max(0, Math.Min(L, a)); e = Math.Max(0, Math.Min(L, e));
                if (e - a <= Tol || q == null) return;
                var v = Kinds(q);
                for (int k = 0; k < 5; k++) if (v[k] != 0) sets[k].Spread.Add((a, e, v[k]));
            }
            if (r.FromLevelProfile == null || r.FromLevelProfile.IsEmpty) Spread(0, L, r.FromLevel);
            else foreach (var st in r.FromLevelProfile.Steps) Spread(st.A, st.B, st.Load);
            foreach (var p in r.Partials) Spread(p.A, p.B, p.Load);
            foreach (var p in r.Points)
            {
                var v = Kinds(p.Load);
                for (int k = 0; k < 5; k++) if (v[k] != 0) sets[k].Points.Add((Math.Max(0, Math.Min(L, p.At)), v[k]));
            }
            var R = BeamStatics.Reactions(L, r.Supports.Select(x => x.At).ToList(), sets);
            r.Stands = R != null;
            if (R == null) return;
            for (int i = 0; i < r.Supports.Count; i++)
                r.Supports[i].Reaction = new PointLoad { Dead = R[0][i], Live = R[1][i], RoofLive = R[2][i], Snow = R[3][i], Wind = R[4][i] };
        }

        /// <summary>
        /// A level's beams solved together: each one's reactions with what lands on it (<paramref name="fixedPoints"/>: the
        /// columns and the walls' posts) and the reactions of the beams bearing on it. Where beams bear on each other round a
        /// loop (reciprocal framing, as at a corner of overhangs or an opening framed pinwheel-fashion) each reaction depends on
        /// the others: R = R0 + A R, linear. It is solved by going round until nothing changes, each pass carried before
        /// carrier (a level with no loop is done in one pass and confirmed by a second). A beam landing partway along another
        /// passes on only a share of its load (a / L &lt; 1), so a loop converges to the exact answer. A loop of beams resting only
        /// on each other's ends, with nothing under them, passes all of it on and never does: a mechanism. It is said so, and
        /// those ends are left untraced.
        /// </summary>
        private static void SolveBeams(List<Beam> order, List<Line> lines, Dictionary<string, BeamLoad> all,
                                       Dictionary<string, List<ConcentratedLoad>> fixedPoints, Level lv, List<string> notes)
        {
            if (order.Count == 0) return;
            var ids = new HashSet<string>(order.Select(m => m.Id));
            var lineOf = order.ToDictionary(m => m.Id, m => lines.First(l => l.IsBeam && l.Id == m.Id));
            double Size(PointLoad p) => Math.Abs(p.Dead) + Math.Abs(p.Live) + Math.Abs(p.RoofLive) + Math.Abs(p.Snow) + Math.Abs(p.Wind);
            var got = new Dictionary<string, PointLoad[]>();
            const int MostPasses = 5000;
            var moving = new HashSet<string>();
            int pass;
            for (pass = 0; pass < MostPasses; pass++)
            {
                moving.Clear();
                double change = 0, scale = 0;
                foreach (var m in order)
                {
                    var r = all[m.Id];
                    var me = lineOf[m.Id];
                    r.Points.Clear();
                    r.Points.AddRange(fixedPoints[m.Id]);
                    foreach (var o in order)
                    {
                        if (o.Id == m.Id || !got.TryGetValue(o.Id, out var ro)) continue;
                        var os = all[o.Id].Supports;
                        for (int i = 0; i < os.Count; i++)
                            if (os[i].On == m.Id)
                                r.Points.Add(new ConcentratedLoad { Where = os[i].Where, At = Along(me, os[i].Where), Load = ro[i], From = o.Id, Source = o.Id });
                    }
                    Reactions(r);
                    var now = r.Supports.Select(x => x.Reaction).ToArray();
                    double size = now.Sum(Size);
                    double d = got.TryGetValue(m.Id, out var was) && was.Length == now.Length
                        ? now.Select((x, i) => Size(x + was[i].Times(-1))).Sum()
                        : size;
                    if (d > 1e-9 * (1 + size)) moving.Add(m.Id);
                    change += d;
                    scale += size;
                    got[m.Id] = now;
                }
                if (pass > 0 && change <= 1e-9 * (1 + scale)) break;
            }

            // the loops: beams that, following what holds them up, come back to themselves
            bool Reaches(string from, string to)
            {
                var seen = new HashSet<string>();
                var next = new Stack<string>(new[] { from });
                while (next.Count > 0)
                {
                    var x = next.Pop();
                    foreach (var on in all[x].Supports.Select(s => s.On))
                    {
                        if (on == null || !ids.Contains(on)) continue;
                        if (on == to) return true;
                        if (seen.Add(on)) next.Push(on);
                    }
                }
                return false;
            }
            var loop = order.Where(m => Reaches(m.Id, m.Id)).Select(m => m.Id).ToList();
            if (pass < MostPasses)
            {
                if (loop.Count > 0)
                    notes.Add($"{string.Join(", ", loop)} at {lv.Name} bear on each other round a loop (reciprocal framing): solved together, each reaction with what comes back round the loop.");
                return;
            }

            // a mechanism: the ends in the loop that rest on another's end, with nothing under them, carry nothing
            var said = new List<Point2>();
            int cut = 0;
            foreach (var m in order.Where(m => moving.Contains(m.Id)))
            {
                var r = all[m.Id];
                foreach (var x in r.Supports.Where(x => x.Name == "start" || x.Name == "end"))
                {
                    var on = x.On;
                    if (on == null || !moving.Contains(on) || !order.Any(o => o.Id == on && (Dist(o.Start, x.Where) <= Bearing || Dist(o.End, x.Where) <= Bearing))) continue;
                    cut++;
                    x.On = null;
                    if (said.Any(q => Dist(q, x.Where) <= Bearing)) continue;
                    said.Add(x.Where);
                    notes.Add($"{m.Id} and {on} at {lv.Name} rest on each other's ends at ({x.Where.X:0.##}, {x.Where.Y:0.##}) with nothing under them: a mechanism, not traced down there. Draw a column, a post or a wall under that point.");
                }
            }
            if (cut > 0) SolveBeams(order, lines, all, fixedPoints, lv, notes);
            else notes.Add($"The beams {string.Join(", ", moving)} at {lv.Name} bearing on each other do not settle: their reactions are not reliable.");
        }

        /// <summary>A floor or roof on a level: where it is and which way its framing spans.</summary>
        private sealed class Plate
        {
            public Region Region;
            public SpanDirection Span;
            public double Overhang;
            /// <summary>Its joists' or rafters' spacing (ft): a beam running along them carries half of it each side.</summary>
            public double Spacing;
            /// <summary>The wind on it (the roof, in the uplift pass), else null.</summary>
            public RoofWindCase Wind;
        }

        private static List<Plate> Plates(Building b, Level lv, Level roofLevel, RoofWindCase wind)
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
                list.Add(new Plate { Region = Region.Of(new[] { Grow(plan) }), Span = span, Overhang = o, Wind = wind, Spacing = (b.Defaults?.RafterSpacing ?? 0) / 12 });
                return list;
            }
            var holes = b.OpeningsCutting(lv).SelectMany(x => x.Pieces).ToList();
            foreach (var d in b.Diaphragms.Where(d => d.LevelId == lv.Id && d.Pieces != null && d.Pieces.Count > 0))
                list.Add(new Plate { Region = Region.Of(d.Pieces.Select(Grow)).Minus(holes), Span = d.EffectiveSpan, Overhang = o, Wind = isRoof ? wind : null,
                                     Spacing = (isRoof ? b.Defaults?.RafterSpacing ?? 0 : b.Defaults?.FloorJoistSpacing ?? 0) / 12 });
            return list;
        }

        /// <summary>
        /// The tributary width on a bearing line from every plate on the level, the most to an edge past any eave, the most line
        /// load (each kind, along the line: the area loads across the tributary strip, a zone's in its part) and the zones met.
        /// </summary>
        /// <remarks>
        /// Every line's share at once, the lines across the framing first: a beam running along it (<see cref="AlongStrips"/>)
        /// gets the strip beside it from them, taken as they are found (so none is counted twice), but never from a member it
        /// carries (<paramref name="carries"/>): a girder takes nothing off the beams framing into it (owner 2026-11-10).
        /// </remarks>
        private static Dictionary<Line, (double lo, double hi, double edge, LineLoad load, List<string> zones, double span, LoadProfile profile)> Tributaries(
            List<Line> lines, List<Plate> plates, AreaLoad area, List<LoadZone> zones, Func<LineLoad, LineLoad, LineLoad> pick, Func<Line, Line, bool> carries)
        {
            var per = lines.ToDictionary(l => l, l => new List<(Plate p, double lo, double hi, double edge, LineLoad load, List<string> zones, double span, LoadProfile profile)>());
            var taken = new Dictionary<(Line, Plate), List<(double fa, double ga, double ov, PointLoad total)>>();
            var takenZones = new Dictionary<(Line, Plate), List<string>>();
            foreach (var p in plates)
            {
                var strips = AlongStrips(p, lines);
                foreach (var x in strips) { taken[(x.m, p)] = new List<(double, double, double, PointLoad)>(); takenZones[(x.m, p)] = new List<string>(); }
                foreach (var w in lines.Where(w => Across(w, p)))
                {
                    var (lo, hi, edge, load, z, span) = Tributary(w, p, lines, strips, carries, taken, takenZones, area, zones, pick, out var one);
                    per[w].Add((p, lo, hi, edge, load, z, span, one));
                }
            }
            foreach (var p in plates)
                foreach (var x in AlongStrips(p, lines))
                {
                    var (lo, hi, load) = AlongStrip(x.m, p, x.a, x.b, taken[(x.m, p)], pick, out var one);
                    per[x.m].Add((p, lo, hi, 0, load, takenZones[(x.m, p)], 0, one));
                }
            var result = new Dictionary<Line, (double, double, double, LineLoad, List<string>, double, LoadProfile)>();
            foreach (var kv in per)
            {
                var profile = new LoadProfile();
                double tMin = double.MaxValue, tMax = 0, edgeMax = 0, spanMax = 0;
                LineLoad most = null;
                var met = new List<string>();
                foreach (var t in kv.Value)
                {
                    if (t.hi <= 0) continue;
                    tMin = Math.Min(tMin, t.lo); tMax = Math.Max(tMax, t.hi); edgeMax = Math.Max(edgeMax, t.edge - t.p.Overhang); spanMax = Math.Max(spanMax, t.span);
                    most = pick(most, t.load);
                    profile = profile.Combine(t.profile, pick);
                    met.AddRange(t.zones.Where(x => !met.Contains(x)));
                }
                result[kv.Key] = (tMax > 0 ? tMin : 0, tMax, edgeMax, most ?? new LineLoad(), met, spanMax, profile);
            }
            return result;
        }

        /// <summary>Whether a line is across a plate's framing: the joists or rafters bear on it.</summary>
        private static bool Across(Line w, Plate p) =>
            p.Span == SpanDirection.WestEast ? w.Dir == WallDirection.SouthNorth : w.Dir == WallDirection.WestEast;

        /// <summary>
        /// The line load from a strip across the span (from <paramref name="f"/> to <paramref name="g"/>) at <paramref name="s"/>
        /// along the line: the level's area loads, a zone's where it covers (the larger of each kind where zones overlap).
        /// </summary>
        private static LineLoad Strip(bool alongX, double s, double f, double g, AreaLoad area, List<LoadZone> zones, List<string> met)
        {
            var here = zones.Where(z => alongX ? z.Y0 + Tol < s && s < z.Y1 - Tol : z.X0 + Tol < s && s < z.X1 - Tol)          // inside it, not only touching its edge
                            .Select(z => (z, a: alongX ? z.X0 : z.Y0, b: alongX ? z.X1 : z.Y1))
                            .Where(x => x.b > f + Tol && x.a < g - Tol).ToList();
            if (here.Count == 0) return area.Times(g - f);
            var cuts = new List<double> { f, g };
            foreach (var x in here) { if (x.a > f && x.a < g) cuts.Add(x.a); if (x.b > f && x.b < g) cuts.Add(x.b); }
            cuts = cuts.Distinct().OrderBy(c => c).ToList();
            var sum = new LineLoad();
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                double m = (cuts[k] + cuts[k + 1]) / 2, len = cuts[k + 1] - cuts[k];
                var cover = here.Where(x => x.a <= m && m <= x.b).ToList();
                AreaLoad q = area;
                if (cover.Count > 0)
                {
                    q = new AreaLoad { Dead = cover.Max(x => x.z.Load.Dead), Live = cover.Max(x => x.z.Load.Live), RoofLive = cover.Max(x => x.z.Load.RoofLive), Snow = cover.Max(x => x.z.Load.Snow) };
                    foreach (var x in cover) if (!met.Contains(x.z.Name)) met.Add(x.z.Name);
                }
                sum += q.Times(len);
            }
            return sum;
        }

        /// <summary>
        /// The tributary width of a plate's framing on a bearing line (ft): the least and the most along it, and the most taken to
        /// an edge with no support (0, 0 when the framing runs along the line or the plate does not reach it).
        /// </summary>
        private static (double lo, double hi, double edge, LineLoad load, List<string> zones, double span) Tributary(Line w, Plate p, List<Line> all,
            List<(Line m, double c, double h, double a, double b)> strips, Func<Line, Line, bool> carries,
            Dictionary<(Line, Plate), List<(double fa, double ga, double ov, PointLoad total)>> taken, Dictionary<(Line, Plate), List<string>> takenZones,
            AreaLoad area, List<LoadZone> zones, Func<LineLoad, LineLoad, LineLoad> pick, out LoadProfile profile)
        {
            profile = new LoadProfile();
            var steps = new List<ProfileStep>();
            LineLoad most = null;
            var met = new List<string>();
            // joists running W-E bear on S-N lines, and the other way round
            bool alongX = p.Span == SpanDirection.WestEast;
            if (!Across(w, p)) return (0, 0, 0, new LineLoad(), met, 0);
            double c0 = alongX ? w.Start.X : w.Start.Y;                                   // the line's position across the span
            double s0 = alongX ? Math.Min(w.Start.Y, w.End.Y) : Math.Min(w.Start.X, w.End.X);
            double s1 = alongX ? Math.Max(w.Start.Y, w.End.Y) : Math.Max(w.Start.X, w.End.X);

            // the other bearing lines across the span: (position, from, to)
            var lines = all.Where(o => !(o.Id == w.Id && o.IsBeam == w.IsBeam) && o.Dir == w.Dir)
                           .Select(o => alongX ? (c: o.Start.X, a: Math.Min(o.Start.Y, o.End.Y), b: Math.Max(o.Start.Y, o.End.Y))
                                               : (c: o.Start.Y, a: Math.Min(o.Start.X, o.End.X), b: Math.Max(o.Start.X, o.End.X))).ToList();
            var cells = p.Region.Cells().ToList();
            int n = Math.Max(1, (int)Math.Ceiling((s1 - s0) / Step));
            double lo = double.MaxValue, hi = 0, edge = 0, span = 0;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                double s = s0 + (i + 0.5) * (s1 - s0) / n;
                // the plate's extent across the span through this point of the line
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
                // a line within Bearing of this one is the same line (drawn by hand)
                var here = lines.Where(l => l.a - Tol <= s && s <= l.b + Tol && Math.Abs(l.c - c0) > Bearing).Select(l => l.c).ToList();
                var below = here.Where(c => c < c0 && c >= a - Tol).DefaultIfEmpty(double.NaN).Max();
                var above = here.Where(c => c > c0 && c <= bb + Tol).DefaultIfEmpty(double.NaN).Min();
                double left = double.IsNaN(below) ? c0 - a : (c0 - below) / 2;
                double right = double.IsNaN(above) ? bb - c0 : (above - c0) / 2;
                if (double.IsNaN(below)) edge = Math.Max(edge, c0 - a);
                if (double.IsNaN(above)) edge = Math.Max(edge, bb - c0);
                // the framing landing on it spans to the next line on each side (to the edge where there is none on either)
                span = Math.Max(span, double.IsNaN(below) && double.IsNaN(above) ? Math.Max(c0 - a, bb - c0)
                                      : Math.Max(double.IsNaN(below) ? 0 : c0 - below, double.IsNaN(above) ? 0 : above - c0));
                double t = Math.Max(0, left) + Math.Max(0, right);
                lo = Math.Min(lo, t); hi = Math.Max(hi, t);
                double f = c0 - Math.Max(0, left), g = c0 + Math.Max(0, right);
                var strip = Strip(alongX, s, f, g, area, zones, met);
                if (p.Wind != null) strip.Wind += WindAcross(p.Wind, alongX, s, f, g);
                double st = alongX ? w.Start.Y : w.Start.X, half = (s1 - s0) / n / 2;
                // less the strips the beams along the framing take (each counted once, on the beam): the share of this
                // step inside a strip, over the part of the span the beam runs along; never off a member the beam carries
                foreach (var ts in strips)
                {
                    if (carries(ts.m, w)) continue;
                    double ov = Math.Min(s + half, ts.c + ts.h) - Math.Max(s - half, ts.c - ts.h);
                    double fa = Math.Max(f, ts.a), ga = Math.Min(g, ts.b);
                    if (ov <= 1e-9 || ga - fa <= 1e-9) continue;
                    var off = Strip(alongX, s, fa, ga, area, zones, takenZones[(ts.m, p)]);
                    if (p.Wind != null) off.Wind += WindAcross(p.Wind, alongX, s, fa, ga);
                    strip = Less(strip, off, Math.Min(1, ov / (2 * half)));
                    taken[(ts.m, p)].Add((fa, ga, ov, off.Over(ov)));
                }
                most = pick(most, strip);
                steps.Add(new ProfileStep { A = Math.Min(Math.Abs(s - half - st), Math.Abs(s + half - st)), B = Math.Max(Math.Abs(s - half - st), Math.Abs(s + half - st)), Load = strip });
            }
            profile = LoadProfile.Of(steps, pick);
            return any ? (lo, hi, edge, most ?? new LineLoad(), met, span) : (0, 0, 0, new LineLoad(), new List<string>(), 0);
        }

        private static LineLoad Less(LineLoad a, LineLoad b, double k) => new LineLoad
        {
            Dead = a.Dead - k * b.Dead, Live = a.Live - k * b.Live, RoofLive = a.RoofLive - k * b.RoofLive, Snow = a.Snow - k * b.Snow, Wind = a.Wind - k * b.Wind,
        };

        /// <summary>
        /// The beams running along a plate's framing (trimmers, beams parallel to the joists or rafters) and the strip each
        /// carries: half a spacing each side (<see cref="Plate.Spacing"/>), less where another line along the framing is nearer.
        /// Each: the beam, its position across the span, the strip's half width, and where it runs along the span.
        /// </summary>
        private static List<(Line m, double c, double h, double a, double b)> AlongStrips(Plate p, List<Line> all)
        {
            var list = new List<(Line m, double c, double h, double a, double b)>();
            if (!(p.Spacing > 0)) return list;
            bool alongX = p.Span == SpanDirection.WestEast;
            var dir = alongX ? WallDirection.WestEast : WallDirection.SouthNorth;
            (double c, double a, double b) Of(Line o) => alongX ? (o.Start.Y, Math.Min(o.Start.X, o.End.X), Math.Max(o.Start.X, o.End.X))
                                                                : (o.Start.X, Math.Min(o.Start.Y, o.End.Y), Math.Max(o.Start.Y, o.End.Y));
            var par = all.Where(o => o.Dir == dir).ToList();
            foreach (var m in par.Where(o => o.IsBeam))
            {
                var (c, a, b) = Of(m);
                double h = p.Spacing / 2;
                foreach (var o in par)
                {
                    if (ReferenceEquals(o, m)) continue;
                    var (oc, oa, ob) = Of(o);
                    if (ob <= a + Tol || oa >= b - Tol) continue;
                    double d = Math.Abs(oc - c);
                    if (d > Bearing) h = Math.Min(h, d / 2);
                }
                list.Add((m, c, h, a, b));
            }
            return list;
        }

        /// <summary>
        /// The floor or roof a beam running along the framing carries: what it took from the lines across the framing beside
        /// it (<see cref="Tributaries"/>), half a spacing each side where there is floor (none over an opening) and where no
        /// member it carries has it, spread step by step along it as found.
        /// </summary>
        private static (double lo, double hi, LineLoad load) AlongStrip(Line w, Plate p, double a0, double b0,
            List<(double fa, double ga, double ov, PointLoad total)> got, Func<LineLoad, LineLoad, LineLoad> pick, out LoadProfile profile)
        {
            profile = new LoadProfile();
            bool alongX = p.Span == SpanDirection.WestEast;
            int n = Math.Max(1, (int)Math.Ceiling((b0 - a0) / Step));
            double d = (b0 - a0) / n, st = alongX ? w.Start.X : w.Start.Y;
            if (!(d > 1e-9)) return (0, 0, new LineLoad());
            var lb = Enumerable.Range(0, n).Select(i => new PointLoad()).ToArray();
            var width = new double[n];
            foreach (var g in got)
            {
                double len = g.ga - g.fa;
                if (len <= 1e-12) continue;
                for (int i = 0; i < n; i++)
                {
                    double x0 = a0 + i * d, o = Math.Min(x0 + d, g.ga) - Math.Max(x0, g.fa);
                    if (o <= 0) continue;
                    lb[i] = lb[i] + g.total.Times(o / len);
                    width[i] += g.ov * o / d;
                }
            }
            LineLoad most = null;
            var steps = new List<ProfileStep>();
            for (int i = 0; i < n; i++)
            {
                var q = new LineLoad { Dead = lb[i].Dead / d, Live = lb[i].Live / d, RoofLive = lb[i].RoofLive / d, Snow = lb[i].Snow / d, Wind = lb[i].Wind / d };
                most = pick(most, q);
                double x0 = a0 + i * d, x1 = x0 + d;
                steps.Add(new ProfileStep { A = Math.Min(Math.Abs(x0 - st), Math.Abs(x1 - st)), B = Math.Max(Math.Abs(x0 - st), Math.Abs(x1 - st)), Load = q });
            }
            profile = LoadProfile.Of(steps, pick);
            double hi = width.Max();
            return (hi > 1e-9 ? width.Min() : 0, hi > 1e-9 ? hi : 0, most ?? new LineLoad());
        }

        /// <summary>
        /// The wind across a tributary strip (plf, + down): the pressure taken at points <see cref="Step"/> apart from <paramref name="f"/> to
        /// <paramref name="g"/> across the span, at <paramref name="s"/> along the line.
        /// </summary>
        private static double WindAcross(RoofWindCase wind, bool alongX, double s, double f, double g)
        {
            if (wind?.PressureAt == null || g - f <= 0) return 0;
            int n = Math.Max(1, (int)Math.Ceiling((g - f) / Step));
            double d = (g - f) / n, sum = 0;
            for (int k = 0; k < n; k++)
            {
                double c = f + (k + 0.5) * d;
                sum += (alongX ? wind.PressureAt(c, s) : wind.PressureAt(s, c)) * d;
            }
            return sum;
        }

        /// <summary>How long two walls run on the same line together (ft); 0 when they are not on one line.</summary>
        private static double Overlap(Wall a, Wall b)
        {
            if (b.Direction == WallDirection.NotOrthogonal) return 0;
            var (o0, o1) = OverlapAlong(new Line { Id = b.Id, Start = b.Start, End = b.End, Dir = b.Direction }, a.Start, a.End, a.Direction);
            return Math.Max(0, o1 - o0);
        }

        /// <summary>Where a segment on the same line overlaps a bearing line, along it from its start (ft); (0, 0) when it does not.</summary>
        private static (double a, double b) OverlapAlong(Line l, Point2 s, Point2 e, WallDirection d)
        {
            if (d == WallDirection.NotOrthogonal || l.Dir != d) return (0, 0);
            bool we = d == WallDirection.WestEast;
            if (Math.Abs(we ? s.Y - l.Start.Y : s.X - l.Start.X) > (l.IsBeam ? Bearing : Tol)) return (0, 0);
            double l0 = we ? Math.Min(l.Start.X, l.End.X) : Math.Min(l.Start.Y, l.End.Y), l1 = we ? Math.Max(l.Start.X, l.End.X) : Math.Max(l.Start.Y, l.End.Y);
            double a0 = we ? Math.Min(s.X, e.X) : Math.Min(s.Y, e.Y), a1 = we ? Math.Max(s.X, e.X) : Math.Max(s.Y, e.Y);
            double lo = Math.Max(l0, a0), hi = Math.Min(l1, a1);
            if (hi - lo <= 0) return (0, 0);
            // measured from the line's start, whichever way it was drawn
            double st = we ? l.Start.X : l.Start.Y;
            double x0 = Math.Abs(lo - st), x1 = Math.Abs(hi - st);
            return (Math.Min(x0, x1), Math.Max(x0, x1));
        }

        /// <summary>How far along a line from its start a point is (ft, its projection, kept on the line).</summary>
        private static double Along(Line l, Point2 p)
        {
            double len = l.Length;
            if (len < 1e-9) return 0;
            double t = ((p.X - l.Start.X) * (l.End.X - l.Start.X) + (p.Y - l.Start.Y) * (l.End.Y - l.Start.Y)) / len;
            return Math.Max(0, Math.Min(len, t));
        }

        private static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double DistanceToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Dist(p, a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return Dist(p, new Point2(a.X + t * dx, a.Y + t * dy));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

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
        public PointLoad StartReaction { get; set; } = new PointLoad();
        public PointLoad EndReaction { get; set; } = new PointLoad();
        /// <summary>What each end bears on (a wall, a column, a beam), or null when nothing is drawn under it.</summary>
        public string StartOn { get; set; }
        public string EndOn { get; set; }
        public List<string> Notes { get; } = new List<string>();
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

                // what carries a point at this level: a wall under it, a column under it, else a beam on it (not at that beam's own end)
                string SupportOf(Point2 at, string exceptBeam, string exceptColumn)
                {
                    var w = bearing.FirstOrDefault(x => DistanceToSegment(at, x.Start, x.End) <= Bearing);
                    if (w != null) return w.Id;
                    var c = columns.FirstOrDefault(x => x.Id != exceptColumn && Dist(x.At, at) <= Bearing);
                    if (c != null) return c.Id;
                    return beams.FirstOrDefault(x => x.Id != exceptBeam && DistanceToSegment(at, x.Start, x.End) <= Bearing && Dist(at, x.Start) > Bearing && Dist(at, x.End) > Bearing)?.Id;
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

                // (b) the beams, those framing into a girder before the girder
                var ends = beams.ToDictionary(m => m.Id, m => (start: SupportOf(m.Start, m.Id, null), end: SupportOf(m.End, m.Id, null)));
                var order = new List<Beam>();
                var left = beams.ToList();
                while (left.Count > 0)
                {
                    var ready = left.Where(m => !left.Any(o => o.Id != m.Id && (ends[o.Id].start == m.Id || ends[o.Id].end == m.Id))).ToList();
                    if (ready.Count == 0)
                    {
                        result.Notes.Add($"Beams at {lv.Name} bear on each other in a loop ({string.Join(", ", left.Select(m => m.Id))}): their reactions may be missing a share.");
                        ready = left.ToList();
                    }
                    order.AddRange(ready);
                    left.RemoveAll(ready.Contains);
                }
                foreach (var m in order)
                {
                    var me = lines.First(l => l.IsBeam && l.Id == m.Id);
                    var r = new BeamLoad { BeamId = m.Id, LevelId = lv.Id, Span = me.Length };
                    result.Beams[m.Id] = r;
                    if (me.Dir == WallDirection.NotOrthogonal) r.Notes.Add("Not square to the plan: the floor or roof on it is not found (only what stands or lands on it).");
                    else
                    {
                        var (lo, hi, edge, load, met, span) = TributaryAll(me, plates, lines, area, zones, Most, out var beamProfile);
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
                    foreach (var g in landings.Where(g => g.Target == m.Id))
                        r.Points.Add(new ConcentratedLoad { Where = g.Where, At = Along(me, g.Where), Load = g.Load, From = g.From, Source = g.Source });
                    Reactions(r);
                    r.StartOn = ends[m.Id].start; r.EndOn = ends[m.Id].end;
                    foreach (var (on, pt, R, which) in new[] { (r.StartOn, m.Start, r.StartReaction, "start"), (r.EndOn, m.End, r.EndReaction, "end") })
                    {
                        if (on == null) { r.Notes.Add($"Nothing drawn under its {which} ({pt.X:0.##}, {pt.Y:0.##}): its reaction is not traced down."); continue; }
                        if (!R.IsEmpty) landings.Add(new Landing { Target = on, Where = pt, Load = R, From = m.Id, Source = m.Id });
                    }
                }

                // (c) the walls
                foreach (var w in bearing)
                {
                    var me = lines.First(l => !l.IsBeam && l.Id == w.Id);
                    var r = new WallTopLoad { WallId = w.Id, LevelId = lv.Id };
                    var (lo, hi, edge, load, met, span) = TributaryAll(me, plates, lines, area, zones, Most, out var levelProfile);
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
        /// A simple span's end reactions: each load on it on its own, by statics (its floor or roof share step by step along
        /// it, each wall standing on it step by step, each point load), each kind alike, and summed (owner: never the largest
        /// over the span).
        /// </summary>
        private static void Reactions(BeamLoad r)
        {
            double L = r.Span;
            if (L <= Tol) return;
            var total = new PointLoad();
            var end = new PointLoad();
            if (r.FromLevelProfile == null || r.FromLevelProfile.IsEmpty)
            {
                total = r.FromLevel.Over(L);
                end = r.FromLevel.Over(L).Times(0.5);
            }
            else
                foreach (var st in r.FromLevelProfile.Steps)
                {
                    double a = Math.Max(0, Math.Min(L, st.A)), e = Math.Max(0, Math.Min(L, st.B));
                    if (e - a <= Tol) continue;
                    var q = st.Load.Over(e - a);
                    total += q;
                    end += q.Times((a + e) / 2 / L);
                }
            foreach (var p in r.Partials)
            {
                var q = p.Load.Over(p.B - p.A);
                total += q;
                end += q.Times((p.A + p.B) / 2 / L);
            }
            foreach (var p in r.Points)
            {
                double x = Math.Max(0, Math.Min(L, p.At));
                total += p.Load;
                end += p.Load.Times(x / L);
            }
            r.EndReaction = end;
            r.StartReaction = total + end.Times(-1);
        }

        /// <summary>A floor or roof on a level: where it is and which way its framing spans.</summary>
        private sealed class Plate
        {
            public Region Region;
            public SpanDirection Span;
            public double Overhang;
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
                list.Add(new Plate { Region = Region.Of(new[] { Grow(plan) }), Span = span, Overhang = o, Wind = wind });
                return list;
            }
            var holes = b.OpeningsCutting(lv).SelectMany(x => x.Pieces).ToList();
            foreach (var d in b.Diaphragms.Where(d => d.LevelId == lv.Id && d.Pieces != null && d.Pieces.Count > 0))
                list.Add(new Plate { Region = Region.Of(d.Pieces.Select(Grow)).Minus(holes), Span = d.EffectiveSpan, Overhang = o, Wind = isRoof ? wind : null });
            return list;
        }

        /// <summary>
        /// The tributary width on a bearing line from every plate on the level, the most to an edge past any eave, the most line
        /// load (each kind, along the line: the area loads across the tributary strip, a zone's in its part) and the zones met.
        /// </summary>
        private static (double lo, double hi, double edge, LineLoad load, List<string> zones, double span) TributaryAll(Line me, List<Plate> plates, List<Line> lines, AreaLoad area, List<LoadZone> zones,
            Func<LineLoad, LineLoad, LineLoad> pick, out LoadProfile profile)
        {
            profile = new LoadProfile();
            double tMin = double.MaxValue, tMax = 0, edgeMax = 0, spanMax = 0;
            LineLoad most = null;
            var met = new List<string>();
            foreach (var p in plates)
            {
                var (lo, hi, edge, load, z, span) = Tributary(me, p, lines, area, zones, pick, out var one);
                if (hi <= 0) continue;
                tMin = Math.Min(tMin, lo); tMax = Math.Max(tMax, hi); edgeMax = Math.Max(edgeMax, edge - p.Overhang); spanMax = Math.Max(spanMax, span);
                most = pick(most, load);
                profile = profile.Combine(one, pick);
                met.AddRange(z.Where(x => !met.Contains(x)));
            }
            return (tMax > 0 ? tMin : 0, tMax, edgeMax, most ?? new LineLoad(), met, spanMax);
        }

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
        private static (double lo, double hi, double edge, LineLoad load, List<string> zones, double span) Tributary(Line w, Plate p, List<Line> all, AreaLoad area, List<LoadZone> zones,
            Func<LineLoad, LineLoad, LineLoad> pick, out LoadProfile profile)
        {
            profile = new LoadProfile();
            var steps = new List<ProfileStep>();
            LineLoad most = null;
            var met = new List<string>();
            // joists running W-E bear on S-N lines, and the other way round
            bool alongX = p.Span == SpanDirection.WestEast;
            if (alongX ? w.Dir != WallDirection.SouthNorth : w.Dir != WallDirection.WestEast) return (0, 0, 0, new LineLoad(), met, 0);
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
                most = pick(most, strip);
                double st = alongX ? w.Start.Y : w.Start.X, half = (s1 - s0) / n / 2;
                steps.Add(new ProfileStep { A = Math.Min(Math.Abs(s - half - st), Math.Abs(s + half - st)), B = Math.Max(Math.Abs(s - half - st), Math.Abs(s + half - st)), Load = strip });
            }
            profile = LoadProfile.Of(steps, pick);
            return any ? (lo, hi, edge, most ?? new LineLoad(), met, span) : (0, 0, 0, new LineLoad(), new List<string>(), 0);
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

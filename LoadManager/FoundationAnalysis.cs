using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace LoadManager
{
    public enum DesignMethod
    {
        /// <summary>Allowable stress design: ASCE 7-22 2.4 and the IBC 1605.2 alternative combinations.</summary>
        Asd,
        /// <summary>Strength design (LRFD): ASCE 7-22 2.3.</summary>
        Strength,
    }

    /// <summary>
    /// The service loads on a piece of foundation, each kind on its own (line loads plf or point loads lb): the gravity loads, and
    /// the wind as the range its cases give (+ down, − up), as each combination may take either end of it.
    /// </summary>
    public sealed class ServiceLoad
    {
        public double Dead { get; set; }
        public double Live { get; set; }
        public double RoofLive { get; set; }
        public double Snow { get; set; }
        /// <summary>The wind lifting most (the lowest; + down).</summary>
        public double WindMin { get; set; }
        /// <summary>The wind pressing down most (the highest).</summary>
        public double WindMax { get; set; }
        /// <summary>The wind in each wind case (<see cref="WindCase.Name"/>; + down): a combination with W takes one case at a time.</summary>
        public Dictionary<string, double> Winds { get; set; } = new Dictionary<string, double>();

        /// <summary>The wind in one case (0 where it has none here).</summary>
        public double Wind(string windCase) => windCase != null && Winds != null && Winds.TryGetValue(windCase, out var v) ? v : 0;

        public double Of(LoadType t) => t == LoadType.Dead ? Dead : t == LoadType.Live ? Live : t == LoadType.RoofLive ? RoofLive : t == LoadType.Snow ? Snow : 0;

        public bool Has(LoadType t) => t == LoadType.Dead || (t == LoadType.Wind ? Math.Abs(WindMin) > 1e-9 || Math.Abs(WindMax) > 1e-9 : Of(t) > 1e-9);

        public bool IsEmpty => !Has(LoadType.Live) && !Has(LoadType.RoofLive) && !Has(LoadType.Snow) && !Has(LoadType.Wind) && Math.Abs(Dead) < 1e-9;

        /// <summary>
        /// The most the combination gives (down): the dead load at its factor, each variable load where it adds to it or else set to
        /// zero ("each load combination shall be investigated with one or more of the variable loads set to zero").
        /// </summary>
        public double Most(LoadCombination c) => c.Terms.Sum(t => Term(t, true, null));

        /// <summary>The least (the most uplift): the dead load at its factor, each variable load where it takes away or else zero.</summary>
        public double Least(LoadCombination c) => c.Terms.Sum(t => Term(t, false, null));

        /// <summary>
        /// The most in one case: with W, the wind of that one wind case, as it is (+ or −, the same case everywhere); the gravity
        /// variable loads each in or set to zero, whichever is worse.
        /// </summary>
        public double Most(FoundationCase c) => c.Combination.Terms.Sum(t => Term(t, true, c.Wind?.Name));

        public double Least(FoundationCase c) => c.Combination.Terms.Sum(t => Term(t, false, c.Wind?.Name));

        private double Term(LoadTerm t, bool most, string windCase)
        {
            if (t.Type == LoadType.Dead) return t.Factor * Dead;
            if (t.Type == LoadType.Wind && windCase != null) return t.Factor * Wind(windCase);
            double lo, hi;
            if (t.Type == LoadType.Wind) { lo = Math.Min(0, WindMin); hi = Math.Max(0, WindMax); }
            else { double v = Of(t.Type); lo = Math.Min(0, v); hi = Math.Max(0, v); }
            double a = t.Factor * lo, b = t.Factor * hi;
            return most ? Math.Max(a, b) : Math.Min(a, b);
        }

        public static ServiceLoad Of(GravityLoad g) => new ServiceLoad { Dead = g?.Dead ?? 0, Live = g?.Live ?? 0, RoofLive = g?.RoofLive ?? 0, Snow = g?.Snow ?? 0, PathWind = g?.Wind ?? 0 };

        /// <summary>
        /// The roof's wind an uplift pass carried down with it (<see cref="GravityLoad.Wind"/>, + down), not yet given a case:
        /// the free bodies take it into one wind case (<see cref="FreeBody.OfBody"/>). Not in any combination by itself.
        /// </summary>
        public double PathWind { get; set; }

        public ServiceLoad WithWind(double min, double max) => new ServiceLoad { Dead = Dead, Live = Live, RoofLive = RoofLive, Snow = Snow, WindMin = min, WindMax = max };

        /// <summary>With the wind of each wind case (its range set from them).</summary>
        public ServiceLoad WithWinds(Dictionary<string, double> winds)
        {
            var w = winds ?? new Dictionary<string, double>();
            return new ServiceLoad
            {
                Dead = Dead, Live = Live, RoofLive = RoofLive, Snow = Snow, Winds = w,
                WindMin = w.Count == 0 ? 0 : w.Values.Min(), WindMax = w.Count == 0 ? 0 : w.Values.Max(),
            };
        }

        public override string ToString() => $"D {Dead:0.##}, L {Live:0.##}, Lr {RoofLive:0.##}, S {Snow:0.##}" + (Has(LoadType.Wind) ? $", W {WindMin:0.##} to {WindMax:0.##}" : "");
    }

    /// <summary>
    /// One wind case for the whole building: one of the wind calculator's own cases, with its roof pressures and the shear walls'
    /// end forces the lateral design saved under the same name.
    /// </summary>
    public sealed class WindCase
    {
        /// <summary>The wind calculator's case name: "Case 2, from W, roof Case A, +GCpi, torsion +".</summary>
        public string Name { get; set; }
        /// <summary>
        /// The lateral design's case: the same wind, one way the shear walls share it ("… (rigid)", "… (flexible)"; then it is
        /// also <see cref="Name"/>), null: none saved for it.
        /// </summary>
        public string Lateral { get; set; }
        /// <summary>Where it comes from ("W", "SW", ...; null: not known).</summary>
        public string From { get; set; }
        /// <summary>The roof wind case's index in <see cref="WindUplift.Runs"/> (−1: none) and its name.</summary>
        public int Roof { get; set; } = -1;
        public string RoofName { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>A load combination as investigated: the code's combination, and for one with W the one wind case it is taken in.</summary>
    public sealed class FoundationCase
    {
        public LoadCombination Combination { get; set; }
        /// <summary>The wind case (null: the combination has no W).</summary>
        public WindCase Wind { get; set; }
        public string Name => Wind == null ? Combination.Name : $"{Combination.Name} · W: {Wind.Name}";
        public string Equation => Combination.Equation;
        public override string ToString() => Name;
    }

    /// <summary>One combination's range on a piece of foundation (+ down).</summary>
    public sealed class CombinedLoad
    {
        public FoundationCase Case { get; set; }
        public LoadCombination Combination => Case?.Combination;
        public double Max { get; set; }
        public double Min { get; set; }
    }

    /// <summary>The most down and the most up over every combination (and wind case), each with the one giving it.</summary>
    public sealed class LoadEnvelope
    {
        public double Max { get; set; }
        public FoundationCase MaxBy { get; set; }
        public double Min { get; set; }
        public FoundationCase MinBy { get; set; }
        public bool Lifts => Min < -1e-9;

        public static LoadEnvelope Of(IEnumerable<CombinedLoad> all)
        {
            LoadEnvelope e = null;
            foreach (var c in all)
            {
                if (e == null) { e = new LoadEnvelope { Max = c.Max, MaxBy = c.Case, Min = c.Min, MinBy = c.Case }; continue; }
                if (c.Max > e.Max + 1e-9) { e.Max = c.Max; e.MaxBy = c.Case; }
                if (c.Min < e.Min - 1e-9) { e.Min = c.Min; e.MinBy = c.Case; }
            }
            return e ?? new LoadEnvelope();
        }

        /// <summary>Over several envelopes (the steps along a wall): the largest and the least of them.</summary>
        public static LoadEnvelope Over(IEnumerable<LoadEnvelope> all)
        {
            LoadEnvelope e = null;
            foreach (var c in all)
            {
                if (e == null) { e = new LoadEnvelope { Max = c.Max, MaxBy = c.MaxBy, Min = c.Min, MinBy = c.MinBy }; continue; }
                if (c.Max > e.Max + 1e-9) { e.Max = c.Max; e.MaxBy = c.MaxBy; }
                if (c.Min < e.Min - 1e-9) { e.Min = c.Min; e.MinBy = c.MinBy; }
            }
            return e ?? new LoadEnvelope();
        }
    }

    /// <summary>A stretch of a wall's footing with uniform loads, from <see cref="A"/> to <see cref="B"/> along the wall from its start (ft).</summary>
    public sealed class FoundationSegment
    {
        public double A { get; set; }
        public double B { get; set; }
        public double Length => B - A;
        /// <summary>Service line loads (plf).</summary>
        public ServiceLoad Load { get; set; }
        public List<CombinedLoad> Combined { get; } = new List<CombinedLoad>();
        public LoadEnvelope Envelope { get; set; }
    }

    public enum FoundationPointKind
    {
        /// <summary>A post in a bearing wall (under a beam end or a column above).</summary>
        Post,
        /// <summary>A column standing on the foundation.</summary>
        Column,
        /// <summary>A shear wall's end (hold-down or its compression), from the lateral design.</summary>
        Lateral,
    }

    /// <summary>A concentrated load on the foundation (lb).</summary>
    public sealed class FoundationPoint
    {
        /// <summary>What it is ("C1", "BM1 via W7", "SW W3 end"); several joined with " + " where they are at one place.</summary>
        public string From { get; set; }
        public FoundationPointKind Kind { get; set; }
        /// <summary>Also a shear wall's end there (its hold-down or compression force is in the wind).</summary>
        public bool HasLateral { get; set; }
        public Point2 Where { get; set; }
        /// <summary>The wall whose footing it is on (null: on its own, a column's pad).</summary>
        public string WallId { get; set; }
        /// <summary>Along that wall from its start (ft).</summary>
        public double At { get; set; }
        public ServiceLoad Load { get; set; }
        /// <summary>The roof wind's range here (+ down), from the whole-building cases.</summary>
        public double RoofWindMin { get; set; }
        public double RoofWindMax { get; set; }
        /// <summary>The roof wind here in each roof case (by its index in the uplift runs; + down).</summary>
        public List<double> RoofWinds { get; set; } = new List<double>();
        /// <summary>The lateral design's force here per case (+ down; its hold-down's uplift is negative).</summary>
        public Dictionary<string, double> Lateral { get; } = new Dictionary<string, double>();
        public List<CombinedLoad> Combined { get; } = new List<CombinedLoad>();
        public LoadEnvelope Envelope { get; set; }
    }

    /// <summary>A wall on the ground: the line loads along its footing and the points on it.</summary>
    public sealed class FoundationWall
    {
        public string Id { get; set; }
        public Point2 Start { get; set; }
        public Point2 End { get; set; }
        public double Length { get; set; }
        public List<FoundationSegment> Segments { get; } = new List<FoundationSegment>();
        public List<FoundationPoint> Points { get; } = new List<FoundationPoint>();
        /// <summary>Of the line load (plf) over its length.</summary>
        public LoadEnvelope Line { get; set; }
        public Point2 PointAt(double x) => Length <= 0 ? Start : new Point2(Start.X + (End.X - Start.X) * x / Length, Start.Y + (End.Y - Start.Y) * x / Length);

        /// <summary>The whole service load (lb): the line loads × their lengths and the points.</summary>
        public ServiceLoad Total()
        {
            // the wind case by case (the whole wall's in one case), its range from them
            var t = new ServiceLoad();
            var winds = new Dictionary<string, double>();
            void Add(Dictionary<string, double> w, double k)
            {
                foreach (var kv in w ?? new Dictionary<string, double>()) { winds.TryGetValue(kv.Key, out var v); winds[kv.Key] = v + kv.Value * k; }
            }
            foreach (var s in Segments)
            {
                t.Dead += s.Load.Dead * s.Length; t.Live += s.Load.Live * s.Length; t.RoofLive += s.Load.RoofLive * s.Length; t.Snow += s.Load.Snow * s.Length;
                Add(s.Load.Winds, s.Length);
            }
            foreach (var p in Points)
            {
                t.Dead += p.Load.Dead; t.Live += p.Load.Live; t.RoofLive += p.Load.RoofLive; t.Snow += p.Load.Snow;
                Add(p.Load.Winds, 1);
            }
            return t.WithWinds(winds);
        }
    }

    /// <summary>
    /// Everything coming down onto the foundation, combined: the gravity load path's walls on the ground (their line loads as they
    /// vary along them) and its columns and posts, the roof wind's whole-building cases carried down the same path, and the lateral
    /// design's hold-down and compression forces (<see cref="LateralSection"/>). Each in every load combination of the method that has
    /// only the loads the manager knows (D, L, Lr, S, W), with each variable load set to zero where that is worse; and the envelope:
    /// the most down and the most up, with the combination that gives each.
    /// </summary>
    public sealed class FoundationAnalysis
    {
        private const double Tol = 1e-6;
        /// <summary>A lateral force this close to a wall's footing (ft, across it) is on it; this close to a column or post, at it.</summary>
        public const double Near = 1.0;

        public DesignMethod Method { get; private set; }
        /// <summary>The code's combinations with the loads found here (their factors).</summary>
        public List<LoadCombination> Combinations { get; } = new List<LoadCombination>();
        /// <summary>The whole building's wind cases: the wind calculator's own, each once (no pairing).</summary>
        public List<WindCase> WindCases { get; } = new List<WindCase>();
        /// <summary>
        /// Every combination as investigated: one with W once for each wind case (the same case everywhere on the foundation),
        /// one without W once. Each place's <see cref="CombinedLoad"/> lists follow this order.
        /// </summary>
        public List<FoundationCase> Cases { get; } = new List<FoundationCase>();
        /// <summary>Combinations left out and why, in a sentence each.</summary>
        public List<string> Skipped { get; } = new List<string>();
        public List<FoundationWall> Walls { get; } = new List<FoundationWall>();
        /// <summary>Points not on a wall's footing: columns on their own pads, and lateral forces where no footing is drawn.</summary>
        public List<FoundationPoint> Points { get; } = new List<FoundationPoint>();
        public List<string> Notes { get; } = new List<string>();
        public IEnumerable<FoundationPoint> AllPoints => Walls.SelectMany(w => w.Points).Concat(Points);

        /// <param name="uplift">The roof wind down the load path (null or without cases: no roof wind).</param>
        /// <param name="lateral">The lateral design's forces (null: none).</param>
        public static FoundationAnalysis Run(Building b, LoadSection s, WindUplift uplift, LateralSection lateral, DesignMethod method)
        {
            var r = new FoundationAnalysis { Method = method };
            s = s ?? new LoadSection();
            var path = GravityLoadPath.Run(b, s);
            r.Notes.AddRange(path.Notes);
            var runs = uplift?.Runs.Select(x => x.Path).ToList() ?? new List<GravityLoadPath.Result>();
            var latPoints = (lateral?.Points ?? new List<LateralPoint>()).Where(p => p.Up != null && p.Up.Count > 0).ToList();
            var roofCases = uplift?.Runs.Select(x => x.Case).ToList() ?? new List<RoofWindCase>();
            WindCasesOf(r, roofCases, latPoints.Count == 0 ? null : lateral);
            var wcs = r.WindCases;

            // ---- the walls: the gravity profile cut wherever any wind case's changes
            foreach (var f in path.Foundation.Where(f => f.IsWall))
            {
                var w = b.Walls.FirstOrDefault(x => x.Id == f.Id);
                var fw = new FoundationWall { Id = f.Id, Start = w?.Start ?? f.Where, End = w?.End ?? f.Where, Length = f.Length };
                var winds = runs.Select(p => p.Foundation.FirstOrDefault(x => x.IsWall && x.Id == f.Id)).ToList();
                var cuts = new List<double> { 0, f.Length };
                cuts.AddRange(f.Profile.Steps.SelectMany(x => new[] { x.A, x.B }));
                foreach (var x in winds.Where(x => x != null)) cuts.AddRange(x.Profile.Steps.SelectMany(t => new[] { t.A, t.B }));
                cuts = cuts.Where(x => x >= -Tol && x <= f.Length + Tol).OrderBy(x => x).Aggregate(new List<double>(), (l, x) => { if (l.Count == 0 || x - l[l.Count - 1] > 1e-4) l.Add(x); return l; });
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    double m = (cuts[i] + cuts[i + 1]) / 2;
                    var g = ServiceLoad.Of(f.Profile.At(m));
                    var ws = winds.Select(x => x?.Profile.At(m).Wind ?? 0).ToList();
                    var load = wcs.Count == 0 ? g : g.WithWinds(wcs.ToDictionary(wc => wc.Name, wc => wc.Roof >= 0 ? ws[wc.Roof] : 0));
                    var last = fw.Segments.LastOrDefault();
                    if (load.IsEmpty) continue;
                    if (last != null && Math.Abs(last.B - cuts[i]) < 1e-4 && Same(last.Load, load)) { last.B = cuts[i + 1]; continue; }
                    fw.Segments.Add(new FoundationSegment { A = cuts[i], B = cuts[i + 1], Load = load });
                }
                foreach (var c in f.Points)
                {
                    var ws = winds.Select(x => x?.Points.FirstOrDefault(p => p.From == c.From)?.Load.Wind ?? 0).ToList();
                    fw.Points.Add(new FoundationPoint
                    {
                        From = c.From, Kind = FoundationPointKind.Post, WallId = f.Id, At = c.At, Where = fw.PointAt(c.At), Load = ServiceLoad.Of(c.Load),
                        RoofWindMin = ws.Count == 0 ? 0 : ws.Min(), RoofWindMax = ws.Count == 0 ? 0 : ws.Max(), RoofWinds = ws,
                    });
                }
                r.Walls.Add(fw);
            }

            // ---- columns on the ground
            foreach (var f in path.Foundation.Where(f => !f.IsWall))
            {
                var ws = runs.Select(p => p.Foundation.FirstOrDefault(x => !x.IsWall && x.Id == f.Id)?.Point.Wind ?? 0).ToList();
                var p0 = new FoundationPoint
                {
                    From = f.Id, Kind = FoundationPointKind.Column, Where = f.Where, Load = ServiceLoad.Of(f.Point),
                    RoofWindMin = ws.Count == 0 ? 0 : ws.Min(), RoofWindMax = ws.Count == 0 ? 0 : ws.Max(), RoofWinds = ws,
                };
                // a column standing in a wall's footing is on it
                var on = r.Walls.Select(x => (x, at: Along(x, f.Where), off: Across(x, f.Where))).Where(t => t.off <= Near && t.at >= -Near && t.at <= t.x.Length + Near).OrderBy(t => t.off).FirstOrDefault();
                if (on.x != null) { p0.WallId = on.x.Id; p0.At = Clamp(on.at, 0, on.x.Length); on.x.Points.Add(p0); }
                else r.Points.Add(p0);
            }

            // ---- the lateral design's forces: at a column or post there, else on a wall's footing, else on their own
            foreach (var lp in latPoints)
            {
                var all = r.AllPoints.ToList();
                var at = all.Where(p => Dist(p.Where, lp.Where) <= Near).OrderBy(p => Dist(p.Where, lp.Where)).FirstOrDefault();
                if (at == null)
                {
                    var on = r.Walls.Select(x => (x, a: Along(x, lp.Where), off: Across(x, lp.Where)))
                                    .Where(t => t.off <= Near && t.a >= -Near && t.a <= t.x.Length + Near)
                                    .OrderBy(t => t.x.Id == lp.WallId ? 0 : 1).ThenBy(t => t.off).FirstOrDefault();
                    at = new FoundationPoint { From = lp.From, Kind = FoundationPointKind.Lateral, Where = lp.Where, Load = new ServiceLoad() };
                    if (on.x != null) { at.WallId = on.x.Id; at.At = Clamp(on.a, 0, on.x.Length); at.Where = on.x.PointAt(at.At); on.x.Points.Add(at); }
                    else
                    {
                        r.Points.Add(at);
                        r.Notes.Add($"{lp.From}: no wall footing or column is drawn under it ({lp.X:0.##}, {lp.Y:0.##}); shown on its own.");
                    }
                }
                else at.From += " + " + lp.From;
                at.HasLateral = true;
                foreach (var kv in lp.Up)
                {
                    at.Lateral.TryGetValue(kv.Key, out var v);
                    at.Lateral[kv.Key] = v - kv.Value;   // + up in the section, + down here
                }
            }

            // ---- the points' wind, case by case: the roof's in that case's roof wind and the shear wall's in its own case
            foreach (var p in r.AllPoints)
            {
                if (wcs.Count == 0) continue;
                p.Load = p.Load.WithWinds(wcs.ToDictionary(wc => wc.Name, wc =>
                    (wc.Roof >= 0 && wc.Roof < p.RoofWinds.Count ? p.RoofWinds[wc.Roof] : 0) +
                    (wc.Lateral != null && p.Lateral.TryGetValue(wc.Lateral, out var v) ? v : 0)));
            }
            foreach (var w in r.Walls) w.Points.Sort((x, y) => x.At.CompareTo(y.At));

            // ---- the combinations: those with only D, L, Lr, S and W, and only those whose loads are somewhere here
            var present = new HashSet<LoadType> { LoadType.Dead };
            foreach (var l in r.Walls.SelectMany(w => w.Segments.Select(x => x.Load)).Concat(r.AllPoints.Select(p => p.Load)))
                foreach (LoadType t in Enum.GetValues(typeof(LoadType))) if (l.Has(t)) present.Add(t);
            var known = new HashSet<LoadType> { LoadType.Dead, LoadType.Live, LoadType.RoofLive, LoadType.Snow, LoadType.Wind };
            var skippedTypes = new SortedSet<string>();
            foreach (var c in method == DesignMethod.Asd ? LoadCombinations.Asd() : LoadCombinations.Asce7Strength())
            {
                var unknown = c.Types.Where(t => !known.Contains(t)).ToList();
                if (unknown.Count > 0) { foreach (var t in unknown) skippedTypes.Add(LoadCombinations.Symbol(t)); continue; }
                if (!c.Types.All(present.Contains)) continue;
                r.Combinations.Add(c);
            }
            if (skippedTypes.Count > 0)
                r.Skipped.Add($"Combinations with {string.Join(", ", skippedTypes)} are left out: the load manager has no such loads (rain, tornado and earthquake are not found here).");
            r.Skipped.Add("Combinations with a load that is nowhere on the foundation are left out (they give the same as one without it).");

            // ---- each combination with W once per wind case
            foreach (var c in r.Combinations)
            {
                if (c.Has(LoadType.Wind) && wcs.Count > 0) r.Cases.AddRange(wcs.Select(wc => new FoundationCase { Combination = c, Wind = wc }));
                else r.Cases.Add(new FoundationCase { Combination = c });
            }

            foreach (var w in r.Walls)
            {
                foreach (var sg in w.Segments) { Combine(r.Cases, sg.Load, sg.Combined); sg.Envelope = LoadEnvelope.Of(sg.Combined); }
                w.Line = LoadEnvelope.Over(w.Segments.Select(x => x.Envelope));
            }
            foreach (var p in r.AllPoints) { Combine(r.Cases, p.Load, p.Combined); p.Envelope = LoadEnvelope.Of(p.Combined); }
            return r;
        }

        /// <summary>
        /// The whole building's wind cases: the wind calculator's own (<see cref="RoofWind.Cases"/>), each once, under its name:
        /// its roof pressures down the load path, and the shear walls' hold-down forces the Shear Wall Calculator saved under the
        /// same name. Nothing is paired or made here. A saved shear wall case the calculator does not have now (an older wind
        /// setup) is left out, with a note. Without the wind calculator's cases (no wind setup), the shear walls' cases alone.
        /// </summary>
        private static void WindCasesOf(FoundationAnalysis r, List<RoofWindCase> roof, LateralSection lateral)
        {
            var lat = lateral?.Cases?.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList() ?? new List<string>();
            if (roof.Count == 0)
            {
                foreach (var l in lat) r.WindCases.Add(new WindCase { Name = l, Lateral = l });
                if (lat.Count > 0)
                    r.Notes.Add($"Wind: the wind calculator's cases are not available here (no roof wind), so the shear walls' {lat.Count} cases are taken alone, " +
                                "by their names (the wind calculator's); every combination with W taken in each one.");
                return;
            }
            // each wind once for every way the shear walls share it (rigid, flexible: LateralSection.Cases), the same roof
            // pressures in both, never mixed
            for (int i = 0; i < roof.Count; i++)
            {
                var mine = lat.Where(l => LateralSection.WindOf(l) == roof[i].Name).ToList();
                if (mine.Count == 0) mine.Add(null);
                foreach (var l in mine)
                    r.WindCases.Add(new WindCase { Name = l ?? roof[i].Name, Roof = i, RoofName = roof[i].Name, From = roof[i].From, Lateral = l });
            }
            var names = new HashSet<string>(roof.Select(x => x.Name));
            var missing = lat.Where(l => !names.Contains(LateralSection.WindOf(l))).ToList();
            r.Notes.Add($"Wind: the wind calculator's {roof.Count} wind cases, each on its own (its roof pressures down the load path" +
                        (lat.Count > missing.Count ? ", and the shear walls' hold-down forces in the same case, by name, once for each way they share it: the diaphragm rigid and flexible, kept apart" : "") +
                        "); every combination with W taken in each one.");
            if (missing.Count > 0)
                r.Notes.Add($"Wind: {missing.Count} of the {lat.Count} shear wall cases saved are not among the wind calculator's cases now (e.g. \"{missing[0]}\"), " +
                            "so their hold-down forces are left out. Save the shear wall design again so it matches the wind setup.");
        }

        private static void Combine(IEnumerable<FoundationCase> cs, ServiceLoad l, List<CombinedLoad> into)
        {
            foreach (var c in cs) into.Add(new CombinedLoad { Case = c, Max = l.Most(c), Min = l.Least(c) });
        }

        private static bool Same(ServiceLoad a, ServiceLoad b) =>
            Math.Abs(a.Dead - b.Dead) < Tol && Math.Abs(a.Live - b.Live) < Tol && Math.Abs(a.RoofLive - b.RoofLive) < Tol && Math.Abs(a.Snow - b.Snow) < Tol &&
            Math.Abs(a.WindMin - b.WindMin) < Tol && Math.Abs(a.WindMax - b.WindMax) < Tol &&
            a.Winds.Count == b.Winds.Count && a.Winds.All(kv => Math.Abs(kv.Value - b.Wind(kv.Key)) < Tol);

        private static double Along(FoundationWall w, Point2 p)
        {
            if (w.Length <= 0) return 0;
            return ((p.X - w.Start.X) * (w.End.X - w.Start.X) + (p.Y - w.Start.Y) * (w.End.Y - w.Start.Y)) / w.Length;
        }

        private static double Across(FoundationWall w, Point2 p)
        {
            if (w.Length <= 0) return Dist(p, w.Start);
            return Math.Abs((p.X - w.Start.X) * (w.End.Y - w.Start.Y) - (p.Y - w.Start.Y) * (w.End.X - w.Start.X)) / w.Length;
        }

        private static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;
        private static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}

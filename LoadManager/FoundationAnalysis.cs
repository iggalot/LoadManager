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

        public double Of(LoadType t) => t == LoadType.Dead ? Dead : t == LoadType.Live ? Live : t == LoadType.RoofLive ? RoofLive : t == LoadType.Snow ? Snow : 0;

        public bool Has(LoadType t) => t == LoadType.Dead || (t == LoadType.Wind ? Math.Abs(WindMin) > 1e-9 || Math.Abs(WindMax) > 1e-9 : Of(t) > 1e-9);

        public bool IsEmpty => !Has(LoadType.Live) && !Has(LoadType.RoofLive) && !Has(LoadType.Snow) && !Has(LoadType.Wind) && Math.Abs(Dead) < 1e-9;

        /// <summary>
        /// The most the combination gives (down): the dead load at its factor, each variable load where it adds to it or else set to
        /// zero ("each load combination shall be investigated with one or more of the variable loads set to zero").
        /// </summary>
        public double Most(LoadCombination c) => c.Terms.Sum(t => Term(t, true));

        /// <summary>The least (the most uplift): the dead load at its factor, each variable load where it takes away or else zero.</summary>
        public double Least(LoadCombination c) => c.Terms.Sum(t => Term(t, false));

        private double Term(LoadTerm t, bool most)
        {
            if (t.Type == LoadType.Dead) return t.Factor * Dead;
            double lo, hi;
            if (t.Type == LoadType.Wind) { lo = Math.Min(0, WindMin); hi = Math.Max(0, WindMax); }
            else { double v = Of(t.Type); lo = Math.Min(0, v); hi = Math.Max(0, v); }
            double a = t.Factor * lo, b = t.Factor * hi;
            return most ? Math.Max(a, b) : Math.Min(a, b);
        }

        public static ServiceLoad Of(GravityLoad g) => new ServiceLoad { Dead = g?.Dead ?? 0, Live = g?.Live ?? 0, RoofLive = g?.RoofLive ?? 0, Snow = g?.Snow ?? 0 };

        public ServiceLoad WithWind(double min, double max) => new ServiceLoad { Dead = Dead, Live = Live, RoofLive = RoofLive, Snow = Snow, WindMin = min, WindMax = max };

        public override string ToString() => $"D {Dead:0.##}, L {Live:0.##}, Lr {RoofLive:0.##}, S {Snow:0.##}" + (Has(LoadType.Wind) ? $", W {WindMin:0.##} to {WindMax:0.##}" : "");
    }

    /// <summary>One combination's range on a piece of foundation (+ down).</summary>
    public sealed class CombinedLoad
    {
        public LoadCombination Combination { get; set; }
        public double Max { get; set; }
        public double Min { get; set; }
    }

    /// <summary>The most down and the most up over every combination, each with the one giving it.</summary>
    public sealed class LoadEnvelope
    {
        public double Max { get; set; }
        public LoadCombination MaxBy { get; set; }
        public double Min { get; set; }
        public LoadCombination MinBy { get; set; }
        public bool Lifts => Min < -1e-9;

        public static LoadEnvelope Of(IEnumerable<CombinedLoad> all)
        {
            LoadEnvelope e = null;
            foreach (var c in all)
            {
                if (e == null) { e = new LoadEnvelope { Max = c.Max, MaxBy = c.Combination, Min = c.Min, MinBy = c.Combination }; continue; }
                if (c.Max > e.Max + 1e-9) { e.Max = c.Max; e.MaxBy = c.Combination; }
                if (c.Min < e.Min - 1e-9) { e.Min = c.Min; e.MinBy = c.Combination; }
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
            var t = new ServiceLoad();
            foreach (var s in Segments)
            {
                t.Dead += s.Load.Dead * s.Length; t.Live += s.Load.Live * s.Length; t.RoofLive += s.Load.RoofLive * s.Length; t.Snow += s.Load.Snow * s.Length;
                t.WindMin += s.Load.WindMin * s.Length; t.WindMax += s.Load.WindMax * s.Length;
            }
            foreach (var p in Points)
            {
                t.Dead += p.Load.Dead; t.Live += p.Load.Live; t.RoofLive += p.Load.RoofLive; t.Snow += p.Load.Snow;
                t.WindMin += p.Load.WindMin; t.WindMax += p.Load.WindMax;
            }
            return t;
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
        public List<LoadCombination> Combinations { get; } = new List<LoadCombination>();
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
                    var load = ws.Count == 0 ? g : g.WithWind(ws.Min(), ws.Max());
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
                        RoofWindMin = ws.Count == 0 ? 0 : ws.Min(), RoofWindMax = ws.Count == 0 ? 0 : ws.Max(),
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
                    RoofWindMin = ws.Count == 0 ? 0 : ws.Min(), RoofWindMax = ws.Count == 0 ? 0 : ws.Max(),
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

            // ---- the points' wind: the roof's range and the lateral's, their extremes taken together
            bool latOnly = false;
            foreach (var p in r.AllPoints)
            {
                double lo = p.RoofWindMin, hi = p.RoofWindMax;
                if (p.Lateral.Count > 0)
                {
                    lo += Math.Min(0, p.Lateral.Values.Min());
                    hi += Math.Max(0, p.Lateral.Values.Max());
                    latOnly = true;
                }
                p.Load = p.Load.WithWind(lo, hi);
            }
            if (latOnly && runs.Count > 0)
                r.Notes.Add("Where the roof's wind and a shear wall's end force meet, the most lifting of each are added (and the most pressing down of each), " +
                            "whatever their cases: the two designs name their cases differently. That is on the safe side.");
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

            foreach (var w in r.Walls)
            {
                foreach (var sg in w.Segments) { Combine(r.Combinations, sg.Load, sg.Combined); sg.Envelope = LoadEnvelope.Of(sg.Combined); }
                w.Line = LoadEnvelope.Over(w.Segments.Select(x => x.Envelope));
            }
            foreach (var p in r.AllPoints) { Combine(r.Combinations, p.Load, p.Combined); p.Envelope = LoadEnvelope.Of(p.Combined); }
            return r;
        }

        private static void Combine(IEnumerable<LoadCombination> cs, ServiceLoad l, List<CombinedLoad> into)
        {
            foreach (var c in cs) into.Add(new CombinedLoad { Combination = c, Max = l.Most(c), Min = l.Least(c) });
        }

        private static bool Same(ServiceLoad a, ServiceLoad b) =>
            Math.Abs(a.Dead - b.Dead) < Tol && Math.Abs(a.Live - b.Live) < Tol && Math.Abs(a.RoofLive - b.RoofLive) < Tol && Math.Abs(a.Snow - b.Snow) < Tol &&
            Math.Abs(a.WindMin - b.WindMin) < Tol && Math.Abs(a.WindMax - b.WindMax) < Tol;

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

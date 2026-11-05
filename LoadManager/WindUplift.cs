using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>The most net uplift on one thing over the wind cases: the load (dead and wind together) and the case it is in.</summary>
    public sealed class Governing<T> where T : GravityLoad
    {
        public T Load { get; set; }
        public RoofWindCase Case { get; set; }
        /// <summary>0.6D + 0.6W (negative: net up).</summary>
        public double Net => Load?.Net ?? 0;
        public bool Lifts => Net < -1e-9;
    }

    /// <summary>
    /// The rafter or truss tie on a wall or beam at the roof (components and cladding): the roof's dead load and the C&amp;C suction on
    /// the tributary strip (plf), and per rafter at the roof framing spacing.
    /// </summary>
    public sealed class RoofTie
    {
        public string Id { get; set; }
        public bool IsBeam { get; set; }
        /// <summary>The roof's share only (the wall's own weight is under the tie).</summary>
        public LineLoad Line { get; set; } = new LineLoad();
        /// <summary>The roof framing spacing (in).</summary>
        public double SpacingIn { get; set; }
        /// <summary>The rafters' span (ft), the load path's longest landing on it, and their effective wind area (sq ft).</summary>
        public double SpanFt { get; set; }
        public double Area { get; set; }
        /// <summary>The C&amp;C case at that area.</summary>
        public RoofWindCase Case { get; set; }
        /// <summary>The net per rafter (lb, 0.6D + 0.6W; negative: up).</summary>
        public double PerRafter => Line.Net * SpacingIn / 12.0;
    }

    /// <summary>
    /// Wind uplift, both ways ASCE 7 asks for: the whole-building (MWFRS) cases through the load path to the wall tops, beams, posts
    /// and foundation (<see cref="GravityLoadPath.Uplift"/>, one pass a case, the most net uplift of them kept on each thing), and
    /// the components and cladding (C&amp;C) case for the rafter or truss ties at the roof (a tie is a component: 26.2, Ch. 30).
    /// Both with 0.6D + 0.6W (ASCE 7-22 2.4 Eq. 7a), the dead load the only load resisting.
    /// </summary>
    public sealed class WindUplift
    {
        public List<(RoofWindCase Case, GravityLoadPath.Result Path)> Runs { get; } = new List<(RoofWindCase, GravityLoadPath.Result)>();
        public RoofWindCase Cladding { get; private set; }
        /// <summary>The ties at the roof by wall or beam ID.</summary>
        public Dictionary<string, RoofTie> Ties { get; } = new Dictionary<string, RoofTie>();
        /// <summary>The roof level (null when the building has none).</summary>
        public string RoofLevelId { get; private set; }

        public bool HasWind => Runs.Count > 0;

        /// <summary>A rafter's effective wind area (sq ft, ASCE 7 26.2): its span × the larger of its spacing and a third of its span.</summary>
        public static double RafterArea(double spanFt, double spacingIn) => spanFt > 0 ? spanFt * Math.Max(spacingIn / 12.0, spanFt / 3.0) : 0;

        /// <param name="cladding">The C&amp;C case at an effective wind area (sq ft): read at each rafter's (<see cref="RafterArea"/>).</param>
        public static WindUplift Run(Building b, LoadSection s, IEnumerable<RoofWindCase> mwfrs, Func<double, RoofWindCase> cladding)
        {
            var u = new WindUplift { RoofLevelId = b?.RoofLevel()?.Id };
            if (b == null) return u;
            // each case down the load path on its own: they share nothing but the (read-only) building and loads, so they run side
            // by side on the machine's cores, kept in the calculator's order
            var cases = (mwfrs ?? Enumerable.Empty<RoofWindCase>()).ToList();
            var paths = new GravityLoadPath.Result[cases.Count];
            try { System.Threading.Tasks.Parallel.For(0, cases.Count, i => paths[i] = GravityLoadPath.Uplift(b, s, cases[i])); }
            catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
            {
                // the first case's own exception, as a sequential run would have thrown it
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
            }
            for (int i = 0; i < cases.Count; i++) u.Runs.Add((cases[i], paths[i]));
            if (cladding == null || u.RoofLevelId == null) return u;

            // the rafters landing on each wall or beam at the roof, their span from the load path; one pass per area
            double spacing = b.Defaults?.RafterSpacing > 0 ? b.Defaults.RafterSpacing : 24;
            var plain = u.Runs.Count > 0 ? u.Runs[0].Path : GravityLoadPath.Uplift(b, s, null);
            var at = plain.Values.Where(x => x.LevelId == u.RoofLevelId && x.TributaryMax > 0).Select(x => (id: x.WallId, beam: false, span: x.FramingSpan))
                          .Concat(plain.Beams.Values.Where(x => x.LevelId == u.RoofLevelId && x.TributaryMax > 0).Select(x => (id: x.BeamId, beam: true, span: x.FramingSpan)))
                          .Select(x => (x.id, x.beam, x.span, area: Math.Round(RafterArea(x.span, spacing)))).Where(x => x.area > 0).ToList();
            foreach (var group in at.GroupBy(x => x.area))
            {
                var c = cladding(group.Key);
                if (c == null) continue;
                u.Cladding = u.Cladding ?? c;
                var r = GravityLoadPath.Uplift(b, s, c);
                foreach (var x in group)
                {
                    var line = x.beam ? (r.Beams.TryGetValue(x.id, out var m) ? m.FromLevel : null)
                                      : (r.TryGetValue(GravityLoadPath.Key(x.id, u.RoofLevelId), out var w) ? w.FromLevel : null);
                    if (line != null) u.Ties[x.id] = new RoofTie { Id = x.id, IsBeam = x.beam, Line = line, SpacingIn = spacing, SpanFt = x.span, Area = x.area, Case = c };
                }
            }
            return u;
        }

        private Governing<T> Worst<T>(Func<GravityLoadPath.Result, T> pick) where T : GravityLoad
        {
            Governing<T> best = null;
            foreach (var (c, path) in Runs)
            {
                var load = pick(path);
                if (load == null) continue;
                if (best == null || load.Net < best.Net - 1e-9) best = new Governing<T> { Load = load, Case = c };
            }
            return best;
        }

        /// <summary>A wall's top on a story (<see cref="GravityLoadPath.Key"/>).</summary>
        public Governing<LineLoad> WallTop(string key) => Worst(p => p.TryGetValue(key, out var w) ? w.Load : null);

        /// <summary>A beam's start or end reaction.</summary>
        public Governing<PointLoad> BeamEnd(string beamId, bool start) =>
            Worst(p => p.Beams.TryGetValue(beamId, out var m) ? start ? m.StartReaction : m.EndReaction : null);

        /// <summary>What a column carries down to its base.</summary>
        public Governing<PointLoad> Column(string columnId) => Worst(p => p.Columns.TryGetValue(columnId, out var c) ? c.Load : null);

        /// <summary>A wall's line load at its base on the foundation.</summary>
        public Governing<LineLoad> FoundationWall(string wallId) => Worst(p => p.Foundation.FirstOrDefault(f => f.IsWall && f.Id == wallId)?.Line);

        /// <summary>A concentrated load in a wall (a post), by what brings it (<see cref="ConcentratedLoad.From"/>), on a wall's top.</summary>
        public Governing<PointLoad> Post(string key, string from) =>
            Worst(p => p.TryGetValue(key, out var w) ? w.Points.FirstOrDefault(x => x.From == from)?.Load : null);

        /// <summary>A post in a wall on the foundation.</summary>
        public Governing<PointLoad> FoundationPost(string wallId, string from) =>
            Worst(p => p.Foundation.FirstOrDefault(f => f.IsWall && f.Id == wallId)?.Points.FirstOrDefault(x => x.From == from)?.Load);
    }
}

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
    /// The rafter or truss tie on a wall or beam at the roof: the roof's dead load and the wind's suction on the tributary strip
    /// (plf), and per rafter at the roof framing spacing. The owner's rule (2026-11-07): the components and cladding uplift
    /// (C&amp;C: a tie is a component, 26.2, Ch. 30) where it is higher than the whole-building (MWFRS) uplift in the same place,
    /// else the MWFRS: the worst case. Both are kept, and which one governs.
    /// </summary>
    public sealed class RoofTie
    {
        public string Id { get; set; }
        public bool IsBeam { get; set; }
        /// <summary>The governing of the two (<see cref="Cladding"/>, <see cref="Mwfrs"/>): the roof's share only (the wall's own weight is under the tie).</summary>
        public LineLoad Line { get; set; } = new LineLoad();
        /// <summary>The C&amp;C at the rafters' area and its case (null: none found).</summary>
        public LineLoad Cladding { get; set; }
        public RoofWindCase CladdingCase { get; set; }
        /// <summary>The MWFRS case lifting it most and its load (null: no whole-building cases).</summary>
        public LineLoad Mwfrs { get; set; }
        public RoofWindCase MwfrsCase { get; set; }
        /// <summary>The C&amp;C governs (its net uplift at least the MWFRS's); else the MWFRS.</summary>
        public bool CladdingGoverns { get; set; }
        public string Governs => CladdingGoverns ? "C&C" : "MWFRS";
        /// <summary>The roof framing spacing (in).</summary>
        public double SpacingIn { get; set; }
        /// <summary>The rafters' span (ft), the load path's longest landing on it, and their effective wind area (sq ft).</summary>
        public double SpanFt { get; set; }
        public double Area { get; set; }
        /// <summary>The governing case (<see cref="CladdingCase"/> or <see cref="MwfrsCase"/>).</summary>
        public RoofWindCase Case { get; set; }
        /// <summary>The net per rafter (lb, 0.6D + 0.6W; negative: up).</summary>
        public double PerRafter => Line.Net * SpacingIn / 12.0;

        /// <summary>
        /// The tie place by place along its wall or beam (ft from its start): where the C&amp;C zones or the MWFRS case lifting it
        /// most change, the worse of the two there (owner: the C&amp;C where it is higher). <see cref="Line"/> is the worst of them.
        /// </summary>
        public List<RoofTieStep> Along { get; } = new List<RoofTieStep>();
    }

    /// <summary>One stretch of a roof tie (<see cref="RoofTie.Along"/>): its net line load (0.6D + 0.6W, the roof's share), which governs and its case.</summary>
    public sealed class RoofTieStep
    {
        public double A { get; set; }
        public double B { get; set; }
        public LineLoad Line { get; set; } = new LineLoad();
        public bool CladdingGoverns { get; set; }
        public string Governs => CladdingGoverns ? "C&C" : "MWFRS";
        public RoofWindCase Case { get; set; }
        public double Length => B - A;
        /// <summary>The net per rafter here (lb; negative: up).</summary>
        public double PerRafter(double spacingIn) => Line.Net * spacingIn / 12.0;
        public override string ToString() => $"{A:0.##} to {B:0.##} ft: {Line.Net:0} plf ({Governs})";
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
            if (u.RoofLevelId == null) return u;

            // the rafters landing on each wall or beam at the roof, their span from the load path; one pass per area
            double spacing = b.Defaults?.RafterSpacing > 0 ? b.Defaults.RafterSpacing : 24;
            var plain = u.Runs.Count > 0 ? u.Runs[0].Path : GravityLoadPath.Uplift(b, s, null);
            var at = plain.Values.Where(x => x.LevelId == u.RoofLevelId && x.TributaryMax > 0).Select(x => (id: x.WallId, beam: false, span: x.FramingSpan))
                          .Concat(plain.Beams.Values.Where(x => x.LevelId == u.RoofLevelId && x.TributaryMax > 0).Select(x => (id: x.BeamId, beam: true, span: x.FramingSpan)))
                          .Select(x => (x.id, x.beam, x.span, area: Math.Round(RafterArea(x.span, spacing)))).Where(x => x.area > 0).ToList();
            var claddingAlong = new Dictionary<string, LoadProfile>();
            foreach (var group in at.GroupBy(x => x.area).Where(g => cladding != null))
            {
                var c = cladding(group.Key);
                if (c == null) continue;
                u.Cladding = u.Cladding ?? c;
                var r = GravityLoadPath.Uplift(b, s, c);
                foreach (var x in group)
                {
                    var line = x.beam ? (r.Beams.TryGetValue(x.id, out var m) ? m.FromLevel : null)
                                      : (r.TryGetValue(GravityLoadPath.Key(x.id, u.RoofLevelId), out var w) ? w.FromLevel : null);
                    if (line != null) u.Ties[x.id] = new RoofTie { Id = x.id, IsBeam = x.beam, SpacingIn = spacing, SpanFt = x.span, Area = x.area, Cladding = line, CladdingCase = c };
                    var prof = Profile(r, x.id, x.beam, u.RoofLevelId);
                    if (line != null && prof != null) claddingAlong[x.id] = prof;
                }
            }
            // the MWFRS at each tie, the case lifting it most there; the tie takes the worse of the two (owner)
            foreach (var x in at)
            {
                var m = u.Worst(p => x.beam ? (p.Beams.TryGetValue(x.id, out var bm) ? bm.FromLevel : null)
                                            : (p.TryGetValue(GravityLoadPath.Key(x.id, u.RoofLevelId), out var w) ? w.FromLevel : null));
                if (!u.Ties.TryGetValue(x.id, out var t))
                {
                    if (m?.Load == null) continue;
                    u.Ties[x.id] = t = new RoofTie { Id = x.id, IsBeam = x.beam, SpacingIn = spacing, SpanFt = x.span, Area = x.area };
                }
                t.Mwfrs = m?.Load;
                t.MwfrsCase = m?.Case;
                t.CladdingGoverns = t.Cladding != null && (t.Mwfrs == null || t.Cladding.Net <= t.Mwfrs.Net + 1e-9);
                t.Line = t.CladdingGoverns ? t.Cladding : t.Mwfrs;
                t.Case = t.CladdingGoverns ? t.CladdingCase : t.MwfrsCase;
            }
            // place by place along each: the C&C there against every MWFRS case there
            foreach (var t in u.Ties.Values)
                FillAlong(t, claddingAlong.TryGetValue(t.Id, out var cp) ? (cp, t.CladdingCase) : ((LoadProfile, RoofWindCase)?)null,
                          u.Runs.Select(p => (Profile(p.Path, t.Id, t.IsBeam, u.RoofLevelId), p.Case)).Where(p => p.Item1 != null).ToList());
            return u;
        }

        /// <summary>The roof's share along a wall's top or a beam in one pass (null: none, or no profile kept).</summary>
        private static LoadProfile Profile(GravityLoadPath.Result path, string id, bool beam, string roofLevelId)
        {
            var p = beam ? (path.Beams.TryGetValue(id, out var m) ? m.FromLevelProfile : null)
                         : (path.TryGetValue(GravityLoadPath.Key(id, roofLevelId), out var w) ? w.FromLevelProfile : null);
            return p == null || p.IsEmpty ? null : p;
        }

        /// <summary>
        /// The tie's stretches (<see cref="RoofTie.Along"/>): cut wherever any of the loads changes; in each, the MWFRS case
        /// lifting it most there, and the C&amp;C where it is at least as high (the owner's rule, as for the whole tie).
        /// </summary>
        private static void FillAlong(RoofTie t, (LoadProfile profile, RoofWindCase c)? cladding, List<(LoadProfile profile, RoofWindCase c)> mwfrs)
        {
            const double tol = 1e-6;
            var all = mwfrs.Select(m => m.profile).Concat(cladding.HasValue ? new[] { cladding.Value.profile } : new LoadProfile[0]).ToList();
            var cuts = all.SelectMany(p => p.Steps.SelectMany(s => new[] { s.A, s.B })).OrderBy(x => x)
                          .Aggregate(new List<double>(), (l, x) => { if (l.Count == 0 || x - l[l.Count - 1] > tol) l.Add(x); return l; });
            LineLoad Here(LoadProfile p, double x) => p.Steps.FirstOrDefault(s => x >= s.A - tol && x <= s.B + tol)?.Load;
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], b = cuts[i + 1], mid = (a + b) / 2;
                LineLoad mw = null; RoofWindCase mc = null;
                foreach (var m in mwfrs)
                {
                    var l = Here(m.profile, mid);
                    if (l != null && (mw == null || l.Net < mw.Net - 1e-9)) { mw = l; mc = m.c; }
                }
                var cc = cladding.HasValue ? Here(cladding.Value.profile, mid) : null;
                if (mw == null && cc == null) continue;
                bool cg = cc != null && (mw == null || cc.Net <= mw.Net + 1e-9);
                var line = cg ? cc : mw;
                var c = cg ? cladding.Value.c : mc;
                var last = t.Along.LastOrDefault();
                if (last != null && Math.Abs(last.B - a) < tol && last.CladdingGoverns == cg && last.Case == c && Math.Abs(last.Line.Net - line.Net) < 1e-6) { last.B = b; continue; }
                t.Along.Add(new RoofTieStep { A = a, B = b, Line = line, CladdingGoverns = cg, Case = c });
            }
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

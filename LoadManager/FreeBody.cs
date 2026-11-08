using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>The four kinds of force on a free-body diagram (WISE).</summary>
    public enum FreeBodyPart
    {
        /// <summary>W: the body's own weight.</summary>
        Weight,
        /// <summary>
        /// I: the internal forces where the body is cut from the rest of the structure: the reactions of the members
        /// connected to it that are not in the body (a wall under it, a wall standing on it, a beam end landing on it).
        /// </summary>
        Internal,
        /// <summary>S: the support reactions from outside the structure: the foundation.</summary>
        Support,
        /// <summary>E: the external loads applied to it: the floors' and roof's loads, the wind.</summary>
        External,
    }

    /// <summary>One gravity force on a free body: a line load over a stretch of a member or a point load, at a height, service level.</summary>
    public sealed class FreeBodyForce
    {
        public FreeBodyPart Part { get; set; }
        /// <summary>What it is: "Level 2 floor", "BM1 via W7", "own weight", "W1 (Level 1) under it".</summary>
        public string What { get; set; }
        /// <summary>The member it acts on.</summary>
        public string Member { get; set; }
        /// <summary>
        /// The member at its other end (the wall or beam under it, the wall standing on it, the beam whose end lands on it):
        /// the force is that member's reaction on this one, and this one's on it is the same the other way. Null: an applied
        /// load (a floor, the roof, a load entered by hand), the weight, or the foundation.
        /// </summary>
        public string Other { get; set; }
        /// <summary>Both its members are in the body: half of a pair inside it, cancelled by the other half (not in the sums).</summary>
        public bool Inside { get; set; }
        /// <summary>The elevation it acts at (ft).</summary>
        public double Z { get; set; }
        /// <summary>From and to along its member from its start (ft); equal for a point load.</summary>
        public double A { get; set; }
        public double B { get; set; }
        public bool IsLine => B - A > 1e-9;
        /// <summary>The service loads by kind: plf for a line load, lb for a point load.</summary>
        public ServiceLoad Load { get; set; } = new ServiceLoad();
        /// <summary>It acts upward (what holds the member up); else downward.</summary>
        public bool Up { get; set; }

        /// <summary>
        /// Its size in a combination (plf or lb), the factors as written: the gravity loads (D, L, Lr, S) and, on a free body
        /// in one wind case (<see cref="FreeBody.WindCase"/>), the roof's wind in it (W: <see cref="ServiceLoad.Winds"/>). The
        /// service loads (no combination): the gravity loads only.
        /// </summary>
        public double Value(LoadCombination c) =>
            c == null ? Load.Dead + Load.Live + Load.RoofLive + Load.Snow
            : c.Factor(LoadType.Dead) * Load.Dead + c.Factor(LoadType.Live) * Load.Live + c.Factor(LoadType.RoofLive) * Load.RoofLive + c.Factor(LoadType.Snow) * Load.Snow
              + (Load.Winds == null || Load.Winds.Count == 0 ? 0 : c.Factor(LoadType.Wind) * Load.Winds.Values.Sum());

        /// <summary>The roof's wind in it (W, one wind case: <see cref="FreeBody.WindCase"/>), not the gravity loads.</summary>
        public bool IsWind => Load.Winds != null && Load.Winds.Count > 0;

        /// <summary>Its resultant in a combination (lb), + in the way it acts.</summary>
        public double Resultant(LoadCombination c) => Value(c) * (IsLine ? B - A : 1);
    }

    /// <summary>A member's half of one connection pair (<see cref="ConnectionPair"/>): the force on it, per wind case.</summary>
    public sealed class FreeBodyConnection
    {
        public ConnectionPair Pair { get; set; }
        /// <summary>Its member's own (it hangs from it, at its base); else one of a member standing on it.</summary>
        public bool Own { get; set; }
        public FreeBodyPart Part { get; set; }
        public string What { get; set; }
        /// <summary>The member it acts on, and the member at the pair's other end (null: the foundation, or nothing).</summary>
        public string Member { get; set; }
        public string Other { get; set; }
        /// <summary>Both its members are in the body: half of a pair inside it (not in the sums).</summary>
        public bool Inside { get; set; }
        /// <summary>The elevation it acts at (ft) and where along its member (ft from its start): at the hold-down when it pulls.</summary>
        public double Z { get; set; }
        public double A { get; set; }
        /// <summary>Where along its member it acts when it presses (ft): the end post's centreline.</summary>
        public double ACompression { get; set; }

        /// <summary>The force on its member in one wind case (lb, + up).</summary>
        public double On(string windCase) => Own ? Pair.OnUpper(windCase) : Pair.OnLower(windCase);

        /// <summary>Where along its member it acts in one wind case (ft from its start).</summary>
        public double AlongIn(string windCase) => Pair.Of(windCase) < 0 ? ACompression : A;

        /// <summary>Where on plan it acts in one wind case.</summary>
        public Point2 WhereIn(string windCase) => Pair.WhereIn(windCase);
    }

    /// <summary>
    /// The wind's shear on a wall, per wind case: on its top from its diaphragm (E, the wind), and the sill's under it (S on
    /// the foundation; I on a floor, the framing it is cut from). Horizontal, along the wall's line.
    /// </summary>
    public sealed class FreeBodyShear
    {
        public FreeBodyPart Part { get; set; }
        public string What { get; set; }
        public string Member { get; set; }
        /// <summary>The elevation it acts at (ft).</summary>
        public double Z { get; set; }
        /// <summary>A point on its line of action on plan (its wall's start) and its + direction (its wall's, start to end).</summary>
        public Point2 Origin { get; set; }
        public Point2 Dir { get; set; }
        /// <summary>Wind case → the force (lb, W at strength level), + along its wall from its start to its end.</summary>
        public Dictionary<string, double> ByCase { get; set; } = new Dictionary<string, double>();
        public double Of(string windCase) => windCase != null && ByCase.TryGetValue(windCase, out var v) ? v : 0;
    }

    /// <summary>One member of a free body: where it is.</summary>
    public sealed class FreeBodyMember
    {
        public string Id { get; set; }
        /// <summary>"wall", "beam" or "column".</summary>
        public string Kind { get; set; }
        public Point2 Start { get; set; }
        public Point2 End { get; set; }
        /// <summary>Its length (ft); 0 for a column.</summary>
        public double Length { get; set; }
        public double Bottom { get; set; }
        public double Top { get; set; }

        /// <summary>The point on plan <paramref name="along"/> ft from its start.</summary>
        public Point2 At(double along) => Length <= 1e-9 ? Start
            : new Point2(Start.X + (End.X - Start.X) * along / Length, Start.Y + (End.Y - Start.Y) * along / Length);
    }

    /// <summary>
    /// A free-body diagram of the selected members (one wall, beam or column, or several together), by WISE for the gravity
    /// loads and the wind alike (the owner's rule: it applies to every body, a member or a whole system). W: its weight.
    /// E: the loads applied to it (the floors' and roof's, the wind). S: the foundation's reactions. I: where it is cut from
    /// the rest of the structure, the reactions of the members connected to it that are not selected. A connection between
    /// two selected members is a pair inside the body: its two halves cancel and are left out of the sums (they are kept,
    /// <see cref="FreeBodyForce.Inside"/>, for a drawing that shows them as the check). The gravity forces keep their service
    /// loads by kind, so the diagram stays in equilibrium in every combination; the wind's (<see cref="Connections"/>,
    /// <see cref="Shears"/>) are per wind case, W at strength level.
    /// </summary>
    public sealed class FreeBody
    {
        /// <summary>The member's ID, or the members' joined ("W1 + W2").</summary>
        public string Id { get; set; }
        /// <summary>"wall", "beam" or "column" for one member; "body" for several.</summary>
        public string Kind { get; set; }
        /// <summary>One member's geometry (the first's, for several): its length along its start-to-end (ft), 0 for a column.</summary>
        public double Length { get; set; }
        public Point2 Start { get; set; }
        public Point2 End { get; set; }
        /// <summary>Its bottom and top (ft): the lowest and highest of its members.</summary>
        public double Bottom { get; set; }
        public double Top { get; set; }
        public List<FreeBodyMember> Members { get; } = new List<FreeBodyMember>();
        public List<FreeBodyForce> Forces { get; } = new List<FreeBodyForce>();
        public List<string> Notes { get; } = new List<string>();

        /// <summary>The lateral design's connections on its members, each its half of an equal and opposite pair.</summary>
        public List<FreeBodyConnection> Connections { get; } = new List<FreeBodyConnection>();

        /// <summary>The wind's shear on its walls (E, at the top of each story it is a shear wall on) and their sills'.</summary>
        public List<FreeBodyShear> Shears { get; } = new List<FreeBodyShear>();

        public FreeBodyMember MemberOf(string id) => Members.FirstOrDefault(m => m.Id == id) ?? Members.FirstOrDefault();

        // ---------------------------------------------------------------- the gravity loads

        /// <summary>Its forces of one part (those inside the body left out).</summary>
        public IEnumerable<FreeBodyForce> Of(FreeBodyPart part) => Forces.Where(f => !f.Inside && f.Part == part);

        /// <summary>The pairs inside it (both halves), for a drawing that shows them.</summary>
        public IEnumerable<FreeBodyForce> InsideForces => Forces.Where(f => f.Inside);

        /// <summary>
        /// The sum of the vertical forces in a combination (lb, + down): 0 in equilibrium. In a wind case
        /// (<see cref="WindCase"/>) and a combination with W: the roof's wind too, and the shear walls' connections in that case
        /// at the combination's W factor (<see cref="ConnectionsUp"/>).
        /// </summary>
        public double Imbalance(LoadCombination c) => Forces.Where(f => !f.Inside).Sum(f => f.Up ? -f.Resultant(c) : f.Resultant(c)) - ConnectionsUp(c);

        /// <summary>
        /// The wind case the free body is in (a shear wall case, "… (rigid)" or "… (flexible)", or the wind calculator's own),
        /// null: the gravity loads alone. Its roof's wind is in the forces (<see cref="FreeBodyForce.IsWind"/>), its shear
        /// walls' connections in <see cref="Connections"/>; the combinations with W take both.
        /// </summary>
        public string WindCase { get; set; }

        /// <summary>The shear walls' connections on it (not those inside), in <see cref="WindCase"/> at the combination's W factor (lb, + up); 0 with no case or no W.</summary>
        public double ConnectionsUp(LoadCombination c) =>
            c == null || WindCase == null ? 0 : c.Factor(LoadType.Wind) * Connections.Where(k => !k.Inside).Sum(k => k.On(WindCase));

        /// <summary>The total of one part in a combination (lb, + the way its forces act).</summary>
        public double Total(FreeBodyPart part, LoadCombination c) => Of(part).Sum(f => f.Resultant(c));

        /// <summary>What holds it up in a combination (lb): the foundation's and the cut's upward forces.</summary>
        public double HeldUp(LoadCombination c) => Forces.Where(f => !f.Inside && f.Up).Sum(f => f.Resultant(c));

        /// <summary>Every gravity load kind on it.</summary>
        public ServiceLoad Kinds()
        {
            var s = new ServiceLoad();
            foreach (var f in Forces.Where(f => !f.Inside && !f.Up))
            {
                double k = f.IsLine ? f.B - f.A : 1;
                s.Dead += f.Load.Dead * k; s.Live += f.Load.Live * k; s.RoofLive += f.Load.RoofLive * k; s.Snow += f.Load.Snow * k;
            }
            return s;
        }

        /// <summary>
        /// The combinations of the method made only of D, L, Lr and S whose loads are on it, and in a wind case
        /// (<see cref="WindCase"/>) those with W too when the wind is on it (the roof's or the shear walls'), the one holding it
        /// up most first.
        /// </summary>
        public List<LoadCombination> Combinations(DesignMethod method)
        {
            var k = Kinds();
            var allowed = new HashSet<LoadType> { LoadType.Dead, LoadType.Live, LoadType.RoofLive, LoadType.Snow };
            bool wind = WindCase != null && (Forces.Any(f => !f.Inside && f.IsWind) || Connections.Any(x => !x.Inside && Math.Abs(x.On(WindCase)) > 1e-9));
            if (wind) allowed.Add(LoadType.Wind);
            var list = (method == DesignMethod.Asd ? LoadCombinations.Asd() : LoadCombinations.Asce7Strength())
                .Where(c => c.Types.All(allowed.Contains) && c.Types.All(t => t == LoadType.Wind || k.Has(t)))
                .ToList();
            return list.OrderByDescending(HeldUp).ToList();
        }

        /// <summary>Where on plan a gravity force acts: a step of a line load (uniform over it) at its middle, a point load at its place.</summary>
        public Point2 WhereOf(FreeBodyForce f)
        {
            var m = MemberOf(f.Member);
            if (m == null) return new Point2(0, 0);
            return m.Kind == "column" ? m.Start : m.At(f.IsLine ? (f.A + f.B) / 2 : f.A);
        }

        /// <summary>
        /// ΣM of the vertical forces in a combination about <see cref="Centre"/> (lb·ft, about x east and y north; vertical
        /// forces give none about z): each load at its own place (a line load step by step). In a combination with W and a
        /// wind case, the shear walls' forces too (<see cref="WindMoment"/> at the combination's W factor): the wind's shear on
        /// top against its connections.
        /// </summary>
        public (double x, double y, double z) Moment(LoadCombination c)
        {
            var o = Centre;
            double mx = 0, my = 0, mz = 0;
            foreach (var f in Forces.Where(f => !f.Inside))
            {
                var p = WhereOf(f);
                double fz = f.Up ? f.Resultant(c) : -f.Resultant(c);
                mx += (p.Y - o.y) * fz;
                my -= (p.X - o.x) * fz;
            }
            if (c != null && WindCase != null && c.Has(LoadType.Wind))
            {
                double k = c.Factor(LoadType.Wind);
                var w = WindMoment(WindCase);
                mx += k * w.x; my += k * w.y; mz += k * w.z;
            }
            return (mx, my, mz);
        }

        /// <summary>The size of the moments in a combination (lb·ft): Σ |force| × its distance from <see cref="Centre"/>, for the tolerance.</summary>
        public double MomentScale(LoadCombination c)
        {
            var o = Centre;
            double s = Forces.Where(f => !f.Inside).Sum(f => { var p = WhereOf(f); return Math.Abs(f.Resultant(c)) * Math.Sqrt((p.X - o.x) * (p.X - o.x) + (p.Y - o.y) * (p.Y - o.y)); });
            if (c != null && WindCase != null && c.Has(LoadType.Wind)) s += Math.Abs(c.Factor(LoadType.Wind)) * WindMomentScale(WindCase);
            return s;
        }

        /// <summary>
        /// Why it is not in equilibrium in a combination (null: it is): ΣFz over 0.5 % of its largest force (at least 1 lb), or
        /// ΣM over 0.5 % of <see cref="MomentScale"/> (at least 10 lb·ft), the wind's rule (<see cref="WindOutOfBalance"/>).
        /// </summary>
        public string OutOfBalance(LoadCombination c)
        {
            double big = Forces.Where(f => !f.Inside).Select(f => Math.Abs(f.Resultant(c))).DefaultIfEmpty(0).Max();
            double fTol = Math.Max(1.0, 0.005 * big), mTol = Math.Max(10.0, 0.005 * MomentScale(c));
            double fz = Imbalance(c);
            if (Math.Abs(fz) > fTol) return $"ΣFz = {fz:+#,0;-#,0} lb in {c?.Name ?? "the service loads"}";
            var m = Moment(c);
            double mm = Math.Sqrt(m.x * m.x + m.y * m.y + m.z * m.z);
            if (mm > mTol) return $"ΣM = {mm:#,0} lb·ft in {c?.Name ?? "the service loads"} (of {MomentScale(c):N0})";
            return null;
        }

        // ---------------------------------------------------------------- the wind

        /// <summary>The wind cases on it.</summary>
        public List<string> WindCases => Connections.SelectMany(c => c.Pair.Force.Keys).Concat(Shears.SelectMany(s => s.ByCase.Keys)).Distinct().ToList();

        private IEnumerable<FreeBodyConnection> WindVertical => Connections.Where(c => !c.Inside);
        private IEnumerable<FreeBodyShear> WindHorizontal => Shears;

        /// <summary>The point the moments are taken about: the middle of its members on plan, at its bottom.</summary>
        public (double x, double y, double z) Centre
        {
            get
            {
                var pts = Members.SelectMany(m => new[] { m.Start, m.End }).ToList();
                return pts.Count == 0 ? (0, 0, Bottom) : (pts.Average(p => p.X), pts.Average(p => p.Y), Bottom);
            }
        }

        /// <summary>The wind's ΣF in one case (lb, x east, y north, z up).</summary>
        public (double x, double y, double z) WindForce(string windCase)
        {
            double fx = 0, fy = 0, fz = 0;
            foreach (var c in WindVertical) fz += c.On(windCase);
            foreach (var s in WindHorizontal) { double h = s.Of(windCase); fx += h * s.Dir.X; fy += h * s.Dir.Y; }
            return (fx, fy, fz);
        }

        /// <summary>The wind's ΣM in one case about <see cref="Centre"/> (lb·ft, about x east, y north, z up).</summary>
        public (double x, double y, double z) WindMoment(string windCase)
        {
            var o = Centre;
            double mx = 0, my = 0, mz = 0;
            foreach (var c in WindVertical)
            {
                var p = c.WhereIn(windCase);
                double fz = c.On(windCase), rx = p.X - o.x, ry = p.Y - o.y;
                mx += ry * fz; my -= rx * fz;
            }
            foreach (var s in WindHorizontal)
            {
                double h = s.Of(windCase), fx = h * s.Dir.X, fy = h * s.Dir.Y;
                double rx = s.Origin.X - o.x, ry = s.Origin.Y - o.y, rz = s.Z - o.z;
                mx -= rz * fy; my += rz * fx; mz += rx * fy - ry * fx;
            }
            return (mx, my, mz);
        }

        /// <summary>ΣFz in one wind case (lb, + up): 0 when every vertical force on it is accounted for.</summary>
        public double WindImbalance(string windCase) => WindForce(windCase).z;

        /// <summary>The horizontal ΣF in one wind case (lb, its size).</summary>
        public double WindShearImbalance(string windCase) { var f = WindForce(windCase); return Math.Sqrt(f.x * f.x + f.y * f.y); }

        /// <summary>The ΣM in one wind case (lb·ft, its size).</summary>
        public double WindMomentImbalance(string windCase) { var m = WindMoment(windCase); return Math.Sqrt(m.x * m.x + m.y * m.y + m.z * m.z); }

        /// <summary>A scale for the moment check in one case (lb·ft): each force times its distance from <see cref="Centre"/>.</summary>
        public double WindMomentScale(string windCase)
        {
            var o = Centre;
            double Dist(double x, double y, double z) => Math.Sqrt((x - o.x) * (x - o.x) + (y - o.y) * (y - o.y) + (z - o.z) * (z - o.z));
            return WindVertical.Sum(c => { var p = c.WhereIn(windCase); return Math.Abs(c.On(windCase)) * Dist(p.X, p.Y, c.Z); })
                 + WindHorizontal.Sum(s => Math.Abs(s.Of(windCase)) * Dist(s.Origin.X, s.Origin.Y, s.Z));
        }

        /// <summary>
        /// Why it does not balance the wind in one case (null: it does): ΣFz or the horizontal ΣF over 0.5 % of its largest
        /// force (at least 1 lb), or ΣM over 0.5 % of <see cref="WindMomentScale"/> (at least 10 lb·ft): rounding aside.
        /// </summary>
        public string WindOutOfBalance(string windCase)
        {
            double f = Math.Max(WindVertical.Select(x => Math.Max(x.Pair.MaxTension, x.Pair.MaxCompression)).DefaultIfEmpty(0).Max(),
                                Shears.SelectMany(s => s.ByCase.Values).Select(Math.Abs).DefaultIfEmpty(0).Max());
            double fTol = Math.Max(1.0, 0.005 * f), mTol = Math.Max(10.0, 0.005 * WindMomentScale(windCase));
            double fz = WindImbalance(windCase), fh = WindShearImbalance(windCase), m = WindMomentImbalance(windCase);
            if (Math.Abs(fz) > fTol) return $"ΣFz = {fz:+#,0;-#,0} lb in {windCase}";
            if (fh > fTol) return $"ΣF across = {fh:#,0} lb in {windCase}";
            if (m > mTol) return $"ΣM = {m:#,0} lb·ft in {windCase} (of {WindMomentScale(windCase):N0})";
            return null;
        }

        // ---------------------------------------------------------------- making one

        /// <summary>The free body of the wall, beam or column with this ID, or null (not in the model).</summary>
        public static FreeBody Of(Building b, LoadSection s, GravityLoadPath.Result path, string id, LateralSection lateral = null) =>
            OfBody(b, s, path, new[] { id }, lateral);

        /// <summary>
        /// The free body of the selected members (<paramref name="ids"/>): each member's forces, those of a pair between two of
        /// them inside the body (<see cref="FreeBodyForce.Inside"/>), those from a member not selected internal at the cut (I),
        /// the foundation's a support (S), the loads applied to it external (E). Null: none of them is in the model.
        /// </summary>
        public static FreeBody OfBody(Building b, LoadSection s, GravityLoadPath.Result path, IEnumerable<string> ids, LateralSection lateral = null)
        {
            var list = (ids ?? Enumerable.Empty<string>()).Where(x => x != null).Distinct().ToList();
            if (b == null || list.Count == 0) return null;
            s = s ?? new LoadSection();
            path = path ?? GravityLoadPath.Run(b, s);
            var pairs = ConnectionPairs.Of(lateral);
            var bodies = new List<FreeBody>();
            foreach (var id in list)
            {
                FreeBody one;
                switch (b.Find(id))
                {
                    case Wall w: one = OfWall(b, s, path, w); break;
                    case Beam m: one = OfBeam(b, path, m); break;
                    case Column c: one = OfColumn(b, path, c); break;
                    default: continue;
                }
                AddWind(b, one, lateral, pairs);
                bodies.Add(one);
            }
            return Join(bodies);
        }

        /// <summary>
        /// The same free body in one wind case (<paramref name="windCase"/>: a shear wall case, "… (rigid)" or "… (flexible)",
        /// or the wind calculator's own): its gravity loads as <see cref="OfBody(Building, LoadSection, GravityLoadPath.Result, IEnumerable{string}, LateralSection)"/>,
        /// and the roof's wind in that case from its uplift pass (<paramref name="uplift"/>, the run of the wind calculator's
        /// case, <see cref="LateralSection.WindOf"/>): the same forces again, each with its wind alone (W, in the same parts:
        /// the roof's on it E, the cut's I, the foundation's S). The load path is linear, so the gravity, the roof's wind and the
        /// shear walls' connections each balance, and so does every combination of them (<see cref="Imbalance"/>).
        /// </summary>
        public static FreeBody OfBody(Building b, LoadSection s, GravityLoadPath.Result path, IEnumerable<string> ids, LateralSection lateral,
                                      WindUplift uplift, string windCase)
        {
            var list = (ids ?? Enumerable.Empty<string>()).ToList();
            var fb = OfBody(b, s, path, list, lateral);
            if (fb == null || windCase == null) return fb;
            fb.WindCase = windCase;
            string roof = LateralSection.WindOf(windCase);
            var run = uplift == null ? default : uplift.Runs.FirstOrDefault(r => r.Case?.Name == roof || r.Case?.Name == windCase);
            if (run.Path == null) { fb.Notes.Add($"No roof wind in {windCase} (no uplift pass for it): the shear walls' forces only."); return fb; }
            var w = OfBody(b, s, run.Path, list, null);
            foreach (var f in w?.Forces.Where(f => Math.Abs(f.Load.PathWind) > 1e-9) ?? Enumerable.Empty<FreeBodyForce>())
                fb.Forces.Add(new FreeBodyForce
                {
                    Part = f.Part, What = $"{f.What}: the roof's wind", Member = f.Member, Other = f.Other, Inside = f.Inside, Z = f.Z, A = f.A, B = f.B, Up = f.Up,
                    Load = new ServiceLoad { Winds = new Dictionary<string, double> { [windCase] = f.Load.PathWind } },
                });
            return fb;
        }

        /// <summary>
        /// The free body of members with the wind on them only (<see cref="AddWind"/>), no gravity loads: for the check that
        /// every member balances the wind (<see cref="ConnectionBalance"/>). Null: none of them is in the model.
        /// </summary>
        public static FreeBody WindOnly(Building b, IEnumerable<string> ids, LateralSection lateral, IEnumerable<ConnectionPair> pairs)
        {
            var bodies = new List<FreeBody>();
            foreach (var id in (ids ?? Enumerable.Empty<string>()).Distinct())
            {
                var one = Shell(b, id);
                if (one == null) continue;
                AddWind(b, one, lateral, pairs);
                bodies.Add(one);
            }
            return Join(bodies);
        }

        public static FreeBody WindOnly(Building b, string id, LateralSection lateral, IEnumerable<ConnectionPair> pairs) =>
            WindOnly(b, new[] { id }, lateral, pairs);

        /// <summary>One member's free body with nothing on it yet (its place only); null: not in the model.</summary>
        private static FreeBody Shell(Building b, string id)
        {
            switch (b?.Find(id))
            {
                case Wall w: return New(w.Id, "wall", w.Start, w.End, Elev(b, w.BaseLevelId), Elev(b, w.TopLevelId));
                case Beam m: return New(m.Id, "beam", m.Start, m.End, Elev(b, m.LevelId), Elev(b, m.LevelId));
                case Column c: return New(c.Id, "column", c.At, c.At, Elev(b, c.BaseLevelId), Elev(b, c.TopLevelId));
                default: return null;
            }
        }

        private static FreeBody New(string id, string kind, Point2 a, Point2 e, double bottom, double top)
        {
            var fb = new FreeBody { Id = id, Kind = kind, Start = a, End = e, Length = kind == "column" ? 0 : Dist(a, e), Bottom = bottom, Top = top };
            fb.Members.Add(new FreeBodyMember { Id = id, Kind = kind, Start = a, End = e, Length = fb.Length, Bottom = bottom, Top = top });
            return fb;
        }

        /// <summary>
        /// The members' free bodies as one body: every force kept, each sorted by its other end (WISE): a member in the body,
        /// inside (half of a pair that cancels); a member not in it, I (the cut); none, as it was (E applied, S the foundation, W).
        /// </summary>
        private static FreeBody Join(List<FreeBody> bodies)
        {
            if (bodies.Count == 0) return null;
            var fb = bodies.Count == 1 ? bodies[0] : new FreeBody
            {
                Id = string.Join(" + ", bodies.Select(x => x.Id)), Kind = "body",
                Start = bodies[0].Start, End = bodies[0].End, Length = bodies[0].Length,
                Bottom = bodies.Min(x => x.Bottom), Top = bodies.Max(x => x.Top),
            };
            if (bodies.Count > 1)
                foreach (var x in bodies)
                {
                    fb.Members.AddRange(x.Members);
                    fb.Forces.AddRange(x.Forces);
                    fb.Connections.AddRange(x.Connections);
                    fb.Shears.AddRange(x.Shears);
                    fb.Notes.AddRange(x.Notes.Select(n => $"{x.Id}: {n}"));
                }
            var set = new HashSet<string>(fb.Members.Select(m => m.Id));
            foreach (var f in fb.Forces.Where(f => f.Other != null))
            {
                f.Part = FreeBodyPart.Internal;
                f.Inside = set.Contains(f.Other);
            }
            foreach (var c in fb.Connections.Where(c => c.Other != null))
            {
                c.Part = FreeBodyPart.Internal;
                c.Inside = set.Contains(c.Other);
            }
            return fb;
        }

        /// <summary>
        /// The wind on one member (<see cref="Connections"/>, <see cref="Shears"/>): the wind's shear on its top at each story it
        /// is a shear wall on (E) and the sill's (S on the foundation, I on a floor); its half of each connection pair on it,
        /// its own at its base and those of the members standing on it (I, or S on the foundation).
        /// </summary>
        public static void AddWind(Building b, FreeBody fb, LateralSection lateral, IEnumerable<ConnectionPair> pairs)
        {
            double Along(Point2 x) => fb.Length <= 1e-9 ? 0
                : Math.Max(0, Math.Min(fb.Length, ((x.X - fb.Start.X) * (fb.End.X - fb.Start.X) + (x.Y - fb.Start.Y) * (fb.End.Y - fb.Start.Y)) / fb.Length));
            foreach (var p in ConnectionPairs.At(pairs, fb.Id))
            {
                bool own = p.Upper == fb.Id;
                var comp = p.Source?.CompressionX is double cx && p.Source?.CompressionY is double cy ? new Point2(cx, cy) : p.Where;
                string to = p.OnFoundation ? "the foundation" : p.Lower != null ? $"{p.LowerKind} {p.Lower}" : "nothing (nothing under it)";
                fb.Connections.Add(new FreeBodyConnection
                {
                    Pair = p, Own = own, Member = fb.Id, Other = own ? p.Lower : p.Upper,
                    Part = own && !p.Internal ? FreeBodyPart.Support : FreeBodyPart.Internal,
                    What = own ? $"{p.From}: {p.Connector ?? "no connector"} to {to}" : $"{p.From} ({p.Connector ?? "no connector"}), from {p.UpperKind} {p.Upper} above",
                    Z = Elev(b, p.LevelId), A = Along(p.Where), ACompression = Along(comp),
                });
            }
            if (fb.Kind != "wall" || fb.Length <= 1e-9) return;
            // + toward the wall's high end (east, north) in the design; + from its start to its end here
            bool ew = Math.Abs(fb.End.X - fb.Start.X) >= Math.Abs(fb.End.Y - fb.Start.Y);
            double sign = (ew ? fb.End.X - fb.Start.X : fb.End.Y - fb.Start.Y) >= 0 ? 1 : -1;
            var dir = new Point2((fb.End.X - fb.Start.X) / fb.Length, (fb.End.Y - fb.Start.Y) / fb.Length);
            var shearPairs = ShearPairs.Of(b, lateral, WoodFraming.Connections.ConnectionsSection.Describer(b)).Where(p => p.WallId == fb.Id).ToList();
            foreach (var d in (lateral?.Walls ?? new List<ShearWallDesign>()).Where(d => d.WallId == fb.Id && d.ShearOnTop != null && d.ShearOnTop.Count > 0))
            {
                var lv = b.Level(d.LevelId);
                var topPair = shearPairs.FirstOrDefault(p => p.AtTop && p.LevelId == d.LevelId);
                var sillPair = topPair == null ? null : shearPairs.ElementAtOrDefault(shearPairs.IndexOf(topPair) + 1); // made in pairs: top, then sill
                double bas = BaseOfStory(b, fb, lv);
                // the shear acts at the top of its story, its design's height over the story's base
                double z = d.HeightFt > 0 ? bas + d.HeightFt : lv?.Elevation ?? fb.Top;
                fb.Shears.Add(new FreeBodyShear
                {
                    Part = FreeBodyPart.External, What = $"The wind's shear from the {lv?.Name ?? "level"} diaphragm" + (topPair == null ? "" : $" ({topPair.Connector})"), Member = fb.Id, Z = z,
                    Origin = fb.Start, Dir = dir, ByCase = d.ShearOnTop.ToDictionary(kv => kv.Key, kv => sign * kv.Value),
                });
                bool onGround = bas <= 1e-6;
                fb.Shears.Add(new FreeBodyShear
                {
                    Part = onGround ? FreeBodyPart.Support : FreeBodyPart.Internal,
                    What = (onGround ? "The sill's shear into the foundation" : $"The sill's shear into the floor framing under it (cut)") + (sillPair == null ? "" : $" ({sillPair.Connector})"),
                    Member = fb.Id, Z = bas, Origin = fb.Start, Dir = dir,
                    ByCase = d.ShearOnTop.ToDictionary(kv => kv.Key, kv => -sign * kv.Value),
                });
            }
        }

        private static double Dist(Point2 a, Point2 e) => Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Y - a.Y) * (e.Y - a.Y));

        /// <summary>The base of the story of <paramref name="fb"/> (a wall) that carries <paramref name="lv"/> (ft).</summary>
        private static double BaseOfStory(Building b, FreeBody fb, Level lv)
        {
            if (lv == null || !(b.Find(fb.Id) is Wall w)) return fb.Bottom;
            double z = fb.Bottom;
            foreach (var l in b.LevelsAlong(w))
            {
                if (l.Id == lv.Id) return z;
                z = l.Elevation;
            }
            return fb.Bottom;
        }

        private static double Elev(Building b, string levelId) => b.Level(levelId)?.Elevation ?? 0;

        private static void AddProfile(FreeBody fb, LoadProfile p, FreeBodyPart part, string what, double z, bool up, string other = null)
        {
            foreach (var st in p?.Steps ?? new List<ProfileStep>())
            {
                var load = ServiceLoad.Of(st.Load);
                if ((load.IsEmpty && Math.Abs(load.PathWind) < 1e-9) || st.Length <= 1e-9) continue;
                var last = fb.Forces.LastOrDefault();
                if (last != null && last.Part == part && last.What == what && last.Other == other && Math.Abs(last.Z - z) < 1e-9 && last.Up == up && Math.Abs(last.B - st.A) < 1e-6 && SameLoad(last.Load, load))
                { last.B = st.B; continue; }
                fb.Forces.Add(new FreeBodyForce { Part = part, What = what, Member = fb.Id, Other = other, Z = z, A = st.A, B = st.B, Load = load, Up = up });
            }
        }

        private static bool SameLoad(ServiceLoad a, ServiceLoad b) =>
            Math.Abs(a.Dead - b.Dead) < 1e-6 && Math.Abs(a.Live - b.Live) < 1e-6 && Math.Abs(a.RoofLive - b.RoofLive) < 1e-6 && Math.Abs(a.Snow - b.Snow) < 1e-6 && Math.Abs(a.PathWind - b.PathWind) < 1e-6;

        private static FreeBody OfWall(Building b, LoadSection s, GravityLoadPath.Result path, Wall w)
        {
            double zBase = Elev(b, w.BaseLevelId);
            var fb = New(w.Id, "wall", w.Start, w.End, zBase, Elev(b, w.TopLevelId));
            double len = fb.Length;
            var stories = b.LevelsAlong(w);
            WallTopLoad Story(Level l) => path.TryGetValue(GravityLoadPath.Key(w.Id, l.Id), out var r) ? r : null;
            var tops = stories.Select(l => (level: l, load: Story(l))).Where(x => x.load != null).ToList();
            if (tops.Count == 0) { fb.Notes.Add("The load path does not reach this wall (not square to the plan, or no level along it)."); return fb; }

            // a load entered by hand replaces everything from its level up: the lowest one rules
            int entered = tops.FindIndex(x => x.load.Entered);
            int upTo = entered >= 0 ? entered : tops.Count - 1;
            if (entered >= 0)
                fb.Notes.Add($"A top load entered by hand at {tops[entered].level.Name} replaces the load path's there and above: it is applied there, the line loads above it are not counted.");

            for (int i = 0; i <= upTo; i++)
            {
                var (lv, r) = tops[i];
                double z = lv.Elevation;
                if (i == entered)
                    AddProfile(fb, r.Profile, FreeBodyPart.External, $"Entered by hand at {lv.Name}", z, false);
                else
                {
                    AddProfile(fb, r.FromLevelProfile, FreeBodyPart.External, $"{lv.Name} {(i == tops.Count - 1 && lv.Id == b.RoofLevel()?.Id ? "roof" : "floor")}", z, false);
                    // the walls standing on its top, each its own (the wall's own story above is part of this free body)
                    if (i == tops.Count - 1 && r.Above.Count > 0)
                    {
                        if (r.AboveParts.Count > 0)
                            foreach (var kv in r.AboveParts)
                                AddProfile(fb, kv.Value, FreeBodyPart.Internal, $"{kv.Key} standing on it, with its weight", z, false, kv.Key);
                        else
                            AddProfile(fb, r.FromAboveProfile, FreeBodyPart.External, $"{string.Join(", ", r.Above)} standing on it, with their weight", z, false);
                    }
                }
            }
            // concentrated loads landing on it at each level (those carried down its own stories are the same loads)
            foreach (var (lv, r) in tops)
                foreach (var p in r.Here)
                    if (!p.Load.IsEmpty)
                        fb.Forces.Add(new FreeBodyForce { Part = p.Source == null ? FreeBodyPart.External : FreeBodyPart.Internal, What = p.From, Member = fb.Id, Other = p.Source, Z = lv.Elevation, A = p.At, B = p.At, Load = ServiceLoad.Of(p.Load) });

            // its own weight over the height counted
            double h = tops[upTo].level.Elevation - zBase;
            if (s.WallDead > 0 && h > 0)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.Weight, What = $"Own weight, {s.WallDead:0.##} psf × {h:0.##} ft", Member = fb.Id, Z = zBase + h / 2, A = 0, B = len, Load = new ServiceLoad { Dead = s.WallDead * h } });
            else fb.Notes.Add("No wall weight is entered (Floor and roof loads: the walls' own weight), so W is 0.");

            // what holds it up: the bottom story's load with its own weight, on the foundation (S), or on each wall or beam under
            // it (I, their reactions where it is cut from them); the posts each on their own
            var bottom = tops[0].load;
            var baseLoad = bottom.Profile.Plus(LoadProfile.Uniform(0, len, new LineLoad { Dead = s.WallDead * (tops[0].level.Elevation - zBase) }));
            bool ground = zBase <= 1e-6;
            if (ground) AddProfile(fb, baseLoad, FreeBodyPart.Support, "Under it: the foundation", zBase, true);
            else
            {
                var under = Under(b, w);
                double covered = 0;
                foreach (var (id, a, e) in under)
                {
                    double from = Math.Max(a, covered);
                    if (e - from <= 1e-6) continue;
                    AddProfile(fb, baseLoad.Moved(x => x, from, e), FreeBodyPart.Internal, $"Under it: {id}", zBase, true, id);
                    covered = Math.Max(covered, e);
                }
                if (under.Count == 0 || covered < len - 1e-6 || under.Min(u => u.a) > 1e-6)
                {
                    var bare = new LoadProfile();
                    double at = 0;
                    foreach (var (_, a, e) in under.OrderBy(u => u.a))
                    {
                        if (a > at + 1e-6) bare = bare.Plus(baseLoad.Moved(x => x, at, a));
                        at = Math.Max(at, e);
                    }
                    if (at < len - 1e-6) bare = bare.Plus(baseLoad.Moved(x => x, at, len));
                    AddProfile(fb, bare, FreeBodyPart.Support, "Under it: nothing drawn", zBase, true);
                }
            }
            foreach (var p in bottom.Points)
                if (!p.Load.IsEmpty)
                {
                    string other = ground ? null : p.Onto;
                    fb.Forces.Add(new FreeBodyForce
                    {
                        Part = other == null ? FreeBodyPart.Support : FreeBodyPart.Internal, What = $"Post under {p.From}" + (other == null ? "" : $", on {other}"),
                        Member = fb.Id, Other = other, Z = zBase, A = p.At, B = p.At, Load = ServiceLoad.Of(p.Load), Up = true,
                    });
                }
            return fb;
        }

        /// <summary>
        /// The walls and beams under a wall standing on an upper level, each with the stretch of the wall it is under (ft from
        /// the wall's start), walls first; in order along the wall.
        /// </summary>
        private static List<(string id, double a, double e)> Under(Building b, Wall w)
        {
            const double near = 0.25;
            double dx = w.End.X - w.Start.X, dy = w.End.Y - w.Start.Y, l = Math.Sqrt(dx * dx + dy * dy);
            var list = new List<(string, double, double)>();
            if (l < 1e-9) return list;
            double Off(Point2 p) => Math.Abs((p.X - w.Start.X) * dy - (p.Y - w.Start.Y) * dx) / l;
            double T(Point2 p) => ((p.X - w.Start.X) * dx + (p.Y - w.Start.Y) * dy) / l;
            void Try(string id, Point2 a, Point2 e)
            {
                // collinear with the wall and overlapping it
                if (Off(a) > near || Off(e) > near) return;
                double t0 = Math.Max(Math.Min(T(a), T(e)), 0), t1 = Math.Min(Math.Max(T(a), T(e)), l);
                if (t1 - t0 > near) list.Add((id, t0, t1));
            }
            foreach (var x in b.Walls.Where(x => x.Id != w.Id && b.LevelsAlong(x).Any(v => v.Id == w.BaseLevelId))) Try(x.Id, x.Start, x.End);
            foreach (var m in b.Beams.Where(m => m.LevelId == w.BaseLevelId)) Try(m.Id, m.Start, m.End);
            return list.OrderBy(x => x.Item2).ToList();
        }

        private static FreeBody OfBeam(Building b, GravityLoadPath.Result path, Beam m)
        {
            double z = Elev(b, m.LevelId);
            var fb = New(m.Id, "beam", m.Start, m.End, z, z);
            if (!path.Beams.TryGetValue(m.Id, out var r)) { fb.Notes.Add("The load path does not reach this beam."); return fb; }
            var lv = b.Level(m.LevelId);
            string level = $"{lv?.Name} {(lv?.Id == b.RoofLevel()?.Id ? "roof" : "floor")}";
            // its floor or roof share step by step along it, as its reactions take it (each load on its own)
            if (r.FromLevelProfile != null && !r.FromLevelProfile.IsEmpty) AddProfile(fb, r.FromLevelProfile, FreeBodyPart.External, level, z, false);
            else if (!r.FromLevel.IsEmpty)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.External, What = level, Member = fb.Id, Z = z, A = 0, B = r.Span, Load = ServiceLoad.Of(r.FromLevel) });
            foreach (var p in r.Partials)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.Internal, What = $"{p.From} standing on it, with its weight", Member = fb.Id, Other = p.From, Z = z, A = p.A, B = p.B, Load = ServiceLoad.Of(p.Load) });
            foreach (var p in r.Points)
                fb.Forces.Add(new FreeBodyForce { Part = p.Source == null ? FreeBodyPart.External : FreeBodyPart.Internal, What = p.From, Member = fb.Id, Other = p.Source, Z = z, A = p.At, B = p.At, Load = ServiceLoad.Of(p.Load) });
            fb.Notes.Add("The beam's own weight is not in the loads (beams are not sized yet): W is 0.");
            foreach (var (on, at, R, which) in new[] { (r.StartOn, 0.0, r.StartReaction, "Start"), (r.EndOn, r.Span, r.EndReaction, "End") })
                fb.Forces.Add(new FreeBodyForce
                {
                    Part = on == null ? FreeBodyPart.Support : FreeBodyPart.Internal, What = $"{which}, on {on ?? "nothing drawn"}",
                    Member = fb.Id, Other = on, Z = z, A = at, B = at, Load = ServiceLoad.Of(R), Up = true,
                });
            fb.Notes.AddRange(r.Notes);
            return fb;
        }

        private static FreeBody OfColumn(Building b, GravityLoadPath.Result path, Column c)
        {
            var fb = New(c.Id, "column", c.At, c.At, Elev(b, c.BaseLevelId), Elev(b, c.TopLevelId));
            if (!path.Columns.TryGetValue(c.Id, out var r)) { fb.Notes.Add("The load path does not reach this column."); return fb; }
            foreach (var g in r.Landings)
                fb.Forces.Add(new FreeBodyForce { Part = g.Source == null ? FreeBodyPart.External : FreeBodyPart.Internal, What = $"{g.From} at {b.Level(g.LevelId)?.Name}", Member = fb.Id, Other = g.Source, Z = Elev(b, g.LevelId), Load = ServiceLoad.Of(g.Load) });
            fb.Notes.Add("The column's own weight is not in the loads (columns are not sized yet): W is 0.");
            string on = r.BaseOn == "the foundation" ? null : r.BaseOn;
            fb.Forces.Add(new FreeBodyForce
            {
                Part = r.BaseOn == "the foundation" || on == null ? FreeBodyPart.Support : FreeBodyPart.Internal, What = $"Base, on {r.BaseOn ?? "nothing drawn"}",
                Member = fb.Id, Other = on, Z = fb.Bottom, Load = ServiceLoad.Of(r.Load), Up = true,
            });
            return fb;
        }
    }
}

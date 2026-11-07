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
        /// <summary>W: the element's own weight.</summary>
        Weight,
        /// <summary>I: internal forces where the element is cut (none when the whole element is drawn).</summary>
        Internal,
        /// <summary>S: the support reactions, what holds it up.</summary>
        Support,
        /// <summary>E: the external forces applied to it.</summary>
        External,
    }

    /// <summary>One force on a free body: a line load over a stretch of the element or a point load, at a height, service level.</summary>
    public sealed class FreeBodyForce
    {
        public FreeBodyPart Part { get; set; }
        /// <summary>What it is: "Level 2 floor", "BM1 via W7", "own weight", "W1 (Level 1) under it".</summary>
        public string What { get; set; }
        /// <summary>The elevation it acts at (ft).</summary>
        public double Z { get; set; }
        /// <summary>From and to along the element from its start (ft); equal for a point load.</summary>
        public double A { get; set; }
        public double B { get; set; }
        public bool IsLine => B - A > 1e-9;
        /// <summary>The service loads by kind: plf for a line load, lb for a point load.</summary>
        public ServiceLoad Load { get; set; } = new ServiceLoad();
        /// <summary>It acts upward (a support holding the element up); else downward.</summary>
        public bool Up { get; set; }

        /// <summary>Its size in a combination (plf or lb), the gravity loads only (D, L, Lr, S), the factors as written.</summary>
        public double Value(LoadCombination c) =>
            c == null ? Load.Dead + Load.Live + Load.RoofLive + Load.Snow
            : c.Factor(LoadType.Dead) * Load.Dead + c.Factor(LoadType.Live) * Load.Live + c.Factor(LoadType.RoofLive) * Load.RoofLive + c.Factor(LoadType.Snow) * Load.Snow;

        /// <summary>Its resultant in a combination (lb), + in the way it acts.</summary>
        public double Resultant(LoadCombination c) => Value(c) * (IsLine ? B - A : 1);
    }

    /// <summary>A member's half of one connection pair (<see cref="ConnectionPair"/>): the force on it, per wind case.</summary>
    public sealed class FreeBodyConnection
    {
        public ConnectionPair Pair { get; set; }
        /// <summary>Its own (it hangs from it, at its base: S); else one landing on it (E).</summary>
        public bool Own { get; set; }
        public FreeBodyPart Part { get; set; }
        public string What { get; set; }
        /// <summary>The elevation it acts at (ft) and where along the member (ft from its start): at the hold-down when it pulls.</summary>
        public double Z { get; set; }
        public double A { get; set; }
        /// <summary>Where along the member it acts when it presses (ft): the end post's centreline.</summary>
        public double ACompression { get; set; }

        /// <summary>The force on this member in one wind case (lb, + up).</summary>
        public double On(string windCase) => Own ? Pair.OnUpper(windCase) : Pair.OnLower(windCase);

        /// <summary>Where along the member it acts in one wind case (ft from its start).</summary>
        public double AlongIn(string windCase) => Pair.Of(windCase) < 0 ? ACompression : A;
    }

    /// <summary>
    /// The wind's shear on a wall, per wind case: on its top from its diaphragm (E, the external load), and the sill's
    /// reaction under it (S). Horizontal, along the wall.
    /// </summary>
    public sealed class FreeBodyShear
    {
        public FreeBodyPart Part { get; set; }
        public string What { get; set; }
        /// <summary>The elevation it acts at (ft).</summary>
        public double Z { get; set; }
        /// <summary>Wind case → the force (lb, W at strength level), + along the member from its start to its end.</summary>
        public Dictionary<string, double> ByCase { get; set; } = new Dictionary<string, double>();
        public double Of(string windCase) => windCase != null && ByCase.TryGetValue(windCase, out var v) ? v : 0;
    }

    /// <summary>
    /// A free-body diagram of one whole wall, beam or column, from the gravity load path: its own weight (W), no internal
    /// forces (the element is drawn whole, not cut: I), what holds it up (S) and what is applied to it (E). The loads one
    /// story of a wall puts on its own story below are internal to the wall and do not appear; nor do loads between other
    /// elements. Each force keeps its service loads by kind; a combination factors every force alike, so the diagram stays in
    /// equilibrium in every combination (gravity only: the roof's wind is in the uplift pass).
    /// </summary>
    public sealed class FreeBody
    {
        public string Id { get; set; }
        /// <summary>"wall", "beam" or "column".</summary>
        public string Kind { get; set; }
        /// <summary>Its length along its start-to-end (ft); 0 for a column.</summary>
        public double Length { get; set; }
        public Point2 Start { get; set; }
        public Point2 End { get; set; }
        /// <summary>Its bottom and top (ft).</summary>
        public double Bottom { get; set; }
        public double Top { get; set; }
        public List<FreeBodyForce> Forces { get; } = new List<FreeBodyForce>();
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// The wind on it and what holds it against the wind, per wind case, by WISE as for the gravity loads (kept apart from
        /// <see cref="Forces"/>, the gravity combinations; W at strength level, no dead load here). E: the wind's shear on its
        /// top from its diaphragm (<see cref="Shears"/>), and the connections of the members standing on it. S: its own
        /// hold-downs and compression posts (<see cref="Connections"/>, each its half of an equal and opposite pair) and the
        /// sill's shear. I: none (one whole member; a connection between two stories of the same wall is inside it).
        /// </summary>
        public List<FreeBodyConnection> Connections { get; } = new List<FreeBodyConnection>();

        /// <summary>The wind's shear on it (E, at the top of each story it is a shear wall on) and the sill's under it (S).</summary>
        public List<FreeBodyShear> Shears { get; } = new List<FreeBodyShear>();

        /// <summary>The wind cases on it.</summary>
        public List<string> WindCases => Connections.SelectMany(c => c.Pair.Force.Keys).Concat(Shears.SelectMany(s => s.ByCase.Keys)).Distinct().ToList();

        /// <summary>ΣFy in one wind case (lb, + up): 0 when every vertical force on it is accounted for.</summary>
        public double WindImbalance(string windCase) => Connections.Sum(c => c.On(windCase));

        /// <summary>ΣFx along it in one wind case (lb).</summary>
        public double WindShearImbalance(string windCase) => Shears.Sum(s => s.Of(windCase));

        /// <summary>
        /// ΣM about its start at its base in one wind case (lb·ft, in its own plane, + turning its end up): each vertical force
        /// times its distance along, less each shear times its height. 0 when the connections take the wind's overturning.
        /// </summary>
        public double WindMomentImbalance(string windCase) =>
            Connections.Sum(c => c.On(windCase) * c.AlongIn(windCase)) - Shears.Sum(s => s.Of(windCase) * (s.Z - Bottom));

        /// <summary>
        /// Why it does not balance the wind in one case (null: it does): ΣFy or ΣFx over 0.5 % of its largest force (at
        /// least 1 lb), or ΣM over 1 % of <see cref="WindMomentScale"/> (at least 10 lb·ft): rounding aside.
        /// </summary>
        public string WindOutOfBalance(string windCase)
        {
            double f = Math.Max(Connections.Select(x => Math.Max(x.Pair.MaxTension, x.Pair.MaxCompression)).DefaultIfEmpty(0).Max(),
                                Shears.SelectMany(s => s.ByCase.Values).Select(Math.Abs).DefaultIfEmpty(0).Max());
            double fTol = Math.Max(1.0, 0.005 * f), mTol = Math.Max(10.0, 0.01 * WindMomentScale(windCase));
            double fy = WindImbalance(windCase), fx = WindShearImbalance(windCase), m = WindMomentImbalance(windCase);
            if (Math.Abs(fy) > fTol) return $"ΣFy = {fy:+#,0;-#,0} lb in {windCase}";
            if (Math.Abs(fx) > fTol) return $"ΣFx = {fx:+#,0;-#,0} lb in {windCase}";
            if (Math.Abs(m) > mTol) return $"ΣM = {m:+#,0;-#,0} lb·ft in {windCase} (of {WindMomentScale(windCase):N0})";
            return null;
        }

        /// <summary>A scale for the moment check in one case (lb·ft): the wind's overturning on it and the moments of the forces landing on it.</summary>
        public double WindMomentScale(string windCase) => Math.Abs(Shears.Where(s => s.Part == FreeBodyPart.External).Sum(s => s.Of(windCase) * (s.Z - Bottom)))
                                                          + Connections.Where(c => !c.Own).Sum(c => Math.Abs(c.On(windCase) * c.AlongIn(windCase)));

        public IEnumerable<FreeBodyForce> Of(FreeBodyPart part) => Forces.Where(f => f.Part == part);

        /// <summary>The sum of the vertical forces in a combination (lb, + down): 0 in equilibrium.</summary>
        public double Imbalance(LoadCombination c) => Forces.Sum(f => f.Up ? -f.Resultant(c) : f.Resultant(c));

        /// <summary>The total of one part in a combination (lb, + the way its forces act).</summary>
        public double Total(FreeBodyPart part, LoadCombination c) => Of(part).Sum(f => f.Resultant(c));

        /// <summary>Every gravity load kind on it.</summary>
        public ServiceLoad Kinds()
        {
            var s = new ServiceLoad();
            foreach (var f in Forces.Where(f => !f.Up))
            {
                double k = f.IsLine ? f.B - f.A : 1;
                s.Dead += f.Load.Dead * k; s.Live += f.Load.Live * k; s.RoofLive += f.Load.RoofLive * k; s.Snow += f.Load.Snow * k;
            }
            return s;
        }

        /// <summary>
        /// The combinations of the method made only of D, L, Lr and S whose loads are on it, the one giving the most
        /// support reaction first.
        /// </summary>
        public List<LoadCombination> Combinations(DesignMethod method)
        {
            var k = Kinds();
            var gravity = new HashSet<LoadType> { LoadType.Dead, LoadType.Live, LoadType.RoofLive, LoadType.Snow };
            var list = (method == DesignMethod.Asd ? LoadCombinations.Asd() : LoadCombinations.Asce7Strength())
                .Where(c => c.Types.All(gravity.Contains) && c.Types.All(t => k.Has(t)))
                .ToList();
            return list.OrderByDescending(c => Total(FreeBodyPart.Support, c)).ToList();
        }

        /// <summary>
        /// The free body of the wall, beam or column with this ID, or null (not found, or nothing known of it); with the
        /// lateral design's connections on it from <paramref name="lateral"/> (null: none).
        /// </summary>
        public static FreeBody Of(Building b, LoadSection s, GravityLoadPath.Result path, string id, LateralSection lateral = null)
        {
            if (b == null || id == null) return null;
            s = s ?? new LoadSection();
            path = path ?? GravityLoadPath.Run(b, s);
            FreeBody fb;
            switch (b.Find(id))
            {
                case Wall w: fb = OfWall(b, s, path, w); break;
                case Beam m: fb = OfBeam(b, path, m); break;
                case Column c: fb = OfColumn(b, path, c); break;
                default: return null;
            }
            AddWind(b, fb, lateral, ConnectionPairs.Of(lateral));
            return fb;
        }

        /// <summary>
        /// The wind on it and what holds it against the wind (<see cref="Connections"/>, <see cref="Shears"/>): the wind's
        /// shear on its top at each story it is a shear wall on (E) and the sill's (S); its half of each connection pair on it,
        /// its own at its base (S), those landing on it (E).
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
                    Pair = p, Own = own, Part = own ? FreeBodyPart.Support : FreeBodyPart.External,
                    What = own ? $"{p.From}: {p.Connector ?? "no connector"} to {to}" : $"{p.From} ({p.Connector ?? "no connector"}), from above",
                    Z = Elev(b, p.LevelId), A = Along(p.Where), ACompression = Along(comp),
                });
            }
            if (fb.Kind != "wall" || fb.Length <= 1e-9) return;
            // + toward the wall's high end (east, north) in the design; + from its start to its end here
            bool ew = Math.Abs(fb.End.X - fb.Start.X) >= Math.Abs(fb.End.Y - fb.Start.Y);
            double sign = (ew ? fb.End.X - fb.Start.X : fb.End.Y - fb.Start.Y) >= 0 ? 1 : -1;
            foreach (var d in (lateral?.Walls ?? new List<ShearWallDesign>()).Where(d => d.WallId == fb.Id && d.ShearOnTop != null && d.ShearOnTop.Count > 0))
            {
                var lv = b.Level(d.LevelId);
                double top = lv?.Elevation ?? fb.Top;
                // the shear acts at the top of its story, its design's height over the story's base
                double z = d.HeightFt > 0 ? BaseOfStory(b, fb, lv) + d.HeightFt : top;
                fb.Shears.Add(new FreeBodyShear
                {
                    Part = FreeBodyPart.External, What = $"The wind's shear from the {lv?.Name ?? "level"} diaphragm", Z = z,
                    ByCase = d.ShearOnTop.ToDictionary(kv => kv.Key, kv => sign * kv.Value),
                });
                fb.Shears.Add(new FreeBodyShear
                {
                    Part = FreeBodyPart.Support, What = $"The sill's shear under {(lv == null ? "it" : "its story to " + lv.Name)}", Z = BaseOfStory(b, fb, lv),
                    ByCase = d.ShearOnTop.ToDictionary(kv => kv.Key, kv => -sign * kv.Value),
                });
            }
        }

        /// <summary>
        /// The free body of a wall, beam or column with the wind on it only (<see cref="AddWind"/>), no gravity loads: for the
        /// check that every member balances the wind (<see cref="ConnectionBalance"/>). Null: not in the model.
        /// </summary>
        public static FreeBody WindOnly(Building b, string id, LateralSection lateral, IEnumerable<ConnectionPair> pairs)
        {
            FreeBody fb;
            switch (b?.Find(id))
            {
                case Wall w:
                    fb = new FreeBody { Id = w.Id, Kind = "wall", Start = w.Start, End = w.End, Length = Dist(w.Start, w.End), Bottom = Elev(b, w.BaseLevelId), Top = Elev(b, w.TopLevelId) };
                    break;
                case Beam m:
                    fb = new FreeBody { Id = m.Id, Kind = "beam", Start = m.Start, End = m.End, Length = Dist(m.Start, m.End), Bottom = Elev(b, m.LevelId), Top = Elev(b, m.LevelId) };
                    break;
                case Column c:
                    fb = new FreeBody { Id = c.Id, Kind = "column", Start = c.At, End = c.At, Bottom = Elev(b, c.BaseLevelId), Top = Elev(b, c.TopLevelId) };
                    break;
                default: return null;
            }
            AddWind(b, fb, lateral, pairs);
            return fb;
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

        private static void AddProfile(FreeBody fb, LoadProfile p, FreeBodyPart part, string what, double z, bool up)
        {
            foreach (var st in p?.Steps ?? new List<ProfileStep>())
            {
                var load = ServiceLoad.Of(st.Load);
                if (load.IsEmpty || st.Length <= 1e-9) continue;
                var last = fb.Forces.LastOrDefault();
                if (last != null && last.Part == part && last.What == what && Math.Abs(last.Z - z) < 1e-9 && last.Up == up && Math.Abs(last.B - st.A) < 1e-6 && SameLoad(last.Load, load))
                { last.B = st.B; continue; }
                fb.Forces.Add(new FreeBodyForce { Part = part, What = what, Z = z, A = st.A, B = st.B, Load = load, Up = up });
            }
        }

        private static bool SameLoad(ServiceLoad a, ServiceLoad b) =>
            Math.Abs(a.Dead - b.Dead) < 1e-6 && Math.Abs(a.Live - b.Live) < 1e-6 && Math.Abs(a.RoofLive - b.RoofLive) < 1e-6 && Math.Abs(a.Snow - b.Snow) < 1e-6;

        private static FreeBody OfWall(Building b, LoadSection s, GravityLoadPath.Result path, Wall w)
        {
            double len = Math.Sqrt((w.End.X - w.Start.X) * (w.End.X - w.Start.X) + (w.End.Y - w.Start.Y) * (w.End.Y - w.Start.Y));
            double zBase = Elev(b, w.BaseLevelId);
            var fb = new FreeBody { Id = w.Id, Kind = "wall", Length = len, Start = w.Start, End = w.End, Bottom = zBase, Top = Elev(b, w.TopLevelId) };
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
                    // walls standing on its top (the wall's own story above is part of this free body)
                    if (i == tops.Count - 1 && r.Above.Count > 0)
                        AddProfile(fb, r.FromAboveProfile, FreeBodyPart.External, $"{string.Join(", ", r.Above)} standing on it, with their weight", z, false);
                }
            }
            // concentrated loads landing on it at each level (those carried down its own stories are the same loads)
            foreach (var (lv, r) in tops)
                foreach (var p in r.Here)
                    if (!p.Load.IsEmpty)
                        fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.External, What = p.From, Z = lv.Elevation, A = p.At, B = p.At, Load = ServiceLoad.Of(p.Load) });

            // its own weight over the height counted
            double h = tops[upTo].level.Elevation - zBase;
            if (s.WallDead > 0 && h > 0)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.Weight, What = $"Own weight, {s.WallDead:0.##} psf × {h:0.##} ft", Z = zBase + h / 2, A = 0, B = len, Load = new ServiceLoad { Dead = s.WallDead * h } });
            else fb.Notes.Add("No wall weight is entered (Floor and roof loads: the walls' own weight), so W is 0.");

            // what holds it up: the bottom story's load with its own weight, the posts each on their own
            var bottom = tops[0].load;
            string under = zBase <= 1e-6 ? "the foundation" : Under(b, w) ?? "what is under it (nothing drawn)";
            AddProfile(fb, bottom.Profile.Plus(LoadProfile.Uniform(0, len, new LineLoad { Dead = s.WallDead * (tops[0].level.Elevation - zBase) })),
                       FreeBodyPart.Support, $"Under it: {under}", zBase, true);
            foreach (var p in bottom.Points)
                if (!p.Load.IsEmpty)
                    fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.Support, What = $"Post under {p.From}", Z = zBase, A = p.At, B = p.At, Load = ServiceLoad.Of(p.Load), Up = true });
            return fb;
        }

        /// <summary>The walls and beams under a wall standing on an upper level ("W1, BM2"), or null.</summary>
        private static string Under(Building b, Wall w)
        {
            const double near = 0.25;
            bool OnLine(Point2 a, Point2 e)
            {
                // collinear with the wall and overlapping it
                double dx = w.End.X - w.Start.X, dy = w.End.Y - w.Start.Y, l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 1e-9) return false;
                double Off(Point2 p) => Math.Abs((p.X - w.Start.X) * dy - (p.Y - w.Start.Y) * dx) / l;
                if (Off(a) > near || Off(e) > near) return false;
                double t0 = ((a.X - w.Start.X) * dx + (a.Y - w.Start.Y) * dy) / l, t1 = ((e.X - w.Start.X) * dx + (e.Y - w.Start.Y) * dy) / l;
                return Math.Min(Math.Max(t0, t1), l) - Math.Max(Math.Min(t0, t1), 0) > near;
            }
            var names = b.Walls.Where(x => x.Id != w.Id && b.LevelsAlong(x).Any(l => l.Id == w.BaseLevelId) && OnLine(x.Start, x.End)).Select(x => x.Id)
                         .Concat(b.Beams.Where(m => m.LevelId == w.BaseLevelId && OnLine(m.Start, m.End)).Select(m => m.Id)).ToList();
            return names.Count == 0 ? null : string.Join(", ", names);
        }

        private static FreeBody OfBeam(Building b, GravityLoadPath.Result path, Beam m)
        {
            double len = Math.Sqrt((m.End.X - m.Start.X) * (m.End.X - m.Start.X) + (m.End.Y - m.Start.Y) * (m.End.Y - m.Start.Y));
            double z = Elev(b, m.LevelId);
            var fb = new FreeBody { Id = m.Id, Kind = "beam", Length = len, Start = m.Start, End = m.End, Bottom = z, Top = z };
            if (!path.Beams.TryGetValue(m.Id, out var r)) { fb.Notes.Add("The load path does not reach this beam."); return fb; }
            var lv = b.Level(m.LevelId);
            if (!r.FromLevel.IsEmpty)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.External, What = $"{lv?.Name} {(lv?.Id == b.RoofLevel()?.Id ? "roof" : "floor")}", Z = z, A = 0, B = r.Span, Load = ServiceLoad.Of(r.FromLevel) });
            foreach (var p in r.Partials)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.External, What = $"{p.From} standing on it, with its weight", Z = z, A = p.A, B = p.B, Load = ServiceLoad.Of(p.Load) });
            foreach (var p in r.Points)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.External, What = p.From, Z = z, A = p.At, B = p.At, Load = ServiceLoad.Of(p.Load) });
            fb.Notes.Add("The beam's own weight is not in the loads (beams are not sized yet): W is 0.");
            fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.Support, What = $"Start, on {r.StartOn ?? "nothing drawn"}", Z = z, A = 0, B = 0, Load = ServiceLoad.Of(r.StartReaction), Up = true });
            fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.Support, What = $"End, on {r.EndOn ?? "nothing drawn"}", Z = z, A = r.Span, B = r.Span, Load = ServiceLoad.Of(r.EndReaction), Up = true });
            fb.Notes.AddRange(r.Notes);
            return fb;
        }

        private static FreeBody OfColumn(Building b, GravityLoadPath.Result path, Column c)
        {
            var fb = new FreeBody { Id = c.Id, Kind = "column", Start = c.At, End = c.At, Bottom = Elev(b, c.BaseLevelId), Top = Elev(b, c.TopLevelId) };
            if (!path.Columns.TryGetValue(c.Id, out var r)) { fb.Notes.Add("The load path does not reach this column."); return fb; }
            foreach (var g in r.Landings)
                fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.External, What = $"{g.From} at {b.Level(g.LevelId)?.Name}", Z = Elev(b, g.LevelId), Load = ServiceLoad.Of(g.Load) });
            fb.Notes.Add("The column's own weight is not in the loads (columns are not sized yet): W is 0.");
            fb.Forces.Add(new FreeBodyForce { Part = FreeBodyPart.Support, What = $"Base, on {r.BaseOn ?? "nothing drawn"}", Z = fb.Bottom, Load = ServiceLoad.Of(r.Load), Up = true });
            return fb;
        }
    }
}

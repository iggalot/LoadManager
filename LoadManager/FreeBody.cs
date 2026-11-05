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

        /// <summary>The free body of the wall, beam or column with this ID, or null (not found, or nothing known of it).</summary>
        public static FreeBody Of(Building b, LoadSection s, GravityLoadPath.Result path, string id)
        {
            if (b == null || id == null) return null;
            s = s ?? new LoadSection();
            path = path ?? GravityLoadPath.Run(b, s);
            switch (b.Find(id))
            {
                case Wall w: return OfWall(b, s, path, w);
                case Beam m: return OfBeam(b, path, m);
                case Column c: return OfColumn(b, path, c);
                default: return null;
            }
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

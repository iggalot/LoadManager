using System;
using System.Collections.Generic;
using System.Linq;

namespace LoadManager
{
    /// <summary>A line load over part of a wall: from <see cref="A"/> to <see cref="B"/> along it from its start (ft).</summary>
    public sealed class ProfileStep
    {
        public double A { get; set; }
        public double B { get; set; }
        public LineLoad Load { get; set; } = new LineLoad();
        public double Length => B - A;
        public override string ToString() => $"{A:0.##} to {B:0.##} ft: {Load}";
    }

    /// <summary>
    /// How a line load varies along a wall: steps of uniform load, in order along it from its start (ft); nothing where there is no
    /// step. The load path's "most along the wall" (<see cref="WallTopLoad.Load"/>) is the largest of them; this keeps where each is,
    /// for the foundation (a footing under a wall sees the load where it is, not the largest everywhere).
    /// </summary>
    public sealed class LoadProfile
    {
        private const double Tol = 1e-6;

        public List<ProfileStep> Steps { get; } = new List<ProfileStep>();

        public bool IsEmpty => Steps.Count == 0;

        /// <summary>From the first step's start to the last one's end (0 when empty).</summary>
        public double Start => Steps.Count == 0 ? 0 : Steps[0].A;
        public double End => Steps.Count == 0 ? 0 : Steps[Steps.Count - 1].B;

        public static LoadProfile Uniform(double a, double b, LineLoad w)
        {
            var p = new LoadProfile();
            if (b - a > Tol && w != null) p.Steps.Add(new ProfileStep { A = a, B = b, Load = w + new LineLoad() });
            return p;
        }

        /// <summary>The load at a point along the wall (0 outside every step; at a step's edge, the step starting there).</summary>
        public LineLoad At(double x)
        {
            foreach (var s in Steps)
                if (x >= s.A - Tol && x < s.B - Tol) return s.Load;
            var last = Steps.LastOrDefault();
            return last != null && Math.Abs(x - last.B) <= Tol ? last.Load : new LineLoad();
        }

        private bool Covers(double x) => Steps.Any(s => x >= s.A - Tol && x <= s.B + Tol);

        /// <summary>The ends of every step of both: the places either changes.</summary>
        private static List<double> Cuts(LoadProfile p, LoadProfile q) =>
            p.Steps.SelectMany(s => new[] { s.A, s.B }).Concat(q.Steps.SelectMany(s => new[] { s.A, s.B })).OrderBy(x => x)
             .Aggregate(new List<double>(), (l, x) => { if (l.Count == 0 || x - l[l.Count - 1] > Tol) l.Add(x); return l; });

        /// <summary>Two loads that act together: their sum wherever either is.</summary>
        public LoadProfile Plus(LoadProfile o) => Pointwise(o, (a, b) => a + b);

        /// <summary>
        /// Two alternatives on the same wall (two plates, two walls standing on it): <paramref name="pick"/> where both are, the one there
        /// where only one is.
        /// </summary>
        public LoadProfile Combine(LoadProfile o, Func<LineLoad, LineLoad, LineLoad> pick) => Pointwise(o, pick);

        private LoadProfile Pointwise(LoadProfile o, Func<LineLoad, LineLoad, LineLoad> f)
        {
            o = o ?? new LoadProfile();
            var cuts = Cuts(this, o);
            var r = new LoadProfile();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double m = (cuts[i] + cuts[i + 1]) / 2;
                bool inA = Covers(m), inB = o.Covers(m);
                if (!inA && !inB) continue;
                var w = inA && inB ? f(At(m), o.At(m)) : inA ? At(m) : o.At(m);
                r.Add(cuts[i], cuts[i + 1], w);
            }
            return r;
        }

        /// <summary>Adds a step at the end, merged into the last one when it carries on with the same load.</summary>
        private void Add(double a, double b, LineLoad w)
        {
            var last = Steps.LastOrDefault();
            if (last != null && Math.Abs(last.B - a) <= Tol && last.Load.SameAs(w)) { last.B = b; return; }
            Steps.Add(new ProfileStep { A = a, B = b, Load = w + new LineLoad() });
        }

        /// <summary>Built from steps in any order (overlaps taken with <paramref name="pick"/>).</summary>
        public static LoadProfile Of(IEnumerable<ProfileStep> steps, Func<LineLoad, LineLoad, LineLoad> pick)
        {
            var list = steps.Where(s => s.B - s.A > Tol).OrderBy(s => s.A).ToList();
            var r = new LoadProfile();
            // the usual case, samples side by side: in order, merged where they carry on the same
            if (list.Zip(list.Skip(1), (p, q) => q.A >= p.B - Tol).All(x => x))
            {
                foreach (var s in list) r.Add(s.A, s.B, s.Load);
                return r;
            }
            foreach (var s in list) r = r.Combine(Uniform(s.A, s.B, s.Load), pick);
            return r;
        }

        /// <summary>
        /// Onto another wall on the same line: <paramref name="map"/> takes a distance along this wall to one along the other (it may
        /// run the other way); only the part from <paramref name="from"/> to <paramref name="to"/> on the other is kept.
        /// </summary>
        public LoadProfile Moved(Func<double, double> map, double from, double to)
        {
            var steps = new List<ProfileStep>();
            foreach (var s in Steps)
            {
                double a = map(s.A), b = map(s.B);
                if (a > b) { var t = a; a = b; b = t; }
                a = Math.Max(a, from); b = Math.Min(b, to);
                if (b - a > Tol) steps.Add(new ProfileStep { A = a, B = b, Load = s.Load });
            }
            return Of(steps, LineLoad.Max);
        }

        /// <summary>The largest of each kind along it (as <see cref="LineLoad.Max"/>).</summary>
        public LineLoad Most(Func<LineLoad, LineLoad, LineLoad> pick = null)
        {
            LineLoad m = null;
            foreach (var s in Steps) m = (pick ?? LineLoad.Max)(m, s.Load);
            return m ?? new LineLoad();
        }

        /// <summary>The whole load (lb): Σ load × length.</summary>
        public PointLoad Total() => Steps.Aggregate(new PointLoad(), (t, s) => t + s.Load.Over(s.Length));

        public override string ToString() => string.Join("; ", Steps);
    }
}

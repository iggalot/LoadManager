using System;
using System.Collections.Generic;
using System.Linq;

namespace LoadManager
{
    /// <summary>The kinds of load a combination is made of (ASCE 7 / IBC notation).</summary>
    public enum LoadType
    {
        Dead, Live, RoofLive, Snow, Rain, Wind,
        /// <summary>WT, tornado (ASCE 7-22 Ch. 32).</summary>
        Tornado,
        /// <summary>E, as the IBC alternative ASD combinations write it (Eh and Ev together).</summary>
        Earthquake,
        /// <summary>Eh, the horizontal seismic load effect (ASCE 7-22 12.4.2.1).</summary>
        SeismicHorizontal,
        /// <summary>Ev, the vertical seismic load effect (ASCE 7-22 12.4.2.2).</summary>
        SeismicVertical,
    }

    /// <summary>One load in a combination: its kind, factor and how the code writes it ("0.75(0.7S)" is 0.525 S).</summary>
    public sealed class LoadTerm
    {
        public LoadType Type { get; set; }
        /// <summary>Negative where the code subtracts it (0.6D − 0.7Ev).</summary>
        public double Factor { get; set; }
        /// <summary>As printed, without its sign ("0.7Ev").</summary>
        public string Text { get; set; }
    }

    /// <summary>A load combination: the sum of its terms.</summary>
    public sealed class LoadCombination
    {
        public string Name
        {
            get
            {
                string s = "";
                foreach (var t in Terms)
                    s += s.Length == 0 ? (t.Factor < 0 ? "−" : "") + t.Text : (t.Factor < 0 ? " − " : " + ") + t.Text;
                return s;
            }
        }
        /// <summary>Every code equation this combination comes from ("ASCE 7-22 2.4 Eq. 6a", "IBC Eq. 16-3").</summary>
        public List<string> Equations { get; set; } = new List<string>();
        public string Equation => string.Join("; ", Equations);
        public List<LoadTerm> Terms { get; set; } = new List<LoadTerm>();
        public double Factor(LoadType t) => Terms.Where(x => x.Type == t).Sum(x => x.Factor);
        public bool Has(LoadType t) => Factor(t) != 0;
        public IEnumerable<LoadType> Types => Terms.Where(x => x.Factor != 0).Select(x => x.Type).Distinct();
        public override string ToString() => $"{Name} ({Equation})";
    }

    /// <summary>
    /// The load combinations the design apps use: every equation of the code, every "or" alternative, each with its
    /// variable loads set to zero in turn. None is dropped; one that two equations both give is listed once, with both.
    /// </summary>
    public static class LoadCombinations
    {
        public static string Symbol(LoadType t)
        {
            switch (t)
            {
                case LoadType.Dead: return "D";
                case LoadType.Live: return "L";
                case LoadType.RoofLive: return "Lr";
                case LoadType.Snow: return "S";
                case LoadType.Rain: return "R";
                case LoadType.Wind: return "W";
                case LoadType.Tornado: return "WT";
                case LoadType.Earthquake: return "E";
                case LoadType.SeismicHorizontal: return "Eh";
                case LoadType.SeismicVertical: return "Ev";
                default: throw new ArgumentOutOfRangeException(nameof(t));
            }
        }

        public const string IbcAlternativeAsdSource =
            "IBC 2024 1605.1: structures shall be designed for the strength combinations of ASCE 7 2.3, the allowable stress " +
            "combinations of ASCE 7 2.4, \"or the alternative allowable stress design load combinations of Section 1605.2.\" " +
            "1605.2: Eq. 16-1 D + L + (Lr or 0.7S or R); 16-2 D + L + 0.6W; 16-3 D + L + 0.6W + 0.7S/2; 16-4 D + L + 0.7S + 0.6W/2; " +
            "16-5 D + L + 0.7S + E/1.4; 16-6 0.9D + E/1.4. \"Where using these alternative allowable stress load combinations that " +
            "include wind or seismic loads, allowable stresses are permitted to be increased or load combinations reduced where " +
            "permitted by the material chapter of this code or the referenced standards. For load combinations that include the " +
            "counteracting effects of dead and wind loads, only two-thirds of the minimum dead load likely to be in place during a " +
            "design wind event shall be used.\" \"Where design for tornado loads is required, the alternative allowable stress design " +
            "load combinations of Section 1605.2 shall not apply when tornado loads govern the design.\" (Text from the 2025 California " +
            "Building Code, which adopts the 2024 IBC, and ICC, Demystifying Loads for Building Officials – 2024 IBC / ASCE 7-22, " +
            "slide 64; the California amendment \"Each load combination shall be investigated with one or more of the variable loads " +
            "set to zero\" is followed for every set here, as NDS 2.3.2.2 needs every combination evaluated.)";

        public const string Asce7AsdSource =
            "ASCE 7-22 2.4 (allowable stress design): Eq. 1a D; 2a D + L; 3a D + (Lr or 0.7S or R); 4a D + 0.75L + 0.75(Lr or 0.7S or R); " +
            "5a D + 0.6(W or WT); 6a D + 0.75L + 0.75(0.6(W or WT)) + 0.75(Lr or 0.7S or R); 7a 0.6D + 0.6(W or WT); " +
            "8 D + 0.7Ev + 0.7Eh; 9 D + 0.525Ev + 0.525Eh + 0.75L + 0.1S; 10 0.6D − 0.7Ev + 0.7Eh. (As printed in ICC, Demystifying " +
            "Loads for Building Officials – 2024 IBC / ASCE 7-22, slide 63; not checked against the standard itself.)";

        public const string Asce7StrengthSource =
            "ASCE 7-22 2.3 (strength design / LRFD): Eq. 1a 1.4D; 2a 1.2D + 1.6L + (0.5Lr or 0.3S or 0.5R); " +
            "3a 1.2D + (1.6Lr or 1.0S or 1.6R) + (L or 0.5W); 4a 1.2D + 1.0(W or WT) + L + (0.5Lr or 0.3S or 0.5R); " +
            "5a 0.9D + 1.0(W or WT); 6 1.2D + Ev + Eh + L + 0.15S; 7 0.9D − Ev + Eh. (As printed in ICC, Demystifying Loads for " +
            "Building Officials – 2024 IBC / ASCE 7-22, slide 62; not checked against the standard itself.)";

        private static LoadTerm T(LoadType t, double f, string text) => new LoadTerm { Type = t, Factor = f, Text = text };
        private static LoadTerm D(double f = 1) => T(LoadType.Dead, f, f == 1 ? "D" : $"{f:0.0##}D");

        /// <summary>One printed equation with its "or" alternatives written out.</summary>
        private static IEnumerable<(string eq, LoadTerm[] terms)> Row(string eq, params LoadTerm[][] alternatives) =>
            alternatives.Select(a => (eq, a));

        /// <summary>The (Lr or 0.7S or R) choice with a factor: 0.75(Lr or 0.7S or R) → 0.75Lr, 0.75(0.7S), 0.75R.</summary>
        private static LoadTerm[] LrSR(double f, string fText) => new[]
        {
            T(LoadType.RoofLive, f, fText == "" ? "Lr" : fText + "Lr"),
            T(LoadType.Snow, 0.7 * f, fText == "" ? "0.7S" : fText + "(0.7S)"),
            T(LoadType.Rain, f, fText == "" ? "R" : fText + "R"),
        };

        /// <summary>The 2024 IBC alternative allowable stress design combinations (1605.2, Eq. 16-1 to 16-6).</summary>
        public static List<LoadCombination> IbcAlternativeAsd() => Expand(IbcRows());

        /// <summary>The ASCE 7-22 allowable stress design combinations (2.4, Eq. 1a to 10).</summary>
        public static List<LoadCombination> Asce7Asd() => Expand(Asce7AsdRows());

        /// <summary>The ASCE 7-22 strength design (LRFD) combinations (2.3, Eq. 1a to 7).</summary>
        public static List<LoadCombination> Asce7Strength() => Expand(Asce7StrengthRows());

        /// <summary>
        /// Every allowable stress combination: ASCE 7-22 2.4 and the IBC 1605.2 alternative ones (IBC 1605.1 permits either;
        /// all of both are analyzed). One that both give is listed once, with its equations from each.
        /// </summary>
        public static List<LoadCombination> Asd() => Expand(Asce7AsdRows().Concat(IbcRows()));

        private static IEnumerable<(string eq, LoadTerm[] terms)> IbcRows()
        {
            const string I = "IBC Eq. ";
            var L = T(LoadType.Live, 1, "L");
            return Enumerable.Empty<(string, LoadTerm[])>()
                .Concat(LrSR(1, "").Select(x => (I + "16-1", new[] { D(), L, x })))
                .Concat(Row(I + "16-2", new[] { D(), L, T(LoadType.Wind, 0.6, "0.6W") }))
                .Concat(Row(I + "16-3", new[] { D(), L, T(LoadType.Wind, 0.6, "0.6W"), T(LoadType.Snow, 0.35, "0.7S/2") }))
                .Concat(Row(I + "16-4", new[] { D(), L, T(LoadType.Snow, 0.7, "0.7S"), T(LoadType.Wind, 0.3, "0.6W/2") }))
                .Concat(Row(I + "16-5", new[] { D(), L, T(LoadType.Snow, 0.7, "0.7S"), T(LoadType.Earthquake, 1 / 1.4, "E/1.4") }))
                .Concat(Row(I + "16-6", new[] { D(0.9), T(LoadType.Earthquake, 1 / 1.4, "E/1.4") }));
        }

        private static IEnumerable<(string eq, LoadTerm[] terms)> Asce7AsdRows()
        {
            const string A = "ASCE 7-22 2.4 Eq. ";
            var winds = new[] { (LoadType.Wind, "W"), (LoadType.Tornado, "WT") };
            var rows = new List<(string, LoadTerm[])>();
            rows.Add((A + "1a", new[] { D() }));
            rows.Add((A + "2a", new[] { D(), T(LoadType.Live, 1, "L") }));
            rows.AddRange(LrSR(1, "").Select(x => (A + "3a", new[] { D(), x })));
            rows.AddRange(LrSR(0.75, "0.75").Select(x => (A + "4a", new[] { D(), T(LoadType.Live, 0.75, "0.75L"), x })));
            rows.AddRange(winds.Select(w => (A + "5a", new[] { D(), T(w.Item1, 0.6, "0.6" + w.Item2) })));
            foreach (var w in winds)
                rows.AddRange(LrSR(0.75, "0.75").Select(x => (A + "6a", new[] { D(), T(LoadType.Live, 0.75, "0.75L"), T(w.Item1, 0.45, $"0.75(0.6{w.Item2})"), x })));
            rows.AddRange(winds.Select(w => (A + "7a", new[] { D(0.6), T(w.Item1, 0.6, "0.6" + w.Item2) })));
            rows.Add((A + "8", new[] { D(), T(LoadType.SeismicVertical, 0.7, "0.7Ev"), T(LoadType.SeismicHorizontal, 0.7, "0.7Eh") }));
            rows.Add((A + "9", new[] { D(), T(LoadType.SeismicVertical, 0.525, "0.525Ev"), T(LoadType.SeismicHorizontal, 0.525, "0.525Eh"), T(LoadType.Live, 0.75, "0.75L"), T(LoadType.Snow, 0.1, "0.1S") }));
            rows.Add((A + "10", new[] { D(0.6), T(LoadType.SeismicVertical, -0.7, "0.7Ev"), T(LoadType.SeismicHorizontal, 0.7, "0.7Eh") }));
            return rows;
        }

        private static IEnumerable<(string eq, LoadTerm[] terms)> Asce7StrengthRows()
        {
            const string A = "ASCE 7-22 2.3 Eq. ";
            var winds = new[] { (LoadType.Wind, "W"), (LoadType.Tornado, "WT") };
            LoadTerm[] Half() => new[] { T(LoadType.RoofLive, 0.5, "0.5Lr"), T(LoadType.Snow, 0.3, "0.3S"), T(LoadType.Rain, 0.5, "0.5R") };
            var rows = new List<(string, LoadTerm[])>();
            rows.Add((A + "1a", new[] { D(1.4) }));
            rows.AddRange(Half().Select(x => (A + "2a", new[] { D(1.2), T(LoadType.Live, 1.6, "1.6L"), x })));
            var big = new[] { T(LoadType.RoofLive, 1.6, "1.6Lr"), T(LoadType.Snow, 1.0, "1.0S"), T(LoadType.Rain, 1.6, "1.6R") };
            var with = new[] { T(LoadType.Live, 1, "L"), T(LoadType.Wind, 0.5, "0.5W") };
            foreach (var b in big) rows.AddRange(with.Select(x => (A + "3a", new[] { D(1.2), b, x })));
            foreach (var w in winds)
                rows.AddRange(Half().Select(x => (A + "4a", new[] { D(1.2), T(w.Item1, 1.0, "1.0" + w.Item2), T(LoadType.Live, 1, "L"), x })));
            rows.AddRange(winds.Select(w => (A + "5a", new[] { D(0.9), T(w.Item1, 1.0, "1.0" + w.Item2) })));
            rows.Add((A + "6", new[] { D(1.2), T(LoadType.SeismicVertical, 1, "Ev"), T(LoadType.SeismicHorizontal, 1, "Eh"), T(LoadType.Live, 1, "L"), T(LoadType.Snow, 0.15, "0.15S") }));
            rows.Add((A + "7", new[] { D(0.9), T(LoadType.SeismicVertical, -1, "Ev"), T(LoadType.SeismicHorizontal, 1, "Eh") }));
            return rows;
        }

        /// <summary>
        /// Every row with its variable loads set to zero in turn (every subset of them, by size, then in the row's order).
        /// A combination two rows both give is listed once, at its first place, with every equation it comes from.
        /// </summary>
        private static List<LoadCombination> Expand(IEnumerable<(string eq, LoadTerm[] terms)> rows)
        {
            var list = new List<LoadCombination>();
            var byKey = new Dictionary<string, LoadCombination>();
            foreach (var (eq, terms) in rows)
            {
                var dead = terms.Where(t => t.Type == LoadType.Dead).ToList();
                var variable = terms.Where(t => t.Type != LoadType.Dead).ToList();
                var subsets = Enumerable.Range(0, 1 << variable.Count)
                    .Select(mask => variable.Where((t, i) => (mask & (1 << i)) != 0).ToList())
                    .OrderBy(s => s.Count).ThenBy(s => string.Join(",", s.Select(t => variable.IndexOf(t))));
                foreach (var sub in subsets)
                {
                    var c = new LoadCombination { Terms = dead.Concat(sub).ToList() };
                    string key = string.Join("|", c.Terms.OrderBy(t => t.Type).Select(t => $"{t.Type}:{t.Factor:R}"));
                    if (byKey.TryGetValue(key, out var seen)) { if (!seen.Equations.Contains(eq)) seen.Equations.Add(eq); continue; }
                    c.Equations.Add(eq);
                    byKey[key] = c;
                    list.Add(c);
                }
            }
            return list;
        }
    }
}

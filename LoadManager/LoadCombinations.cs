using System;
using System.Collections.Generic;
using System.Linq;

namespace LoadManager
{
    /// <summary>The kinds of load a combination is made of (ASCE 7 / IBC notation).</summary>
    public enum LoadType { Dead, Live, RoofLive, Snow, Rain, Wind, Earthquake }

    /// <summary>One load in a combination: its kind, factor and how the code writes it ("0.7S/2" is 0.35 S).</summary>
    public sealed class LoadTerm
    {
        public LoadType Type { get; set; }
        public double Factor { get; set; }
        public string Text { get; set; }
    }

    /// <summary>A load combination: the sum of its terms.</summary>
    public sealed class LoadCombination
    {
        public string Name => string.Join(" + ", Terms.Select(t => t.Text));
        /// <summary>The code's equation it comes from ("16-2").</summary>
        public string Equation { get; set; }
        public List<LoadTerm> Terms { get; set; } = new List<LoadTerm>();
        public double Factor(LoadType t) => Terms.Where(x => x.Type == t).Sum(x => x.Factor);
        public bool Has(LoadType t) => Factor(t) != 0;
        public IEnumerable<LoadType> Types => Terms.Where(x => x.Factor != 0).Select(x => x.Type).Distinct();
        public override string ToString() => $"{Name} (Eq. {Equation})";
    }

    /// <summary>The load combinations the design apps use.</summary>
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
                case LoadType.Earthquake: return "E";
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
            "design wind event shall be used.\" (Text from the 2025 California Building Code, which adopts the 2024 IBC; its " +
            "amendment \"Each load combination shall be investigated with one or more of the variable loads set to zero\" is followed " +
            "here too, as NDS 2.3.2.2 needs every combination evaluated.)";

        private static LoadTerm T(LoadType t, double f, string text) => new LoadTerm { Type = t, Factor = f, Text = text };

        /// <summary>
        /// The 2024 IBC alternative allowable stress design combinations (1605.2, Eq. 16-1 to 16-6), each with the variable
        /// loads set to zero in turn (every subset of them), without repeats: "D", "D + L", "D + Lr", "D + L + Lr", ...
        /// A subset leaving 0.9D alone (16-6 without E) is not a combination. Gravity-against-wind uplift (two-thirds of
        /// the dead load) is the caller's (<see cref="IbcAlternativeAsdSource"/>).
        /// </summary>
        public static List<LoadCombination> IbcAlternativeAsd()
        {
            var eqs = new List<(string eq, LoadTerm[] terms)>
            {
                ("16-1", new[] { T(LoadType.Dead, 1, "D"), T(LoadType.Live, 1, "L"), T(LoadType.RoofLive, 1, "Lr") }),
                ("16-1", new[] { T(LoadType.Dead, 1, "D"), T(LoadType.Live, 1, "L"), T(LoadType.Snow, 0.7, "0.7S") }),
                ("16-1", new[] { T(LoadType.Dead, 1, "D"), T(LoadType.Live, 1, "L"), T(LoadType.Rain, 1, "R") }),
                ("16-2", new[] { T(LoadType.Dead, 1, "D"), T(LoadType.Live, 1, "L"), T(LoadType.Wind, 0.6, "0.6W") }),
                ("16-3", new[] { T(LoadType.Dead, 1, "D"), T(LoadType.Live, 1, "L"), T(LoadType.Wind, 0.6, "0.6W"), T(LoadType.Snow, 0.35, "0.7S/2") }),
                ("16-4", new[] { T(LoadType.Dead, 1, "D"), T(LoadType.Live, 1, "L"), T(LoadType.Snow, 0.7, "0.7S"), T(LoadType.Wind, 0.3, "0.6W/2") }),
                ("16-5", new[] { T(LoadType.Dead, 1, "D"), T(LoadType.Live, 1, "L"), T(LoadType.Snow, 0.7, "0.7S"), T(LoadType.Earthquake, 1 / 1.4, "E/1.4") }),
                ("16-6", new[] { T(LoadType.Dead, 0.9, "0.9D"), T(LoadType.Earthquake, 1 / 1.4, "E/1.4") }),
            };
            var list = new List<LoadCombination>();
            var seen = new HashSet<string>();
            foreach (var (eq, terms) in eqs)
            {
                var dead = terms.Where(t => t.Type == LoadType.Dead).ToList();
                var variable = terms.Where(t => t.Type != LoadType.Dead).ToList();
                // subsets by size, then in the equation's order (so D, then D + L, then D + Lr, D + L + Lr)
                var subsets = Enumerable.Range(0, 1 << variable.Count)
                    .Select(mask => variable.Where((t, i) => (mask & (1 << i)) != 0).ToList())
                    .OrderBy(s => s.Count).ThenBy(s => string.Join(",", s.Select(t => variable.IndexOf(t))));
                foreach (var sub in subsets)
                {
                    if (sub.Count == 0 && dead.Any(d => d.Factor != 1)) continue;
                    var c = new LoadCombination { Equation = eq, Terms = dead.Concat(sub).ToList() };
                    string key = string.Join("|", c.Terms.Select(t => $"{t.Type}:{t.Factor:R}"));
                    if (seen.Add(key)) list.Add(c);
                }
            }
            return list;
        }
    }
}

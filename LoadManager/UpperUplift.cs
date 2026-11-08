using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>One beam end or upper column base that carries hold-downs from above: what it does on its support at worst.</summary>
    public sealed class UpperUpliftPoint
    {
        /// <summary>"beam BM1 start", "column C2 base" (the shear wall app's name for it).</summary>
        public string From { get; set; }
        /// <summary>The beam or column.</summary>
        public string Id { get; set; }
        public bool IsBeam { get; set; }
        /// <summary>What it bears on, and its connection (the shear wall app's, sized for the hold-down's tension alone).</summary>
        public string Onto { get; set; }
        public string Connector { get; set; }
        /// <summary>
        /// The least net force on its support over every combination with W and every wind case (lb, + it bears down, − it
        /// pulls up: the tie it needs), and where: the combination and the case.
        /// </summary>
        public double Least { get; set; }
        public string LeastIn { get; set; }
        /// <summary>The most it pulls up from the hold-downs alone at the same factor (lb, + up), for comparison: no dead load.</summary>
        public double HoldownAlone { get; set; }
        public bool Lifts => Least < -0.5;
    }

    /// <summary>
    /// The upper beams and columns carrying hold-downs from above (ROADMAP, step 5): at each beam end and each column base above
    /// the foundation, in every combination with W and every wind case, the net force on its support from its pairs: the
    /// gravity reaction there (the load path's, each load on its own), the roof's wind in the case (its uplift pass) and the
    /// hold-downs' pair at the combination's W factor (the shear wall app's, <see cref="StoryHoldown.Up"/>). Where it pulls
    /// up, the end must be tied down for that (the shear wall app sizes its connection for the hold-down's tension alone,
    /// no dead load); where the dead load holds it, it only bears. Only reads.
    /// </summary>
    public static class UpperUplift
    {
        public static List<UpperUpliftPoint> Run(Building b, GravityLoadPath.Result path, WindUplift uplift, LateralSection lateral, DesignMethod method = DesignMethod.Asd)
        {
            var list = new List<UpperUpliftPoint>();
            if (b == null || path == null || lateral?.Holdowns == null) return list;
            var combos = (method == DesignMethod.Asd ? LoadCombinations.Asd() : LoadCombinations.Asce7Strength())
                .Where(c => c.Has(LoadType.Wind) && c.Types.All(t => t == LoadType.Dead || t == LoadType.Live || t == LoadType.RoofLive || t == LoadType.Snow || t == LoadType.Wind))
                .ToList();
            foreach (var h in lateral.Holdowns.Where(h => h.Kind == "beam end" || h.Kind == "column base"))
            {
                bool beam = h.Kind == "beam end";
                var words = (h.From ?? "").Split(' ');
                if (words.Length < 3) continue;
                string id = words[1];
                bool start = words[2] == "start";
                PointLoad Gravity(GravityLoadPath.Result p)
                {
                    if (p == null) return null;
                    if (beam) return p.Beams.TryGetValue(id, out var m) ? (start ? m.StartReaction : m.EndReaction) : null;
                    return p.Columns.TryGetValue(id, out var c) ? c.Load : null;
                }
                if (!beam)
                {
                    var col = b.Columns.FirstOrDefault(c => c.Id == id);
                    if (col == null || (b.Level(col.BaseLevelId)?.Elevation ?? 0) <= 1e-6) continue; // on the foundation: the foundation's own check
                }
                var g = Gravity(path);
                if (g == null) continue;
                var pt = new UpperUpliftPoint { From = h.From, Id = id, IsBeam = beam, Onto = h.Onto, Connector = h.Connector, Least = double.MaxValue };
                foreach (var kv in h.Up ?? new Dictionary<string, double>())
                {
                    string roof = LateralSection.WindOf(kv.Key);
                    var run = uplift == null ? default : uplift.Runs.FirstOrDefault(r => r.Case?.Name == roof);
                    double roofWind = Gravity(run.Path)?.Wind ?? 0; // + down on the support
                    foreach (var c in combos)
                    {
                        double k = c.Factor(LoadType.Wind);
                        double bears = c.Factor(LoadType.Dead) * g.Dead + c.Factor(LoadType.Live) * g.Live + c.Factor(LoadType.RoofLive) * g.RoofLive
                                     + c.Factor(LoadType.Snow) * g.Snow + k * roofWind - k * kv.Value;
                        if (bears < pt.Least) { pt.Least = bears; pt.LeastIn = $"{c.Name} · W: {kv.Key}"; }
                        pt.HoldownAlone = Math.Max(pt.HoldownAlone, k * kv.Value);
                    }
                }
                if (pt.Least == double.MaxValue) continue;
                pt.Least = Math.Round(pt.Least, 1);
                list.Add(pt);
            }
            return list;
        }
    }
}

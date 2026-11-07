using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>The connection pairs of a project and whether every member balances the wind (<see cref="ConnectionBalance.Run"/>).</summary>
    public sealed class ConnectionBalanceResult
    {
        public List<ConnectionPair> Pairs { get; set; } = new List<ConnectionPair>();
        public List<string> Cases { get; set; } = new List<string>();
        /// <summary>Each member's free body with the wind on it (walls, beams, columns).</summary>
        public List<FreeBody> Bodies { get; } = new List<FreeBody>();
        /// <summary>The members that balance in every case.</summary>
        public List<string> Balanced { get; } = new List<string>();
        /// <summary>Wind case → the sum of the reactions on the foundation (lb, + up): 0 for the overturning couples alone.</summary>
        public Dictionary<string, double> OnFoundation { get; } = new Dictionary<string, double>();
        public List<FoundationProblem> Problems { get; } = new List<FoundationProblem>();
        /// <summary>The lateral design was saved before it kept the cases: nothing to check.</summary>
        public bool NoCases { get; set; }
    }

    /// <summary>
    /// The verification that every force of the wind is accounted for, member by member (WISE, as for the gravity loads): each
    /// wall's, beam's and column's free body with the wind on it (<see cref="FreeBody.WindOnly"/>: E the wind's shear on its
    /// top and the connections of the members standing on it, S its own connections and its sill), in every wind case: ΣFx,
    /// ΣFy and ΣM = 0. Every connection is a pair of equal and opposite forces (<see cref="ConnectionPairs"/>), so a force that
    /// lands on nothing, or one a member takes and does not pass on, shows here. Only reads.
    /// </summary>
    public static class ConnectionBalance
    {
        public static ConnectionBalanceResult Run(Building b, LateralSection lateral)
        {
            var r = new ConnectionBalanceResult { Pairs = ConnectionPairs.Of(lateral) };
            r.Cases = ConnectionPairs.Cases(r.Pairs);
            if (b == null || lateral == null || (r.Pairs.Count == 0 && (lateral.Walls == null || lateral.Walls.Count == 0))) return r;
            if (r.Cases.Count == 0) { r.NoCases = r.Pairs.Count > 0; return r; }

            foreach (var p in r.Pairs.Where(p => !p.Supported))
                r.Problems.Add(new FoundationProblem
                {
                    Id = p.Upper,
                    Message = $"{p.From} ({p.UpperKind} {p.Upper}) lands on nothing: its {p.MaxTension:N0} lb tension and {p.MaxCompression:N0} lb compression have nothing to take them.",
                });
            foreach (var c in r.Cases)
                r.OnFoundation[c] = Math.Round(r.Pairs.Where(p => p.OnFoundation).Sum(p => p.OnLower(c)), 1);

            var ids = r.Pairs.SelectMany(p => new[] { p.Upper, p.Lower })
                       .Concat((lateral.Walls ?? new List<ShearWallDesign>()).Where(d => d.ShearOnTop != null).Select(d => d.WallId))
                       .Where(x => x != null).Distinct().ToList();
            foreach (var id in ids)
            {
                var fb = FreeBody.WindOnly(b, id, lateral, r.Pairs);
                if (fb == null)
                {
                    r.Problems.Add(new FoundationProblem { Id = id, Message = $"{id}: the lateral design names it, but it is not in the model." });
                    continue;
                }
                r.Bodies.Add(fb);
                if (fb.Connections.Count == 0 && fb.Shears.Count == 0) { r.Balanced.Add(id); continue; }
                string why = r.Cases.Select(fb.WindOutOfBalance).FirstOrDefault(x => x != null);
                if (why == null) { r.Balanced.Add(id); continue; }
                int above = fb.Connections.Count(x => !x.Own), own = fb.Connections.Count(x => x.Own);
                r.Problems.Add(new FoundationProblem
                {
                    Id = id,
                    Message = $"The {fb.Kind} {id} does not balance the wind: {why} ({above} connections landing on it, {own} of its own, " +
                              $"{fb.Shears.Count(s => s.Part == FreeBodyPart.External)} wind shears on it).",
                });
            }
            return r;
        }
    }
}

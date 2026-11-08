using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>Whether every member, and the whole structure, is in equilibrium under the gravity loads (<see cref="GravityBalance.Run"/>).</summary>
    public sealed class GravityBalanceResult
    {
        /// <summary>Each wall's, beam's and column's free body, and the whole structure's (<see cref="Whole"/>).</summary>
        public List<FreeBody> Bodies { get; } = new List<FreeBody>();
        /// <summary>The members in equilibrium in every combination.</summary>
        public List<string> Balanced { get; } = new List<string>();
        public FreeBody Whole { get; set; }
        /// <summary>Why the whole structure is not in equilibrium (null: it is).</summary>
        public string WholeOutOfBalance { get; set; }
        public List<FoundationProblem> Problems { get; } = new List<FoundationProblem>();
    }

    /// <summary>
    /// The check that every gravity load is accounted for, member by member (WISE, as the wind's <see cref="ConnectionBalance"/>):
    /// each wall's, beam's and column's free body, and the whole structure's, in every gravity combination of the method:
    /// ΣFz = 0 and ΣM = 0 (<see cref="FreeBody.OutOfBalance"/>). A wall standing on a beam or a wall is a distributed load on
    /// it, step by step along its profile; a post, a beam end or a column a point load; each at its own place, so a load put
    /// in the wrong place shows in ΣM even when ΣFz is right. Only reads.
    /// </summary>
    public static class GravityBalance
    {
        public static GravityBalanceResult Run(Building b, LoadSection s, GravityLoadPath.Result path, DesignMethod method = DesignMethod.Asd)
        {
            var r = new GravityBalanceResult();
            if (b == null) return r;
            s = s ?? LoadSection.Read(b) ?? new LoadSection();
            path = path ?? GravityLoadPath.Run(b, s);
            var ids = b.Walls.Select(w => w.Id).Concat(b.Beams.Select(m => m.Id)).Concat(b.Columns.Select(c => c.Id)).ToList();
            foreach (var id in ids)
            {
                var fb = FreeBody.Of(b, s, path, id);
                if (fb == null) continue;
                r.Bodies.Add(fb);
                string why = fb.Combinations(method).Select(fb.OutOfBalance).FirstOrDefault(x => x != null);
                if (why == null) r.Balanced.Add(id);
                else r.Problems.Add(new FoundationProblem { Id = id, Message = $"The {fb.Kind} {id} is not in equilibrium under the gravity loads: {why}." });
            }
            if (ids.Count > 0)
            {
                r.Whole = FreeBody.OfBody(b, s, path, ids);
                r.WholeOutOfBalance = r.Whole?.Combinations(method).Select(r.Whole.OutOfBalance).FirstOrDefault(x => x != null);
                if (r.WholeOutOfBalance != null)
                    r.Problems.Add(new FoundationProblem { Id = null, Message = $"The whole structure is not in equilibrium under the gravity loads: {r.WholeOutOfBalance}." });
            }
            return r;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>
    /// Every connection a pair of equal and opposite forces, each member's free body with the wind on it (WISE), against a
    /// hand calculation: each balances, and a force not accounted for shows.
    /// </summary>
    [TestClass]
    public class ConnectionBalanceTests
    {
        /// <summary>
        /// W2 (Level 2 to the roof, 10 ft) on W1 (the ground to Level 2, 10 ft), both 20.375 ft along y = 0, 3 in posts: the
        /// hold-downs 0.25 ft in, the compression at the posts' centrelines 0.125 ft in, the arm 20.375 − 1.5 × 0.25 = 20 ft.
        /// Case C pushes the tops east. W2: 1000 lb on its top, M = 10,000 lb·ft, T = C = 10,000 / 20 = 500 lb. W1: 2000 lb
        /// on its top and W2's couple on it: (2000 × 10 + 10,000) / 20 = 1500 lb.
        /// </summary>
        private static (Building b, LateralSection lat, Wall w1, Wall w2) TwoStories()
        {
            var b = Building.New("Two");
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            var w1 = b.AddWall(b.Ground, l2, 0, 0, 20.375, 0);
            var w2 = b.AddWall(l2, rf, 0, 0, 20.375, 0);
            var lat = new LateralSection();
            StoryHoldown End(Wall w, Level top, Level onto, string to, bool west, double up) => new StoryHoldown
            {
                From = $"{w.Id} {(west ? "west" : "east")} end", WallId = w.Id, X = west ? 0.25 : 20.125, Y = 0,
                CompressionX = west ? 0.125 : 20.25, CompressionY = 0, LevelId = top.Id, OntoLevelId = onto.Id, Onto = to,
                Connector = "HDU2", Up = new Dictionary<string, double> { ["C"] = up },
            };
            lat.Holdowns.Add(End(w1, l2, b.Ground, "the foundation", true, 1500));
            lat.Holdowns.Add(End(w1, l2, b.Ground, "the foundation", false, -1500));
            lat.Holdowns.Add(End(w2, rf, l2, $"wall {w1.Id}", true, 500));
            lat.Holdowns.Add(End(w2, rf, l2, $"wall {w1.Id}", false, -500));
            lat.Walls.Add(new ShearWallDesign { WallId = w1.Id, LevelId = l2.Id, HeightFt = 10, ShearOnTop = new Dictionary<string, double> { ["C"] = 2000 } });
            lat.Walls.Add(new ShearWallDesign { WallId = w2.Id, LevelId = rf.Id, HeightFt = 10, ShearOnTop = new Dictionary<string, double> { ["C"] = 1000 } });
            return (b, lat, w1, w2);
        }

        /// <summary>
        /// Level 2's diaphragm in case C: its own wind 1000 lb east; W1's top pushes it back 2000 lb (the other half of W1's
        /// 2000 lb on its top), W2's sill pushes it 1000 lb east (W2's 1000 lb going down): 1000 − 2000 + 1000 = 0.
        /// </summary>
        [TestMethod]
        public void TheDiaphragm_ItsWindAndTheWallsHalves_Balance_ByHand()
        {
            var (b, lat, w1, w2) = TwoStories();
            var l2 = b.Levels.Single(l => l.Name == "Level 2");
            lat.Diaphragms.Add(new DiaphragmWind { LevelId = l2.Id, X = 10.1875, Y = 0, Fx = new Dictionary<string, double> { ["C"] = 1000 }, Fy = new Dictionary<string, double> { ["C"] = 0 } });
            var r = ConnectionBalance.Run(b, lat);
            CollectionAssert.Contains(r.DiaphragmsBalanced, l2.Id);
            var f = ConnectionBalance.DiaphragmForces(lat.Diaphragms[0], r.ShearPairs, "C");
            Assert.AreEqual(3, f.Count);
            Assert.IsTrue(f[0].External);
            Assert.AreEqual(1000, f[0].Fx, 1e-9);
            Assert.AreEqual(-2000, f.Single(x => x.From == $"{w1.Id} top").Fx, 1e-9, "the wall under it pushes back");
            Assert.AreEqual(1000, f.Single(x => x.From == $"{w2.Id} sill").Fx, 1e-9, "the wall above pushes on it");
            Assert.AreEqual(0, f.Sum(x => x.Fx), 1e-9);
            Assert.AreEqual(0, ShearPairs.Balance(lat.Diaphragms[0], r.ShearPairs, "C").fx, 1e-9, "the same sum as the check's");

            lat.Diaphragms[0].Fx["C"] = 1500;
            StringAssert.Contains(ConnectionBalance.Run(b, lat).Problems.Single().Message, "ΣFx = +500 lb in C");
        }

        [TestMethod]
        public void TwoStories_EachWallBalances_ByHand()
        {
            var (b, lat, w1, w2) = TwoStories();
            var r = ConnectionBalance.Run(b, lat);
            Assert.AreEqual(0, r.Problems.Count, string.Join("\n", r.Problems.Select(p => p.Message)));
            CollectionAssert.AreEquivalent(new[] { w1.Id, w2.Id }, r.Balanced);
            Assert.AreEqual(2, r.Pairs.Count(p => p.Internal), "W2 on W1: internal, equal and opposite");
            Assert.AreEqual(2, r.Pairs.Count(p => p.OnFoundation));
            Assert.AreEqual(0, r.OnFoundation["C"], 1e-6, "the couple alone: nothing net on the foundation");

            var upper = r.Bodies.Single(x => x.Id == w2.Id);
            Assert.AreEqual(1000, upper.Shears.Single(s => s.Part == FreeBodyPart.External).Of("C"), 1e-9, "E: the wind on its top");
            Assert.AreEqual(20, upper.Shears.Single(s => s.Part == FreeBodyPart.External).Z, 1e-9, "at the top of its story");
            Assert.AreEqual(-500, upper.Connections.Single(c => c.Pair.From.EndsWith("west end")).On("C"), 1e-9, "S: its west hold-down pulls it down");
            Assert.AreEqual(20.25, upper.Connections.Single(c => c.Pair.From.EndsWith("east end")).AlongIn("C"), 1e-9, "its east end presses at the post's centreline");
            // ΣM about its start: −500 × 0.25 + 500 × 20.25 − 1000 × 10 = 0
            Assert.AreEqual(0, upper.WindMomentImbalance("C"), 1e-6);

            var lower = r.Bodies.Single(x => x.Id == w1.Id);
            var fromAbove = lower.Connections.Where(c => !c.Own).ToList();
            Assert.AreEqual(2, fromAbove.Count);
            Assert.IsTrue(fromAbove.All(c => c.Part == FreeBodyPart.Internal), "W2's on W1: internal, where W1 is cut from W2");
            Assert.AreEqual(500, fromAbove.Single(c => c.Pair.From.EndsWith("west end")).On("C"), 1e-9, "the other half of the pair: W2's tension pulls W1 up");
            // ΣM: 500 × 0.25 − 500 × 20.25 − 1500 × 0.25 + 1500 × 20.25 − 2000 × 10 = 0
            Assert.AreEqual(0, lower.WindMomentImbalance("C"), 1e-6);
            Assert.AreEqual(0, lower.WindImbalance("C"), 1e-9);
            Assert.AreEqual(0, lower.WindShearImbalance("C"), 1e-9, "the sill takes the wind");

            // the whole structure, one body: W2 on W1 inside it (both halves, cancelling), the foundation's its supports
            Assert.IsNull(r.WholeOutOfBalance);
            Assert.AreEqual(4, r.Whole.Connections.Count(c => c.Inside), "the two pairs between W2 and W1, both halves");
            Assert.AreEqual(2, r.Whole.Connections.Count(c => !c.Inside && c.Part == FreeBodyPart.Support), "W1 on the foundation");
            Assert.IsFalse(r.Whole.Connections.Any(c => !c.Inside && c.Part == FreeBodyPart.Internal), "nothing cut");
            // W2 alone is cut from W1: its hold-downs are I
            Assert.IsTrue(upper.Connections.All(c => c.Part == FreeBodyPart.Internal && !c.Inside));
        }

        [TestMethod]
        public void AForceNotAccountedFor_Shows()
        {
            var (b, lat, w1, _) = TwoStories();
            lat.Holdowns[1].Up["C"] = -1000;   // W1's east end presses 1000, not 1500
            var r = ConnectionBalance.Run(b, lat);
            var p = r.Problems.Single(x => x.Id != null);
            Assert.IsNotNull(r.WholeOutOfBalance, "the whole structure does not balance either");
            Assert.AreEqual(2, r.Problems.Count);
            Assert.AreEqual(w1.Id, p.Id);
            StringAssert.Contains(p.Message, "ΣFz = -500 lb in C");
            Assert.IsFalse(r.Balanced.Contains(w1.Id));
        }

        [TestMethod]
        public void LandingOnNothing_Shows()
        {
            var (b, lat, _, w2) = TwoStories();
            lat.Holdowns[2].Onto = null;       // W2's west hold-down: nothing under it
            var r = ConnectionBalance.Run(b, lat);
            Assert.IsTrue(r.Problems.Any(x => x.Id == w2.Id && x.Message.Contains("lands on nothing")));
            Assert.IsFalse(r.Pairs[2].Supported);
        }

        /// <summary>
        /// W2's east hold-down lands on a post, C1, put at the hold-down (x = 20.125); the wall's east end presses at its end post's
        /// centreline, 0.125 ft further east (20.25), inside the same post. In C it presses 500 lb on C1 and C1's base 500 lb on
        /// the foundation. A column is a post: both act on its axis, so C1 balances (before, 500 × 0.125 = 62.5 lb·ft did not).
        /// W2's own free body still has its end pressing at its post's centreline.
        /// </summary>
        [TestMethod]
        public void AWallEndPressingOnAColumn_ActsOnItsAxis_TheColumnBalances()
        {
            var (b, lat, _, w2) = TwoStories();
            var l2 = b.Levels.Single(l => l.Name == "Level 2");
            var c1 = b.AddColumn(b.Ground, l2, 20.125, 0);
            lat.Holdowns[3].Onto = $"column {c1.Id}";   // W2's east end: on the post, not W1
            lat.Holdowns.Add(new StoryHoldown
            {
                From = $"column {c1.Id} base", X = 20.125, Y = 0, OntoLevelId = b.Ground.Id, Onto = "the foundation", Kind = "column base",
                Connector = "HDU2", Up = new Dictionary<string, double> { ["C"] = -500 },
            });
            lat.Holdowns[1].Up["C"] = -1000;  // W1's east end: W2's 500 now goes down C1, not W1
            var r = ConnectionBalance.Run(b, lat);

            var col = r.Bodies.Single(x => x.Id == c1.Id);
            var fromAbove = col.Connections.Single(c => !c.Own);
            Assert.AreEqual(-500, fromAbove.On("C"), 1e-9, "W2's end presses down on it");
            Assert.AreEqual(20.125, fromAbove.WhereIn("C").X, 1e-9, "on the column's axis, not the wall's post centreline");
            Assert.AreEqual(0, col.WindMomentImbalance("C"), 1e-9);
            Assert.IsFalse(r.Problems.Any(p => p.Id == c1.Id), string.Join("\n", r.Problems.Select(p => p.Message)));

            var upper = r.Bodies.Single(x => x.Id == w2.Id);
            Assert.AreEqual(20.25, upper.Connections.Single(c => c.Pair.From.EndsWith("east end")).WhereIn("C").X, 1e-9, "the wall's own: its post's centreline");
            Assert.AreEqual(0, upper.WindMomentImbalance("C"), 1e-6);
        }

        [TestMethod]
        public void SavedBeforeTheCases_NothingToCheck()
        {
            var (b, lat, _, _) = TwoStories();
            foreach (var h in lat.Holdowns) h.Up = null;
            foreach (var w in lat.Walls) w.ShearOnTop = null;
            var r = ConnectionBalance.Run(b, lat);
            Assert.IsTrue(r.NoCases);
            Assert.AreEqual(0, r.Problems.Count);
        }
    }
}

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
            Assert.IsTrue(fromAbove.All(c => c.Part == FreeBodyPart.External), "W2's on W1: external to W1");
            Assert.AreEqual(500, fromAbove.Single(c => c.Pair.From.EndsWith("west end")).On("C"), 1e-9, "the other half of the pair: W2's tension pulls W1 up");
            // ΣM: 500 × 0.25 − 500 × 20.25 − 1500 × 0.25 + 1500 × 20.25 − 2000 × 10 = 0
            Assert.AreEqual(0, lower.WindMomentImbalance("C"), 1e-6);
            Assert.AreEqual(0, lower.WindImbalance("C"), 1e-9);
            Assert.AreEqual(0, lower.WindShearImbalance("C"), 1e-9, "the sill takes the wind");
        }

        [TestMethod]
        public void AForceNotAccountedFor_Shows()
        {
            var (b, lat, w1, _) = TwoStories();
            lat.Holdowns[1].Up["C"] = -1000;   // W1's east end presses 1000, not 1500
            var r = ConnectionBalance.Run(b, lat);
            var p = r.Problems.Single();
            Assert.AreEqual(w1.Id, p.Id);
            StringAssert.Contains(p.Message, "ΣFy = -500 lb in C");
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

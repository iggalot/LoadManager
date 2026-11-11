using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>
    /// What carries what among a level's beams (<see cref="GravityLoadPath"/>): a stair opening's trimmers and headers, beams
    /// bearing on each other round a loop (reciprocal framing), two beams resting only on each other's ends (a mechanism),
    /// and a header hung at a trimmer's end. Each against a hand calculation, and the load all accounted for.
    /// </summary>
    [TestClass]
    public class FramingHierarchyTests
    {
        private const double Tol = 1e-6;

        /// <summary>Everything that lands on a level's walls: their line loads along them and the points on them (lb).</summary>
        private static double OnWalls(GravityLoadPath.Result r, Level lv) =>
            r.Values.Where(t => t.LevelId == lv.Id).Sum(t => t.Profile.Steps.Sum(st => st.Load.Dead * (st.B - st.A)) + t.Points.Sum(p => p.Load.Dead));

        private static Building Box(double length, double width, out Level l2, out Level roof)
        {
            var b = Building.New("Framing");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            l2 = b.AddLevel("Level 2", 10);
            roof = b.AddLevel("Roof", 20);
            b.AddWall(b.Ground, l2, 0, 0, length, 0);
            b.AddWall(b.Ground, l2, 0, width, length, width);
            b.AddWall(b.Ground, l2, 0, 0, 0, width);
            b.AddWall(b.Ground, l2, length, 0, length, width);
            return b;
        }

        /// <summary>
        /// A 30 × 20 ft floor, joists S-N at 16 in., D 10 psf, a stair opening x 10 to 14, y 6 to 14. Trimmers along the
        /// joists at x = 10 and 14 (S wall to N wall), headers across at y = 6 and 14 (trimmer to trimmer).
        /// A header carries 3 ft (half the way to the wall) over its 4 ft: 120 lb, 60 at each end, onto the trimmers; a
        /// trimmer takes nothing off the headers it carries (owner 2026-11-10). A trimmer carries 8 in. each side of it where
        /// the walls would have it: the whole 20 ft on the side away from the opening, and on the opening's side the 3 ft
        /// beside the floor at each end (the rest is the headers'): 0.667 × (20 + 6) = 17.33 sq ft, 173.33 lb. So each trimmer
        /// end (173.33 + 120) / 2 = 146.67 lb. The walls get the whole floor less the opening: (600 − 32) × 10 = 5680 lb,
        /// none counted twice.
        /// </summary>
        [TestMethod]
        public void StairOpening_TrimmersCarryTheirStrip_HeadersFrameIntoThem()
        {
            var b = Box(30, 20, out var l2, out var roof);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddOpening(b.Ground, roof, new Rect2(10, 6, 14, 14));
            var t1 = b.AddBeam(l2, 10, 0, 10, 20);
            var t2 = b.AddBeam(l2, 14, 0, 14, 20);
            var h1 = b.AddBeam(l2, 10, 6, 14, 6);
            var h2 = b.AddBeam(l2, 10, 14, 14, 14);
            Assert.AreEqual(16, b.Defaults.FloorJoistSpacing, 1e-9);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));

            foreach (var h in new[] { h1, h2 })
            {
                Assert.AreEqual(60, r.Beams[h.Id].StartReaction.Dead, Tol, h.Id);
                Assert.AreEqual(60, r.Beams[h.Id].EndReaction.Dead, Tol, h.Id);
            }
            Assert.AreEqual(t1.Id, r.Beams[h1.Id].StartOn);
            Assert.AreEqual(t2.Id, r.Beams[h1.Id].EndOn);
            foreach (var t in new[] { t1, t2 })
            {
                var bt = r.Beams[t.Id];
                Assert.AreEqual(4.0 / 3, bt.TributaryMax, Tol, "a joist spacing: 8 in. each side");
                Assert.AreEqual(2.0 / 3, bt.TributaryMin, Tol, "beside the opening: one side only");
                Assert.AreEqual(2, bt.Points.Count, "the two headers");
                Assert.AreEqual((520.0 / 3 + 120) / 2, bt.StartReaction.Dead, Tol, t.Id);
                Assert.AreEqual((520.0 / 3 + 120) / 2, bt.EndReaction.Dead, Tol, t.Id);
            }
            Assert.AreEqual(5680, OnWalls(r, l2), Tol, "the floor less the opening, each part once");
        }

        /// <summary>
        /// A girder's reactions from its own free body, every load at its place (not half to each end). A 30 × 20 ft floor,
        /// joists S-N at 16 in., D 10 psf. A beam (y = 6, x 0 to 15) from the W wall into a girder (x = 15, y 0 to 20) on the
        /// S and N walls; a wall upstairs standing on the girder from y = 12 to 18 (10 ft at 10 psf: 100 plf).
        /// The beam: 3 + 7 = 10 ft of floor, 100 plf, but not over its first 8 in.: the W wall takes its edge joist there (half
        /// of 16 in., owner 2026-11-11). So 100 × 14.333 = 1433.33 lb with its resultant at 7.833 ft: 748.52 at the girder,
        /// 684.86 at the wall. The girder (y from its start):
        /// <list type="bullet">
        /// <item>the beam's 748.52 lb at 6 ft: × 6 / 20 = 224.56 at its end, 523.96 at its start;</item>
        /// <item>the wall, 600 lb with its resultant at 15 ft: 600 × 15 / 20 = 450 at its end, 150 at its start;</item>
        /// <item>its strip of floor (8 in. each side where the walls would have it, not off the beam it carries): 1.333 ft
        /// wide from 0 to 3 (40 lb at 1.5), 0.667 ft from 3 to 13 (66.67 lb at 8), 1.333 ft from 13 to 20 (93.33 lb at
        /// 16.5): ΣM = 60 + 533.33 + 1540 = 2133.33 lb·ft, so 106.67 at its end and 93.33 at its start.</item>
        /// </list>
        /// Start 523.96 + 150 + 93.33 = 767.29 lb; end 224.56 + 450 + 106.67 = 781.22 lb.
        /// </summary>
        [TestMethod]
        public void Girder_ReactionsFromItsFreeBody_EachLoadAtItsPlace()
        {
            var b = Box(30, 20, out var l2, out var roof);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            var beam = b.AddBeam(l2, 0, 6, 15, 6);
            var girder = b.AddBeam(l2, 15, 0, 15, 20);
            b.AddWall(l2, roof, 15, 12, 15, 18);
            var s = new LoadSection { WallDead = 10 };
            s.Levels = new System.Collections.Generic.Dictionary<string, AreaLoad> { [l2.Id] = new AreaLoad { Dead = 10 } };
            var r = GravityLoadPath.Run(b, s);

            // by hand to 0.1 lb (the strip's edge at 8 in. falls inside a quarter-foot sample); the girder then exactly from it
            double q = 100 * (15 - 2.0 / 3), xc = (2.0 / 3 + 15) / 2;
            Assert.AreEqual(q * (15 - xc) / 15, r.Beams[beam.Id].StartReaction.Dead, 0.1);
            Assert.AreEqual(q * xc / 15, r.Beams[beam.Id].EndReaction.Dead, 0.1);
            Assert.AreEqual(q, r.Beams[beam.Id].StartReaction.Dead + r.Beams[beam.Id].EndReaction.Dead, 1e-6);
            double end = r.Beams[beam.Id].EndReaction.Dead;
            var g = r.Beams[girder.Id];
            Assert.AreEqual(6, g.Points.Single().At, Tol, "the beam lands 6 ft along the girder");
            Assert.AreEqual(600, g.Partials.Sum(p => p.Load.Dead * (p.B - p.A)), Tol, "the wall standing on it");
            Assert.AreEqual(end * 14 / 20 + 150 + 280.0 / 3, g.StartReaction.Dead, 1e-6);
            Assert.AreEqual(end * 6 / 20 + 450 + 320.0 / 3, g.EndReaction.Dead, 1e-6);
            // and its own free body: ΣF and ΣM about its start, by hand from the loads it holds
            double total = end + 600 + 200, moment = end * 6 + 600 * 15 + 6400.0 / 3;
            Assert.AreEqual(total, g.StartReaction.Dead + g.EndReaction.Dead, 1e-6);
            Assert.AreEqual(moment, g.EndReaction.Dead * 20, 1e-6);
        }

        /// <summary>
        /// Reciprocal framing: four beams round a square, pinwheel-fashion, each from a wall to partway along the next, the
        /// last onto the first (A on B on C on D on A). A wall standing on each, 10 ft high at 10 psf: 100 plf over 20 ft.
        /// Each beam has the next one's end at its middle, so its far end gets 1000 + R / 2 = R: R = 2000 lb, and its wall end
        /// 2000 + R − R = 2000. The loop is solved together, said so, and the walls get the whole 8000 lb.
        /// </summary>
        [TestMethod]
        public void Pinwheel_BeamsOnEachOtherRoundALoop_SolvedTogether()
        {
            var b = Box(30, 30, out var l2, out var roof);
            var beams = new[]
            {
                b.AddBeam(l2, 10, 0, 10, 20),   // A: S wall to B
                b.AddBeam(l2, 0, 20, 20, 20),   // B: W wall to C
                b.AddBeam(l2, 20, 30, 20, 10),  // C: N wall to D
                b.AddBeam(l2, 30, 10, 10, 10),  // D: E wall to A
            };
            foreach (var m in beams) b.AddWall(l2, roof, m.Start.X, m.Start.Y, m.End.X, m.End.Y);
            var r = GravityLoadPath.Run(b, new LoadSection { WallDead = 10 });

            for (int i = 0; i < 4; i++)
            {
                var me = r.Beams[beams[i].Id];
                Assert.AreEqual(beams[(i + 1) % 4].Id, me.EndOn, "each onto the next");
                Assert.AreEqual(2000, me.EndReaction.Dead, Tol, beams[i].Id);
                Assert.AreEqual(2000, me.StartReaction.Dead, Tol, beams[i].Id);
                // what lands on the next is exactly this one's reaction (the loop closed, not cut short)
                var on = r.Beams[beams[(i + 1) % 4].Id].Points.Single(p => p.From == beams[i].Id);
                Assert.AreEqual(me.EndReaction.Dead, on.Load.Dead, Tol);
                Assert.AreEqual(10, on.At, Tol, "at the middle of the next");
            }
            Assert.IsTrue(r.Notes.Any(n => n.Contains("reciprocal framing")), string.Join(" | ", r.Notes));
            Assert.AreEqual(8000, OnWalls(r, l2), Tol);
        }

        /// <summary>
        /// Two beams meeting end to end at (15, 10) with nothing under them: each rests on the other, a mechanism. The model
        /// is not valid (owner 2026-11-10): it is the model's error, naming both beams and their level, and nothing is
        /// calculated at all (not the rest of the building either).
        /// </summary>
        [TestMethod]
        public void TwoBeamsOnEachOthersEnds_WithNothingUnder_AMechanism()
        {
            var b = Box(30, 20, out var l2, out var roof);
            var a = b.AddBeam(l2, 0, 10, 15, 10);
            var c = b.AddBeam(l2, 15, 10, 30, 10);
            b.AddWall(l2, roof, 0, 10, 30, 10);
            var r = GravityLoadPath.Run(b, new LoadSection { WallDead = 10 });
            Assert.IsFalse(r.Calculated);
            var e = r.ModelErrors.Single(i => i.Code == "BeamNotHeldUp");
            CollectionAssert.AreEquivalent(new[] { a.Id, c.Id }, e.Ids);
            Assert.AreEqual(l2.Id, e.LevelId);
            Assert.AreEqual(0, r.Beams.Count + r.Count + r.Foundation.Count, "nothing calculated anywhere");
            Assert.IsTrue(r.Notes.Any(n => n.Contains("nothing is calculated")), string.Join(" | ", r.Notes));
        }

        /// <summary>
        /// A header hung near a trimmer's end: the trimmer's end 0.2 ft off the S wall (on it), the header's end 0.2 ft further
        /// along the trimmer (0.4 ft off the wall: not on it, and within 0.25 ft of the trimmer's end). It lands on the trimmer,
        /// at its place, and its reaction goes on to the trimmer's supports.
        /// </summary>
        [TestMethod]
        public void HeaderAtATrimmersEnd_GoesToTheTrimmersSupport()
        {
            var b = Box(30, 20, out var l2, out var roof);
            var t = b.AddBeam(l2, 10, 0.2, 10, 20);
            var h = b.AddBeam(l2, 0, 0.4, 10, 0.4);
            b.AddWall(l2, roof, 0, 0.4, 10, 0.4);
            var r = GravityLoadPath.Run(b, new LoadSection { WallDead = 10 });
            Assert.AreEqual(t.Id, r.Beams[h.Id].EndOn);
            Assert.AreEqual(500, r.Beams[h.Id].EndReaction.Dead, Tol);                       // 100 plf × 10 ft / 2
            // at its place, 0.2 ft along the 19.8 ft trimmer: nearly all onto the wall at the trimmer's start
            Assert.AreEqual(500 * 19.6 / 19.8, r.Beams[t.Id].StartReaction.Dead, Tol);
            Assert.AreEqual(500 * 0.2 / 19.8, r.Beams[t.Id].EndReaction.Dead, Tol);
        }
    }
}

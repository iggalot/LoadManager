using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>The free body of a whole wall, beam and column (WISE) against hand calculations: each one balances.</summary>
    [TestClass]
    public class FreeBodyTests
    {
        private const double Tol = 1e-6;

        /// <summary>
        /// A wall from the ground to the roof (19 ft), 30 ft long, Level 2 at 10 ft. Level 2 D 12 psf over 6 ft: 72 plf;
        /// roof D 15 over 10 ft: 150 plf; walls 10 psf. Free body of the whole wall: E 72 × 30 = 2160 at 10 ft and
        /// 150 × 30 = 4500 at 19 ft; W 10 × 19 × 30 = 5700; S under it 412 plf (72 + 150 + 9 × 10 above Level 2, 10 × 10 below)
        /// × 30 = 12,360 = 2160 + 4500 + 5700. Nothing between its own two stories: they are inside it.
        /// </summary>
        [TestMethod]
        public void WholeWall_TwoStories_FloorRoofWeightAndTheFoundation()
        {
            var b = Building.New("Two");
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 19);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var s = b.AddWall(b.Ground, rf, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 12, 30, 12);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 12 });
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 15 });
            LoadSection.SetWallDead(b, 10);

            var fb = FreeBody.Of(b, LoadSection.Read(b), null, s.Id);
            Assert.AreEqual("wall", fb.Kind);
            var ext = fb.Of(FreeBodyPart.External).ToList();
            Assert.AreEqual(2, ext.Count);
            Assert.AreEqual(72, ext.Single(f => f.Z == 10).Load.Dead, Tol);
            Assert.AreEqual(150, ext.Single(f => f.Z == 19).Load.Dead, Tol);
            Assert.AreEqual(5700, fb.Total(FreeBodyPart.Weight, null), Tol);
            var sup = fb.Of(FreeBodyPart.Support).Single();
            Assert.AreEqual(0, sup.Z, Tol);
            Assert.AreEqual(412, sup.Load.Dead, Tol);
            Assert.IsTrue(sup.Up);
            Assert.AreEqual(12360, fb.Total(FreeBodyPart.Support, null), Tol);
            Assert.AreEqual(0, fb.Imbalance(null), Tol);
            Assert.IsFalse(fb.Of(FreeBodyPart.Internal).Any());
            var c = fb.Combinations(DesignMethod.Asd);
            Assert.IsTrue(c.Count > 0);
            Assert.IsTrue(c.All(x => System.Math.Abs(fb.Imbalance(x)) < Tol));
        }

        /// <summary>
        /// The beam of GravityLoadPathTests.WallOnABeam_TwoStories: E the floor D 100, L 400 plf over 30 ft and the wall on it
        /// D 200, Lr 200 plf from 10 to 20 ft; S 2500 D, 6000 L, 1000 Lr at each end. In D + L: E 3000 + 12,000 + 2000 = 17,000,
        /// S 2 × (2500 + 6000) = 17,000. The column under its start: E 2500 D from the beam at Level 2, S the same at its base.
        /// </summary>
        [TestMethod]
        public void BeamAndColumn_Balance()
        {
            var b = Building.New("Two");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            b.AddWall(l2, rf, 10, 10, 20, 10);
            var bm = b.AddBeam(l2, 0, 10, 30, 10);
            var c1 = b.AddColumn(b.Ground, l2, 0, 10);
            b.AddColumn(b.Ground, l2, 30, 10);
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 10, RoofLive = 20 });
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10, Live = 40 });
            LoadSection.SetWallDead(b, 10);
            var s = LoadSection.Read(b);

            var fb = FreeBody.Of(b, s, null, bm.Id);
            Assert.AreEqual("beam", fb.Kind);
            var dl = LoadCombinations.Asd().First(x => x.Name == "D + L");
            // E the floor; I the wall standing on it (its reaction where the beam is cut from it) and the columns under its ends
            Assert.AreEqual(17000, fb.Forces.Where(f => !f.Up).Sum(f => f.Resultant(dl)), Tol);
            Assert.IsTrue(fb.Of(FreeBodyPart.External).Any() && fb.Of(FreeBodyPart.Internal).Any(f => !f.Up && f.Other != null), "the floor E, the wall on it I");
            Assert.AreEqual(17000, fb.HeldUp(dl), Tol);
            Assert.AreEqual(0, fb.Imbalance(dl), Tol);
            Assert.AreEqual(2, fb.Of(FreeBodyPart.Internal).Count(f => f.Up), "its ends on the columns: I, the columns' reactions");
            Assert.IsFalse(fb.Of(FreeBodyPart.Support).Any(), "nothing of the foundation's on a beam");
            Assert.AreEqual(0, fb.Total(FreeBodyPart.Weight, dl), Tol);

            var fc = FreeBody.Of(b, s, null, c1.Id);
            Assert.AreEqual(2500, fc.Of(FreeBodyPart.Internal).Single().Load.Dead, Tol, "the beam's end on it: I");
            Assert.AreEqual(bm.Id, fc.Of(FreeBodyPart.Internal).Single().Other);
            Assert.AreEqual(10, fc.Of(FreeBodyPart.Internal).Single().Z, Tol);
            Assert.AreEqual(2500, fc.Of(FreeBodyPart.Support).Single().Load.Dead, Tol, "the foundation under it: S");
            Assert.IsTrue(fc.Combinations(DesignMethod.Asd).All(x => System.Math.Abs(fc.Imbalance(x)) < Tol));

            // the beam and the column together: the beam's start on the column is a pair inside the body, both halves cancel
            var both = FreeBody.OfBody(b, s, null, new[] { bm.Id, c1.Id });
            Assert.AreEqual("body", both.Kind);
            Assert.AreEqual(2, both.InsideForces.Count(), "the beam's start reaction and its load on the column");
            Assert.AreEqual(0, both.Imbalance(dl), Tol);
            Assert.AreEqual(17000, both.HeldUp(dl), Tol, "held up by the foundation under the column and the other column");
            Assert.AreEqual(1, both.Of(FreeBodyPart.Support).Count());
            Assert.AreEqual(1, both.Of(FreeBodyPart.Internal).Count(f => f.Up), "the beam's end on the other column: the cut");
        }

        /// <summary>
        /// A beam with several loads on it, none of them uniform (owner: every load on a beam on its own, never the largest):
        /// the floor's share with a heavier zone over part of it, and the wall standing on it with a heavier roof zone over part
        /// of the wall. Its reactions are the statics of each load on its own: ΣFz = 0 and ΣM = 0 about its start in every
        /// combination; the wall's base onto it and its load from the wall are equal and opposite, place by place; the wall
        /// alone, the beam alone and the two together balance.
        /// </summary>
        [TestMethod]
        public void ABeamsLoads_EachOnItsOwn_NeverTheLargest()
        {
            var b = Building.New("Two");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            var wall = b.AddWall(l2, rf, 10, 10, 20, 10);
            var bm = b.AddBeam(l2, 0, 10, 30, 10);
            b.AddColumn(b.Ground, l2, 0, 10);
            b.AddColumn(b.Ground, l2, 30, 10);
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 10, RoofLive = 20 });
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10, Live = 40 });
            LoadSection.SetWallDead(b, 10);
            LoadSection.SetZones(b, new[]
            {
                new LoadZone { LevelId = rf.Id, Name = "Mech", X0 = 10, Y0 = 0, X1 = 14, Y1 = 20, Load = new AreaLoad { Dead = 40 } },
                new LoadZone { LevelId = l2.Id, Name = "Files", X0 = 20, Y0 = 0, X1 = 26, Y1 = 20, Load = new AreaLoad { Dead = 30, Live = 100 } },
            });
            var s = LoadSection.Read(b);
            var path = GravityLoadPath.Run(b, s);
            var r = path.Beams[bm.Id];
            Assert.IsTrue(r.FromLevelProfile.Steps.Select(x => x.Load.Dead).Distinct().Count() > 1, "the floor's share varies along the beam");
            Assert.IsTrue(r.Partials.Where(p => p.From == wall.Id).Select(p => p.Load.Dead).Distinct().Count() > 1, "the wall's load varies along it");

            var fb = FreeBody.Of(b, s, path, bm.Id);
            var wf = FreeBody.Of(b, s, path, wall.Id);
            var both = FreeBody.OfBody(b, s, path, new[] { bm.Id, wall.Id });
            foreach (var c in LoadCombinations.Asd().Where(x => x.Types.All(t => t == LoadType.Dead || t == LoadType.Live || t == LoadType.RoofLive)))
            {
                Assert.AreEqual(0, fb.Imbalance(c), Tol, $"{c.Name}: the beam, ΣFz");
                // ΣM about its start: each load at its own place, the end's reaction at L
                double m = fb.Forces.Where(f => !f.Up).Sum(f => f.Resultant(c) * (f.A + f.B) / 2);
                double rEnd = fb.Forces.Single(f => f.Up && f.A > 1).Resultant(c);
                Assert.AreEqual(m, rEnd * r.Span, 1.0, $"{c.Name}: the beam, ΣM about its start");
                Assert.AreEqual(0, wf.Imbalance(c), Tol, $"{c.Name}: the wall");
                Assert.AreEqual(0, both.Imbalance(c), Tol, $"{c.Name}: the two together");
                // the pair between them: the wall's base on the beam and the beam's load from it, equal and opposite
                double onBeam = fb.Forces.Where(f => f.Other == wall.Id).Sum(f => f.Resultant(c));
                double underWall = wf.Forces.Where(f => f.Other == bm.Id && f.Up).Sum(f => f.Resultant(c));
                Assert.AreEqual(onBeam, underWall, Tol, $"{c.Name}: the pair");
                double moOn = fb.Forces.Where(f => f.Other == wall.Id).Sum(f => f.Resultant(c) * (f.A + f.B) / 2);
                if (c.Name == "D")
                {
                    // by hand: the roof zone replaces the roof's dead over x 10 to 14, a 10 ft strip, and the wall's own 10 psf x 10 ft:
                    // 4 ft at 400 + 100 = 500 plf, 6 ft at 100 + 100 = 200 plf: 2000 + 1200 = 3200 lb, its centroid
                    // (2000 x 12 + 1200 x 17) / 3200 = 13.875 ft (the largest over the wall, 500 plf x 10 ft at 15 ft, was 5000 lb)
                    Assert.AreEqual(3200, onBeam, Tol, "D: the wall on the beam");
                    Assert.AreEqual(13.875, moOn / onBeam, 1e-6, "D: where it sits");
                }
            }
        }

        /// <summary>
        /// The two-story box of <see cref="BeamAndColumn_Balance"/>: the wall on Level 2 over the ground's wall. Alone, the
        /// lower wall is cut from the upper (I, down onto it); alone, the upper is cut from the lower (I, up under it); both
        /// together, the pair is inside: E, W and the foundation's S only, in equilibrium. All selected: the whole structure.
        /// </summary>
        [TestMethod]
        public void StackedWalls_TheCutMovesWithTheSelection()
        {
            var b = Building.New("Two");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var lo = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            var up = b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 10, RoofLive = 20 });
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10, Live = 40 });
            LoadSection.SetWallDead(b, 10);
            var s = LoadSection.Read(b);
            var d = LoadCombinations.Asd().First(x => x.Name == "D");

            var lower = FreeBody.Of(b, s, null, lo.Id);
            var onIt = lower.Of(FreeBodyPart.Internal).Single();
            Assert.AreEqual(up.Id, onIt.Other);
            Assert.IsFalse(onIt.Up, "the upper wall bears down on it");
            Assert.AreEqual(0, lower.Imbalance(d), Tol);

            var upper = FreeBody.Of(b, s, null, up.Id);
            var under = upper.Of(FreeBodyPart.Internal).Single();
            Assert.AreEqual(lo.Id, under.Other);
            Assert.IsTrue(under.Up, "the lower wall holds it up");
            Assert.IsFalse(upper.Of(FreeBodyPart.Support).Any(), "not on the foundation");
            Assert.AreEqual(onIt.Resultant(d), under.Resultant(d), Tol, "equal and opposite");

            var stack = FreeBody.OfBody(b, s, null, new[] { lo.Id, up.Id });
            Assert.AreEqual(2, stack.InsideForces.Count());
            Assert.IsFalse(stack.Of(FreeBodyPart.Internal).Any(), "nothing cut: E, W and S only");
            Assert.AreEqual(0, stack.Imbalance(d), Tol);
            Assert.AreEqual(lower.Total(FreeBodyPart.Support, d), stack.Total(FreeBodyPart.Support, d), Tol, "the foundation holds the stack");

            var all = FreeBody.OfBody(b, s, null, b.Walls.Select(w => w.Id));
            Assert.IsFalse(all.Of(FreeBodyPart.Internal).Any(), "the whole structure: no cut");
            Assert.AreEqual(0, all.Imbalance(d), Tol);
        }
    }
}

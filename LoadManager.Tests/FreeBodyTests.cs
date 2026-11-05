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
            Assert.AreEqual(17000, fb.Total(FreeBodyPart.External, dl), Tol);
            Assert.AreEqual(17000, fb.Total(FreeBodyPart.Support, dl), Tol);
            Assert.AreEqual(0, fb.Imbalance(dl), Tol);
            Assert.AreEqual(2, fb.Of(FreeBodyPart.Support).Count());
            Assert.AreEqual(0, fb.Total(FreeBodyPart.Weight, dl), Tol);

            var fc = FreeBody.Of(b, s, null, c1.Id);
            Assert.AreEqual(2500, fc.Of(FreeBodyPart.External).Single().Load.Dead, Tol);
            Assert.AreEqual(10, fc.Of(FreeBodyPart.External).Single().Z, Tol);
            Assert.AreEqual(2500, fc.Of(FreeBodyPart.Support).Single().Load.Dead, Tol);
            Assert.IsTrue(fc.Combinations(DesignMethod.Asd).All(x => System.Math.Abs(fc.Imbalance(x)) < Tol));
        }
    }
}

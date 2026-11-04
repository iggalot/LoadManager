using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>How the loads vary along each wall (the load path's profiles), against hand calculations.</summary>
    [TestClass]
    public class LoadProfileTests
    {
        private const double Tol = 1e-6;

        [TestMethod]
        public void Profile_PlusAndCombine()
        {
            var a = LoadProfile.Uniform(0, 10, new LineLoad { Dead = 100 });
            var b = LoadProfile.Uniform(4, 6, new LineLoad { Dead = 50, Live = 20 });
            var sum = a.Plus(b);
            Assert.AreEqual(3, sum.Steps.Count);
            Assert.AreEqual(150, sum.At(5).Dead, Tol);
            Assert.AreEqual(20, sum.At(5).Live, Tol);
            Assert.AreEqual(100, sum.At(8).Dead, Tol);
            Assert.AreEqual(100 * 10 + 50 * 2, sum.Total().Dead, Tol);
            var max = a.Combine(LoadProfile.Uniform(8, 12, new LineLoad { Dead = 300 }), LineLoad.Max);
            Assert.AreEqual(300, max.At(9).Dead, Tol);
            Assert.AreEqual(300, max.At(11).Dead, Tol);   // only the second there
            Assert.AreEqual(12, max.End, Tol);
            // moved onto a wall running the other way, 10 ft long: x → 10 − x, kept from 2 to 10
            var moved = LoadProfile.Uniform(0, 4, new LineLoad { Dead = 7 }).Moved(x => 10 - x, 2, 10);
            Assert.AreEqual(6, moved.Start, Tol);
            Assert.AreEqual(10, moved.End, Tol);
        }

        /// <summary>
        /// The one story with the interior wall from x 0 to 15 only (D 15, walls 10 psf, 10 ft): the S wall's strip is 6 ft under
        /// the interior wall (90 plf) and 10 ft past it (150 plf). At the foundation its own weight 100 plf is added all along:
        /// 190 then 250; total 190 × 15 + 250 × 15 = 6,600 lb.
        /// </summary>
        [TestMethod]
        public void Wall_ProfileFollowsTheTributary_ToTheFoundation()
        {
            var b = Building.New("One");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            var s = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(b.Ground, l2, 0, 12, 15, 12);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            LoadSection.SetWallDead(b, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var p = r[GravityLoadPath.Key(s.Id, l2.Id)].Profile;
            Assert.AreEqual(90, p.At(7).Dead, Tol);
            Assert.AreEqual(150, p.At(22).Dead, Tol);
            Assert.AreEqual(2, p.Steps.Count);
            Assert.AreEqual(15, p.Steps[0].B, 0.26);       // sampled every 0.25 ft
            var f = r.Foundation.Single(x => x.Id == s.Id);
            Assert.AreEqual(190, f.Profile.At(7).Dead, Tol);
            Assert.AreEqual(250, f.Profile.At(22).Dead, Tol);
            Assert.AreEqual(f.Line.Dead, f.Profile.Most().Dead, Tol);   // the largest is the line load
            Assert.AreEqual(6600, f.Profile.Total().Dead, 0.26 * 60);
        }

        /// <summary>
        /// A short wall on Level 2 standing on the first story's S wall, from x 20 to 30, carrying the roof (D 15) 10 ft deep: 150 plf
        /// and its own 10 × 9 = 90. On the S wall below that adds 240 plf from x 20 to 30 only (nothing from 0 to 20).
        /// </summary>
        [TestMethod]
        public void WallAbove_OnlyWhereItStands()
        {
            var b = Building.New("Two");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 19);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            var roofPlate = b.AddDiaphragm(rf, new Rect2(20, 0, 30, 10));
            BuildingModel.Edits.BuildingEdits.SetDiaphragmSpan(b, roofPlate.Id, SpanDirection.SouthNorth);
            var s1 = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(l2, rf, 30, 0, 20, 0);                 // drawn the other way
            b.AddWall(l2, rf, 20, 10, 30, 10);
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 15 });
            LoadSection.SetWallDead(b, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var a = r[GravityLoadPath.Key(s1.Id, l2.Id)];
            Assert.AreEqual(0, a.FromAboveProfile.At(10).Dead, Tol);
            Assert.AreEqual(75 + 90, a.FromAboveProfile.At(25).Dead, Tol);   // the roof 10 ft deep spans S-N: 5 ft on it, 75 plf
            Assert.AreEqual(20, a.FromAboveProfile.Start, Tol);
            Assert.AreEqual(30, a.FromAboveProfile.End, Tol);
            Assert.AreEqual(a.FromAbove.Dead, a.FromAboveProfile.Most().Dead, Tol);
        }
    }
}

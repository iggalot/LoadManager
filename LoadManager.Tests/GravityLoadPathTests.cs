using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>
    /// The gravity load path against hand calculations. A 30 ft (W-E) × 20 ft (S-N) plate: on Auto its framing spans the
    /// shorter way, S-N, so it bears on the W-E walls; an interior W-E wall at y = 12 ft.
    /// </summary>
    [TestClass]
    public class GravityLoadPathTests
    {
        private const double Tol = 1e-9;

        private static Building OneStory(out Level l2, out Wall s, out Wall i, out Wall n, out Wall w, double interiorTo = 30)
        {
            var b = Building.New("One");
            b.Roof.Overhang = 0;
            l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            s = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            n = b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            w = b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 30, 0, 30, 20);
            i = b.AddWall(b.Ground, l2, 0, 12, interiorTo, 12);
            return b;
        }

        [TestMethod]
        public void OneStory_TributaryWidths()
        {
            var b = OneStory(out var l2, out var s, out var i, out var n, out var w);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15, RoofLive = 20 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var rs = r[GravityLoadPath.Key(s.Id, l2.Id)];
            Assert.AreEqual(6, rs.TributaryMax, Tol);                  // 12 / 2 (the edge is on the wall)
            Assert.AreEqual(90, rs.Load.Dead, Tol);
            Assert.AreEqual(120, rs.Load.RoofLive, Tol);
            Assert.AreEqual(10, r[GravityLoadPath.Key(i.Id, l2.Id)].TributaryMax, Tol);   // 6 + 4
            Assert.AreEqual(150, r[GravityLoadPath.Key(i.Id, l2.Id)].Load.Dead, Tol);
            Assert.AreEqual(80, r[GravityLoadPath.Key(n.Id, l2.Id)].Load.RoofLive, Tol);  // 4 × 20
            Assert.AreEqual(0, r[GravityLoadPath.Key(w.Id, l2.Id)].Load.Dead, Tol);       // along the framing
            Assert.IsTrue(r.Values.All(x => x.Notes.Count == 0));
        }

        [TestMethod]
        public void PartialInteriorWall_TributaryVariesAlongTheWall()
        {
            var b = OneStory(out var l2, out var s, out _, out _, out _, interiorTo: 15);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            var rs = GravityLoadPath.Run(b, LoadSection.Read(b))[GravityLoadPath.Key(s.Id, l2.Id)];
            Assert.AreEqual(6, rs.TributaryMin, Tol);                  // under the interior wall
            Assert.AreEqual(10, rs.TributaryMax, Tol);                 // past its end: half of 20
            Assert.AreEqual(150, rs.Load.Dead, Tol);                   // the most along the wall
        }

        [TestMethod]
        public void SpanSetByHand_TurnsTheBearingWalls()
        {
            var b = OneStory(out var l2, out var s, out _, out _, out var w);
            BuildingModel.Edits.BuildingEdits.SetDiaphragmSpan(b, b.Diaphragms.Single().Id, SpanDirection.WestEast);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(0, r[GravityLoadPath.Key(s.Id, l2.Id)].Load.Dead, Tol);
            Assert.AreEqual(150, r[GravityLoadPath.Key(w.Id, l2.Id)].Load.Dead, Tol);      // 30 / 2 × 10
        }

        /// <summary>
        /// Two stories: Level 2 (10 ft) D 12, L 40; roof (19 ft) D 15, Lr 20, S 30; walls 10 psf. The interior wall is on
        /// the first story only. Roof on S2: tributary 10 → D 150, Lr 200, S 300. S1: floor 6 ft → D 72, L 240; plus S2 and
        /// its weight 10 × 9 = 90 → D 312, L 240, Lr 200, S 300.
        /// </summary>
        [TestMethod]
        public void TwoStories_WallsAboveComeDown()
        {
            var b = Building.New("Two");
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 19);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var s1 = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            var n1 = b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            var i1 = b.AddWall(b.Ground, l2, 0, 12, 30, 12);
            var s2 = b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 12, Live = 40 });
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 15, RoofLive = 20, Snow = 30 });
            LoadSection.SetWallDead(b, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));

            var top = r[GravityLoadPath.Key(s2.Id, rf.Id)].Load;
            Assert.AreEqual(150, top.Dead, Tol); Assert.AreEqual(200, top.RoofLive, Tol); Assert.AreEqual(300, top.Snow, Tol);
            var a = r[GravityLoadPath.Key(s1.Id, l2.Id)];
            Assert.AreEqual(72, a.FromLevel.Dead, Tol);
            Assert.AreEqual(312, a.Load.Dead, Tol);
            Assert.AreEqual(240, a.Load.Live, Tol);
            Assert.AreEqual(200, a.Load.RoofLive, Tol);
            Assert.AreEqual(300, a.Load.Snow, Tol);
            CollectionAssert.Contains(a.Above, s2.Id);
            Assert.AreEqual(288, r[GravityLoadPath.Key(n1.Id, l2.Id)].Load.Dead, Tol);    // 48 + 150 + 90
            Assert.AreEqual(400, r[GravityLoadPath.Key(i1.Id, l2.Id)].Load.Live, Tol);    // 10 ft × 40, nothing above
            Assert.AreEqual(0, r.Notes.Count);
        }

        [TestMethod]
        public void ContinuousWall_CarriesItsStoryAbove()
        {
            var b = Building.New("Two");
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 19);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var s = b.AddWall(b.Ground, rf, 0, 0, 30, 0);          // ground to roof
            b.AddWall(b.Ground, l2, 0, 12, 30, 12);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 12 });
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 15 });
            LoadSection.SetWallDead(b, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(150, r[GravityLoadPath.Key(s.Id, rf.Id)].Load.Dead, Tol);
            Assert.AreEqual(72 + 150 + 90, r[GravityLoadPath.Key(s.Id, l2.Id)].Load.Dead, Tol);
        }

        /// <summary>Gable, ridge W-E, 2 ft eaves: rafters span S-N; the south wall takes the 2 ft eave and half of 20 ft = 12 ft.</summary>
        [TestMethod]
        public void GableRoof_RaftersAcrossTheRidge_WithTheEaves()
        {
            var b = OneStory(out var l2, out var s, out var i, out _, out var w);
            b.Walls.Remove(i);
            b.Roof = new Roof { Shape = RoofShape.Gable, Ridge = RidgeAxis.WestEast, Pitch = 20, Overhang = 2 };
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var rs = r[GravityLoadPath.Key(s.Id, l2.Id)];
            Assert.AreEqual(12, rs.TributaryMax, Tol);
            Assert.AreEqual(180, rs.Load.Dead, Tol);
            Assert.AreEqual(0, rs.Notes.Count);                        // the eave is no surprise
            Assert.AreEqual(0, r[GravityLoadPath.Key(w.Id, l2.Id)].Load.Dead, Tol);
        }

        [TestMethod]
        public void MissingSupport_AllToTheEdge_Noted()
        {
            var b = Building.New("One");
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            var s = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            var rs = GravityLoadPath.Run(b, LoadSection.Read(b))[GravityLoadPath.Key(s.Id, l2.Id)];
            Assert.AreEqual(20, rs.TributaryMax, Tol);
            Assert.AreEqual(1, rs.Notes.Count);
        }

        [TestMethod]
        public void WallOnTheFloorFraming_Noted()
        {
            var b = OneStory(out var l2, out _, out _, out _, out _);
            var rf = b.AddLevel("Roof", 19);
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var odd = b.AddWall(l2, rf, 0, 8, 30, 8);                 // no wall under it at y = 8
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.IsTrue(r.Notes.Any(x => x.StartsWith(odd.Id)));
        }

        [TestMethod]
        public void Effective_HandEnteredWins()
        {
            var b = OneStory(out var l2, out var s, out _, out _, out _);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            var e = GravityLoadPath.Effective(b, LoadSection.Read(b), s.Id, l2.Id, out var c);
            Assert.AreEqual(90, e.Dead, Tol);
            Assert.AreSame(c.Load, e);
            LoadSection.SetWallTop(b, s.Id, l2.Id, new LineLoad { Dead = 500 });
            Assert.AreEqual(500, GravityLoadPath.Effective(b, LoadSection.Read(b), s.Id, l2.Id, out c).Dead, Tol);
            Assert.AreEqual(90, c.Load.Dead, Tol);                     // still worked out, for comparison
        }

        [TestMethod]
        public void AreaLoadsAndWallDead_SetRoundTripAndClear()
        {
            var b = OneStory(out var l2, out _, out _, out _, out _);
            Assert.IsTrue(LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 12, Live = 40 }));
            Assert.IsFalse(LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 12, Live = 40 }));
            Assert.IsFalse(LoadSection.SetLevel(b, "nope", new AreaLoad { Dead = 1 }));
            Assert.IsTrue(LoadSection.SetWallDead(b, 10));
            Assert.IsFalse(LoadSection.SetWallDead(b, 10));
            var back = ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building;
            var s = LoadSection.Read(back);
            Assert.AreEqual(40, s.Level(l2.Id).Live, Tol);
            Assert.AreEqual(10, s.WallDead, Tol);
            Assert.IsTrue(LoadSection.SetLevel(b, l2.Id, null));
            Assert.IsTrue(LoadSection.SetWallDead(b, 0));
            Assert.IsNull(b.GetData(LoadSection.Key));
        }
    }
}

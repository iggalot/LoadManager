using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>
    /// No area load is dropped (owner 2026-11-11): a strip of floor beside a wall along the framing is that wall's load, an eave
    /// past the end walls goes onto them, and every plate's area loads over its whole area are what its walls and beams carry
    /// (<see cref="GravityLoadPath.Result.AreaChecks"/>), else it is said.
    /// </summary>
    [TestClass]
    public class AreaAccountTests
    {
        private const double Tol = 1e-6;

        /// <summary>What a level's walls and beams carry from its floor or roof, all of it (lb).</summary>
        private static PointLoad Carried(GravityLoadPath.Result r, Level lv) =>
            r.Values.Where(w => w.LevelId == lv.Id).Select(w => w.FromLevelProfile.Total())
             .Concat(r.Beams.Values.Where(m => m.LevelId == lv.Id).Select(m => m.FromLevelProfile.Total()))
             .Aggregate(new PointLoad(), (a, x) => a + x);

        private static Building Box(double overhang, out Level l2, out Wall s, out Wall w)
        {
            var b = Building.New("Account");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = overhang;
            l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            s = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            w = b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 30, 0, 30, 20);
            return b;
        }

        /// <summary>
        /// A 30 × 20 ft roof, rafters S-N at 24 in., D 10, an interior wall at x = 12 along them: it carries its rafter, 1 ft each
        /// side, 20 plf; the end walls 1 ft each, 10 plf. The whole roof, 600 sq ft × 10 = 6000 lb, is carried, once.
        /// </summary>
        [TestMethod]
        public void AWallAlongTheRafters_CarriesItsStrip()
        {
            var b = Box(0, out var l2, out _, out var w);
            var i = b.AddWall(b.Ground, l2, 12, 0, 12, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(0, r.ModelErrors.Count);
            Assert.AreEqual(20, r[GravityLoadPath.Key(i.Id, l2.Id)].Load.Dead, Tol);
            Assert.AreEqual(2, r[GravityLoadPath.Key(i.Id, l2.Id)].TributaryMax, Tol);
            Assert.AreEqual(10, r[GravityLoadPath.Key(w.Id, l2.Id)].Load.Dead, Tol);
            Assert.AreEqual(6000, Carried(r, l2).Dead, 1e-6);
            var ac = r.AreaChecks.Single();
            Assert.AreEqual(600, ac.Area, Tol);
            Assert.AreEqual(6000, ac.Applied.Dead, Tol);
            Assert.IsTrue(ac.Ok, ac.ToString());
        }

        /// <summary>
        /// The same roof with 2 ft eaves all round, D 15, Lr 20: 34 × 24 = 816 sq ft. The S and N eaves are the S and N walls'
        /// (to the edge); the W and E rakes and their corners, past the S and N walls' ends, go onto the W and E walls: each
        /// carries its rafter (1 ft) and the 2 ft rake beside it, 3 ft × 15 = 45 plf, and the corners' 4 sq ft on its last foot
        /// at each end. In all 816 × 15 = 12,240 lb dead and 16,320 lb roof live, every foot carried once.
        /// </summary>
        [TestMethod]
        public void FlatRoofWithEaves_EveryFootCarried()
        {
            var b = Box(2, out var l2, out _, out var w);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15, RoofLive = 20 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(0, r.ModelErrors.Count);
            var ac = r.AreaChecks.Single();
            Assert.AreEqual(816, ac.Area, 1e-6);
            Assert.AreEqual(12240, ac.Applied.Dead, 1e-6);
            Assert.IsTrue(ac.Ok, ac.ToString());
            Assert.AreEqual(12240, Carried(r, l2).Dead, 1e-6);
            Assert.AreEqual(16320, Carried(r, l2).RoofLive, 1e-6);
            var rw = r[GravityLoadPath.Key(w.Id, l2.Id)];
            Assert.AreEqual(15 * (3 * 20 + 2 * 4), rw.FromLevelProfile.Total().Dead, 1e-6);
            Assert.AreEqual(45, rw.FromLevelProfile.At(10).Dead, 1e-6);
            Assert.IsFalse(r.Notes.Any(n => n.Contains("on no wall or beam")));
        }

        /// <summary>
        /// Two stories, the floor with a stair opening (x 10 to 14, y 6 to 14) framed by trimmers and headers, a hallway framed the
        /// same way with its own use (L 100 from y 16 to 20) and a zone (a 4 × 4 ft tub, D 40 in the corner): every plate's
        /// loads over its area, the opening out, are carried. The floor: (600 − 32) sq ft.
        /// </summary>
        [TestMethod]
        public void FloorWithOpeningUseAndZone_EveryFootCarried()
        {
            var b = Building.New("Floor");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 1.5;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            var d = b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            foreach (var lv in new[] { (b.Ground, l2), (l2, rf) })
            {
                b.AddWall(lv.Item1, lv.Item2, 0, 0, 30, 0);
                b.AddWall(lv.Item1, lv.Item2, 0, 20, 30, 20);
                b.AddWall(lv.Item1, lv.Item2, 0, 0, 0, 20);
                b.AddWall(lv.Item1, lv.Item2, 30, 0, 30, 20);
            }
            b.AddOpening(b.Ground, rf, new Rect2(10, 6, 14, 14));
            b.AddBeam(l2, 10, 0, 10, 20);
            b.AddBeam(l2, 14, 0, 14, 20);
            b.AddBeam(l2, 10, 6, 14, 6);
            b.AddBeam(l2, 10, 14, 14, 14);
            var hall = FramingAreas.Place(b, d, new Rect2(0, 16, 30, 20), SpanDirection.SouthNorth);
            FramingAreas.SetName(b, hall, "Hallway");
            LoadSection.SetUses(b, new[] { new AreaUse { Name = "Hallway", Load = new AreaLoad { Dead = 12, Live = 100 } } });
            LoadSection.SetZones(b, new[] { new LoadZone { LevelId = l2.Id, Name = "Tub", X0 = 26, Y0 = 0, X1 = 30, Y1 = 4, Load = new AreaLoad { Dead = 40, Live = 40 } } });
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 12, Live = 40 });
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 15, RoofLive = 20 });
            LoadSection.SetWallDead(b, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(0, r.ModelErrors.Count, string.Join("; ", r.ModelErrors.Select(x => x.Message)));
            Assert.IsTrue(r.AreaChecks.Count >= 2);
            Assert.IsTrue(r.AreaChecks.All(x => x.Ok), string.Join("\n", r.AreaChecks));
            var floor = r.AreaChecks.Where(x => x.LevelId == l2.Id).ToList();
            Assert.AreEqual(568, floor.Sum(x => x.Area), 1e-6);
            // by hand: 568 sq ft at D 12, less the tub's 16 at 12 plus at 40
            Assert.AreEqual(568 * 12 + 16 * (40 - 12), floor.Aggregate(new PointLoad(), (a, x) => a + x.Applied).Dead, 1e-6);
            // and live: 40 over the floor but the hallway's 120 sq ft at 100 and the tub's 16 at 40
            Assert.AreEqual(568 * 40 + 120 * 60, floor.Aggregate(new PointLoad(), (a, x) => a + x.Applied).Live, 1e-6);
            Assert.AreEqual(floor.Aggregate(new PointLoad(), (a, x) => a + x.Applied).Live, Carried(r, l2).Live, 1e-6);
            Assert.AreEqual(33 * 23 * 15, r.AreaChecks.Where(x => x.LevelId == rf.Id).Sum(x => x.Applied.Dead), 1e-6, "the roof, eaves in (the stair opening cuts the floor only)");
        }

        /// <summary>
        /// A roof with 2 ft eaves on the S and N walls only, nothing along the rafters to take the rakes past their ends (x −2 to
        /// 0 and 30 to 32): 2 × 2 × 24 = 96 of its 816 sq ft are on no wall. The account says so; nothing is quietly dropped.
        /// </summary>
        [TestMethod]
        public void RakesWithNothingUnderThem_TheAccountSaysSo()
        {
            var b = Building.New("Open ends");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 2;
            var l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(0, r.ModelErrors.Count, string.Join("; ", r.ModelErrors.Select(x => x.Message)));
            var ac = r.AreaChecks.Single();
            Assert.IsFalse(ac.Ok);
            Assert.AreEqual(96.0 / 816, ac.Off, 1e-6);
            Assert.AreEqual(720 * 15, ac.Carried.Dead, 1e-6);
            Assert.IsTrue(r.Notes.Any(n => n.Contains("is on no wall or beam")), string.Join("\n", r.Notes));
        }
    }
}

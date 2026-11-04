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

        // ------------------------------------------------------------------ beams and columns

        /// <summary>A flat-roofed one story, 30 (W-E) × 20 (S-N), framing S-N, the S and N walls; a beam along y = 10.</summary>
        private static Building BeamStory(out Level l2, out Wall s, out Beam bm)
        {
            var b = Building.New("Beam");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            s = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            bm = b.AddBeam(l2, 0, 10, 30, 10);
            return b;
        }

        /// <summary>
        /// D 10, Lr 20 psf; the beam takes 5 + 5 = 10 ft: 100 and 200 plf over 30 ft, so each end 1500 and 3000 lb, onto a
        /// post to the foundation. The S wall takes 5 ft: D 50.
        /// </summary>
        [TestMethod]
        public void Beam_OnColumns_ToTheFoundation()
        {
            var b = BeamStory(out var l2, out var s, out var bm);
            var c1 = b.AddColumn(b.Ground, l2, 0, 10);
            var c2 = b.AddColumn(b.Ground, l2, 30, 10);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10, RoofLive = 20 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var rb = r.Beams[bm.Id];
            Assert.AreEqual(10, rb.TributaryMax, Tol);
            Assert.AreEqual(1500, rb.StartReaction.Dead, Tol);
            Assert.AreEqual(3000, rb.EndReaction.RoofLive, Tol);
            Assert.AreEqual(c1.Id, rb.StartOn);
            Assert.AreEqual(c2.Id, rb.EndOn);
            Assert.AreEqual(1500, r.Columns[c1.Id].Load.Dead, Tol);
            Assert.AreEqual(3000, r.Columns[c2.Id].Load.RoofLive, Tol);
            Assert.AreEqual("the foundation", r.Columns[c1.Id].BaseOn);
            Assert.AreEqual(50, r[GravityLoadPath.Key(s.Id, l2.Id)].Load.Dead, Tol);
            Assert.AreEqual(0, rb.Notes.Count);
        }

        /// <summary>The same beam on the W and E walls: each wall gets its 1500 lb end as a concentrated load 10 ft along it.</summary>
        [TestMethod]
        public void Beam_OnWalls_ConcentratedLoads()
        {
            var b = BeamStory(out var l2, out _, out var bm);
            var w = b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            var e = b.AddWall(b.Ground, l2, 30, 0, 30, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var pw = r[GravityLoadPath.Key(w.Id, l2.Id)].Points.Single();
            Assert.AreEqual(bm.Id, pw.From);
            Assert.AreEqual(10, pw.At, Tol);
            Assert.AreEqual(1500, pw.Load.Dead, Tol);
            Assert.AreEqual(0, r[GravityLoadPath.Key(w.Id, l2.Id)].Load.Dead, Tol);     // the framing runs along it
            Assert.AreEqual(1500, r[GravityLoadPath.Key(e.Id, l2.Id)].Points.Single().Load.Dead, Tol);
        }

        /// <summary>
        /// Two stories (10 and 20 ft). Roof D 10, Lr 20; Level 2 D 10, L 40; walls 10 psf. On the roof the interior wall I
        /// (x 10 to 20, y = 10) takes 10 ft: D 100, Lr 200 plf. It stands on the beam at Level 2 (y = 10, 30 ft, on posts):
        /// the beam takes 10 ft of floor (D 100, L 400 plf) and I over 10 ft in the middle (D 100 + 10 × 10 = 200, Lr 200).
        /// Each end: D 100 × 15 + 200 × 10 / 2 = 2500, L 400 × 15 = 6000, Lr 200 × 10 / 2 = 1000. The S wall at Level 2: floor
        /// 5 ft (D 50, L 200) plus the S wall above (D 100, Lr 200) and its weight (100): D 250, L 200, Lr 200.
        /// </summary>
        [TestMethod]
        public void WallOnABeam_TwoStories()
        {
            var b = Building.New("Two");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var s1 = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            var i = b.AddWall(l2, rf, 10, 10, 20, 10);
            var bm = b.AddBeam(l2, 0, 10, 30, 10);
            var c1 = b.AddColumn(b.Ground, l2, 0, 10);
            b.AddColumn(b.Ground, l2, 30, 10);
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 10, RoofLive = 20 });
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10, Live = 40 });
            LoadSection.SetWallDead(b, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));

            Assert.AreEqual(100, r[GravityLoadPath.Key(i.Id, rf.Id)].Load.Dead, Tol);
            var rb = r.Beams[bm.Id];
            var p = rb.Partials.Single();
            Assert.AreEqual(i.Id, p.From);
            Assert.AreEqual(10, p.A, Tol);
            Assert.AreEqual(20, p.B, Tol);
            Assert.AreEqual(200, p.Load.Dead, Tol);
            Assert.AreEqual(2500, rb.StartReaction.Dead, Tol);
            Assert.AreEqual(2500, rb.EndReaction.Dead, Tol);
            Assert.AreEqual(6000, rb.StartReaction.Live, Tol);
            Assert.AreEqual(1000, rb.EndReaction.RoofLive, Tol);
            var col = r.Columns[c1.Id];
            Assert.AreEqual(2500, col.Load.Dead, Tol);
            Assert.AreEqual(6000, col.Load.Live, Tol);
            Assert.AreEqual(1000, col.Load.RoofLive, Tol);
            var rs = r[GravityLoadPath.Key(s1.Id, l2.Id)];
            Assert.AreEqual(250, rs.Load.Dead, Tol);
            Assert.AreEqual(200, rs.Load.Live, Tol);
            Assert.AreEqual(200, rs.Load.RoofLive, Tol);
            Assert.IsFalse(r.Notes.Any(x => x.StartsWith(i.Id)));       // it stands on the beam

            // the foundation: S1 brings its top load and its own weight (10 psf × 10 ft): D 350, L 200, Lr 200 plf; the most in
            // ASD is D + L + Lr (IBC 16-1) = 750 plf (ASCE 4a: 350 + 150 + 150 = 650). C1: 2500 + 6000 + 1000 = 9500 lb.
            var fs = r.Foundation.Single(x => x.Id == s1.Id);
            Assert.IsTrue(fs.IsWall);
            Assert.AreEqual(350, fs.Line.Dead, Tol);
            Assert.AreEqual(200, fs.Line.Live, Tol);
            var most = LoadCombinations.Largest(fs.Line).Value;
            Assert.AreEqual("D + L + Lr", most.combination.Name);
            Assert.AreEqual(750, most.total, Tol);
            var fc = r.Foundation.Single(x => x.Id == c1.Id);
            Assert.IsFalse(fc.IsWall);
            Assert.AreEqual(9500, LoadCombinations.Largest(fc.Point).Value.total, Tol);
            Assert.AreEqual(2, r.Foundation.Count(x => x.IsWall));     // S1 and N1 on the ground
            Assert.AreEqual(2, r.Foundation.Count(x => !x.IsWall));    // C1 and C2
        }

        /// <summary>
        /// A beam (y = 10, x 0 to 15) from the W wall into a girder (x = 15, y 0 to 20) on the S and N walls. D 10: the beam
        /// takes 10 ft, 100 plf over 15 ft, so 750 lb each end; the girder carries the 750 at its middle, 375 to each wall.
        /// </summary>
        [TestMethod]
        public void Beam_IntoAGirder()
        {
            var b = BeamStory(out var l2, out var s, out var unused);
            b.Beams.Remove(unused);
            var w = b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            var g = b.AddBeam(l2, 15, 0, 15, 20);
            var bm = b.AddBeam(l2, 0, 10, 15, 10);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(750, r.Beams[bm.Id].EndReaction.Dead, Tol);
            Assert.AreEqual(g.Id, r.Beams[bm.Id].EndOn);
            Assert.AreEqual(w.Id, r.Beams[bm.Id].StartOn);
            Assert.AreEqual(0, r.Beams[g.Id].FromLevel.Dead, Tol);        // the framing runs along it
            Assert.AreEqual(10, r.Beams[g.Id].Points.Single().At, Tol);
            Assert.AreEqual(375, r.Beams[g.Id].StartReaction.Dead, Tol);
            var ps = r[GravityLoadPath.Key(s.Id, l2.Id)].Points.Single();
            Assert.AreEqual(g.Id, ps.From);
            Assert.AreEqual(15, ps.At, Tol);
            Assert.AreEqual(375, ps.Load.Dead, Tol);
            Assert.AreEqual(750, r[GravityLoadPath.Key(w.Id, l2.Id)].Points.Single().Load.Dead, Tol);
        }

        /// <summary>A roof beam's 1500 lb end on the W wall upstairs comes down to the W wall under it, at the same place.</summary>
        [TestMethod]
        public void ConcentratedLoad_ComesDownTheWallUnder()
        {
            var b = Building.New("Post");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var w1 = b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(l2, rf, 0, 0, 0, 20);
            b.AddWall(l2, rf, 30, 0, 30, 20);
            b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            var bm = b.AddBeam(rf, 0, 10, 30, 10);
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 10 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var p = r[GravityLoadPath.Key(w1.Id, l2.Id)].Points.Single();
            Assert.IsTrue(p.From.StartsWith(bm.Id + " via "));
            Assert.AreEqual(10, p.At, Tol);
            Assert.AreEqual(1500, p.Load.Dead, Tol);
        }

        /// <summary>
        /// The roof framing spans W-E onto the upper W and E walls and a roof beam at x = 15 (S-N, on the upper S and N walls):
        /// the beam takes 15 ft, D 10 → 150 plf over 20 ft, 1500 lb each end. Under the upper S wall are two walls meeting at
        /// x = 15: the 1500 lb goes to one of them, not both.
        /// </summary>
        [TestMethod]
        public void ConcentratedLoad_AtAJointBelow_GoesToOneWall()
        {
            var b = Building.New("Joint");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            var d = b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            BuildingModel.Edits.BuildingEdits.SetDiaphragmSpan(b, d.Id, SpanDirection.WestEast);
            var sa = b.AddWall(b.Ground, l2, 0, 0, 15, 0);
            var sb = b.AddWall(b.Ground, l2, 15, 0, 30, 0);
            b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            b.AddWall(l2, rf, 0, 0, 0, 20);
            b.AddWall(l2, rf, 30, 0, 30, 20);
            var bm = b.AddBeam(rf, 15, 0, 15, 20);
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 10 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(1500, r.Beams[bm.Id].StartReaction.Dead, Tol);
            var below = r[GravityLoadPath.Key(sa.Id, l2.Id)].Points.Concat(r[GravityLoadPath.Key(sb.Id, l2.Id)].Points).ToList();
            Assert.AreEqual(1, below.Count);
            Assert.AreEqual(1500, below[0].Load.Dead, Tol);
            Assert.IsTrue(below[0].From.StartsWith(bm.Id + " via "));
        }

        /// <summary>
        /// A load entered by hand on the upper S wall (D 1000 plf) goes on down: the S wall under it takes 5 ft of floor
        /// (D 10 → 50) plus 1000 and the upper wall's weight (10 psf × 10 ft = 100): D 1150; at the foundation plus its own 100: D 1250.
        /// </summary>
        [TestMethod]
        public void HandEnteredLoad_GoesOnDown()
        {
            var b = Building.New("Entered");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 20);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var s1 = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddBeam(l2, 0, 10, 30, 10);
            var s2 = b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            LoadSection.SetWallDead(b, 10);
            LoadSection.SetWallTop(b, s2.Id, rf.Id, new LineLoad { Dead = 1000 });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.IsTrue(r[GravityLoadPath.Key(s2.Id, rf.Id)].Entered);
            var rs = r[GravityLoadPath.Key(s1.Id, l2.Id)];
            Assert.IsFalse(rs.Entered);
            Assert.AreEqual(1150, rs.Load.Dead, Tol);
            Assert.AreEqual(1250, r.Foundation.Single(f => f.Id == s1.Id).Line.Dead, Tol);
        }

        // ------------------------------------------------------------------ load zones

        /// <summary>
        /// The one story (framing S-N; S wall at y = 0, interior wall at y = 12, N at 20), D 15, L 40; a deck zone over x 0 to 10,
        /// D 15, L 60. The S wall's strip is y 0 to 6: in the deck L 60 × 6 = 360 (else 240), so the most is 360; the interior
        /// wall's strip y 6 to 16: L 60 × 10 = 600. Both meet the deck.
        /// </summary>
        [TestMethod]
        public void Zone_ReplacesTheLevelsLoadsWhereItIs()
        {
            var b = OneStory(out var l2, out var s, out var i, out var n, out _);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15, Live = 40 });
            Assert.IsTrue(LoadSection.SetZones(b, new[] { new LoadZone { LevelId = l2.Id, Name = "Deck", X0 = 10, Y0 = 20, X1 = 0, Y1 = 0, Load = new AreaLoad { Dead = 15, Live = 60 } } }));
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var rs = r[GravityLoadPath.Key(s.Id, l2.Id)];
            Assert.AreEqual(360, rs.Load.Live, Tol);
            Assert.AreEqual(90, rs.Load.Dead, Tol);
            CollectionAssert.AreEqual(new[] { "Deck" }, rs.Zones);
            Assert.AreEqual(600, r[GravityLoadPath.Key(i.Id, l2.Id)].Load.Live, Tol);
            Assert.AreEqual(4 * 60, r[GravityLoadPath.Key(n.Id, l2.Id)].Load.Live, Tol);
        }

        /// <summary>
        /// A corridor zone along the S wall, y 0 to 3 over the whole length, L 100 (D 15): the S wall's strip y 0 to 6 is half in it,
        /// L 3 × 100 + 3 × 40 = 420; the interior wall's strip (y 6 to 16) does not reach it: L 400, no zone.
        /// </summary>
        [TestMethod]
        public void Zone_PartOfTheStrip()
        {
            var b = OneStory(out var l2, out var s, out var i, out _, out _);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15, Live = 40 });
            LoadSection.SetZones(b, new[] { new LoadZone { LevelId = l2.Id, Name = "Corridor", X0 = 0, Y0 = 0, X1 = 30, Y1 = 3, Load = new AreaLoad { Dead = 15, Live = 100 } } });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(420, r[GravityLoadPath.Key(s.Id, l2.Id)].Load.Live, Tol);
            Assert.AreEqual(90, r[GravityLoadPath.Key(s.Id, l2.Id)].Load.Dead, Tol);
            Assert.AreEqual(400, r[GravityLoadPath.Key(i.Id, l2.Id)].Load.Live, Tol);
            Assert.AreEqual(0, r[GravityLoadPath.Key(i.Id, l2.Id)].Zones.Count);
        }

        /// <summary>
        /// A wall whose first sample point is within 0.01 ft of a zone's edge (the deck ends at x = 10, the wall runs from x = 9.87, so its first sample point is at 9.994) only touches it:
        /// it takes the level's L 40 × 10 = 400, not the deck's 600.
        /// </summary>
        [TestMethod]
        public void Zone_OnlyTouched_IsNotTaken()
        {
            var b = OneStory(out var l2, out _, out var i, out _, out _);
            b.Walls.Remove(i);
            var j = b.AddWall(b.Ground, l2, 9.87, 12, 30, 12);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Live = 40 });
            LoadSection.SetZones(b, new[] { new LoadZone { LevelId = l2.Id, Name = "Deck", X0 = 0, Y0 = 0, X1 = 10, Y1 = 20, Load = new AreaLoad { Live = 60 } } });
            var r = GravityLoadPath.Run(b, LoadSection.Read(b))[GravityLoadPath.Key(j.Id, l2.Id)];
            Assert.AreEqual(400, r.Load.Live, Tol);
            Assert.AreEqual(0, r.Zones.Count);
        }

        [TestMethod]
        public void Zones_SetRoundTripAndClear()
        {
            var b = OneStory(out var l2, out _, out _, out _, out _);
            var z = new LoadZone { LevelId = l2.Id, Name = " Deck ", X0 = 10, Y0 = 8, X1 = 2, Y1 = 1, Load = new AreaLoad { Live = 60, Dead = -5 } };
            Assert.IsTrue(LoadSection.SetZones(b, new[] { z, new LoadZone { LevelId = "nope", X1 = 1, Y1 = 1 }, new LoadZone { LevelId = l2.Id, X0 = 1, X1 = 1, Y1 = 1 } }));
            var back = LoadSection.Read(ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building);
            var only = back.Zones.Single();                               // the other two: no such level, no area
            Assert.AreEqual("Deck", only.Name);
            Assert.AreEqual(2, only.X0, Tol);
            Assert.AreEqual(10, only.X1, Tol);
            Assert.AreEqual(0, only.Load.Dead, Tol);
            Assert.AreEqual(60, only.Load.Live, Tol);
            Assert.IsFalse(LoadSection.SetZones(b, new[] { z }));         // the same again
            Assert.IsTrue(LoadSection.SetZones(b, null));
            Assert.IsNull(b.GetData(LoadSection.Key));
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
            Assert.AreEqual(90, c.Computed.Dead, Tol);                 // still worked out, for comparison
            Assert.IsTrue(c.Entered);
            Assert.AreEqual(500, c.Load.Dead, Tol);
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

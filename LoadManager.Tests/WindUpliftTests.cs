using System;
using System.Linq;
using ASCE7WindLoadCalculator;
using BuildingModel.Geometry;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>
    /// The load path's uplift pass (dead loads and the wind on the roof, 0.6D + 0.6W) against hand calculations, and the wind
    /// calculator's roof pressures on the model's plan. The one story of <see cref="GravityLoadPathTests"/>: 30 ft (W-E) × 20 ft
    /// (S-N), framing S-N, the S wall at y = 0, an interior wall at y = 12, the N wall at y = 20 (tributary 6, 10, 4 ft).
    /// </summary>
    [TestClass]
    public class WindUpliftTests
    {
        private const double Tol = 1e-6;

        private static Building OneStory(out Level l2, out Wall s, out Wall i, out Wall n)
        {
            var b = Building.New("One");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            s = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            n = b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 30, 0, 30, 20);
            i = b.AddWall(b.Ground, l2, 0, 12, 30, 12);
            return b;
        }

        private static RoofWindCase Uniform(double psf) => new RoofWindCase { Name = "uniform", PressureAt = (x, y) => psf };

        /// <summary>
        /// Roof D 15, L 40 (dropped: only dead load resists), 30 psf up everywhere. S wall, 6 ft: D 90, W −180, 0.6 × 90 − 0.6 × 180
        /// = −54 plf (net up). Interior, 10 ft: D 150, W −300, −90. N, 4 ft: D 60, W −120, −36.
        /// </summary>
        [TestMethod]
        public void UniformSuction_TributaryAndNet()
        {
            var b = OneStory(out var l2, out var s, out var i, out var n);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15, Live = 40 });
            var r = GravityLoadPath.Uplift(b, LoadSection.Read(b), Uniform(-30));
            var rs = r[GravityLoadPath.Key(s.Id, l2.Id)].Load;
            Assert.AreEqual(90, rs.Dead, Tol);
            Assert.AreEqual(-180, rs.Wind, Tol);
            Assert.AreEqual(0, rs.Live, Tol);
            Assert.AreEqual(-54, rs.Net, Tol);
            Assert.AreEqual(-90, r[GravityLoadPath.Key(i.Id, l2.Id)].Load.Net, Tol);
            Assert.AreEqual(-36, r[GravityLoadPath.Key(n.Id, l2.Id)].Load.Net, Tol);
            // the gravity pass is untouched: no wind, the live load there
            var g = GravityLoadPath.Run(b, LoadSection.Read(b))[GravityLoadPath.Key(s.Id, l2.Id)].Load;
            Assert.AreEqual(0, g.Wind, Tol);
            Assert.AreEqual(240, g.Live, Tol);
        }

        /// <summary>
        /// 40 psf up where x &lt; 10, 10 psf up elsewhere; roof D 15, but a zone x 20 to 30 with D 25. Along the S wall (6 ft):
        /// x &lt; 10: D 90, W −240, net −90; x 10 to 20: D 90, W −60, net −18; x &gt; 20: D 150, W −60, net +54. The most net uplift is
        /// at x &lt; 10, its dead and wind together (not the most dead, 150, with the most wind, −240).
        /// </summary>
        [TestMethod]
        public void VaryingWind_DeadAndWindKeptTogether()
        {
            var b = OneStory(out var l2, out var s, out _, out _);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            LoadSection.SetZones(b, new[] { new LoadZone { LevelId = l2.Id, Name = "Heavy", X0 = 20, Y0 = 0, X1 = 30, Y1 = 20, Load = new AreaLoad { Dead = 25 } } });
            var wind = new RoofWindCase { Name = "west end", PressureAt = (x, y) => x < 10 ? -40 : -10 };
            var rs = GravityLoadPath.Uplift(b, LoadSection.Read(b), wind)[GravityLoadPath.Key(s.Id, l2.Id)].Load;
            Assert.AreEqual(90, rs.Dead, Tol);
            Assert.AreEqual(-240, rs.Wind, Tol);
            Assert.AreEqual(-90, rs.Net, Tol);
        }

        /// <summary>
        /// Two stories, walls 10 psf: Level 2 (10 ft) D 12, roof (19 ft) D 15 and 30 psf up. The roof on S2 (10 ft): D 150, W −300.
        /// S1 (floor 6 ft): D 72 + 150 + S2's weight 10 × 9 = 90, so D 312, W −300: net 0.6 × 312 − 180 = +7.2 (held down). At the
        /// foundation S1's own weight 10 × 10 is added: D 412, net +67.2.
        /// </summary>
        [TestMethod]
        public void TwoStories_UpliftComesDownAgainstTheDeadLoad()
        {
            var b = Building.New("Two");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            var rf = b.AddLevel("Roof", 19);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddDiaphragm(rf, new Rect2(0, 0, 30, 20));
            var s1 = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(b.Ground, l2, 0, 12, 30, 12);
            var s2 = b.AddWall(l2, rf, 0, 0, 30, 0);
            b.AddWall(l2, rf, 0, 20, 30, 20);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 12, Live = 40 });
            LoadSection.SetLevel(b, rf.Id, new AreaLoad { Dead = 15, Snow = 30 });
            LoadSection.SetWallDead(b, 10);
            var r = GravityLoadPath.Uplift(b, LoadSection.Read(b), Uniform(-30));
            var top = r[GravityLoadPath.Key(s2.Id, rf.Id)].Load;
            Assert.AreEqual(150, top.Dead, Tol);
            Assert.AreEqual(-300, top.Wind, Tol);
            var a = r[GravityLoadPath.Key(s1.Id, l2.Id)].Load;
            Assert.AreEqual(312, a.Dead, Tol);
            Assert.AreEqual(-300, a.Wind, Tol);
            Assert.AreEqual(7.2, a.Net, Tol);
            var f = r.Foundation.Single(x => x.Id == s1.Id).Line;
            Assert.AreEqual(412, f.Dead, Tol);
            Assert.AreEqual(67.2, f.Net, Tol);
        }

        /// <summary>
        /// A beam along y = 10 on two posts, roof D 10 and 30 psf up: the beam takes 10 ft, D 100 and W −300 plf over 30 ft, each
        /// end D 1500, W −4500 lb; on each post 0.6 × 1500 − 0.6 × 4500 = −1800 lb (net up), down to the foundation.
        /// </summary>
        [TestMethod]
        public void Beam_ReactionsLiftThePosts()
        {
            var b = Building.New("Beam");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            var bm = b.AddBeam(l2, 0, 10, 30, 10);
            var c1 = b.AddColumn(b.Ground, l2, 0, 10);
            b.AddColumn(b.Ground, l2, 30, 10);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10, RoofLive = 20 });
            var r = GravityLoadPath.Uplift(b, LoadSection.Read(b), Uniform(-30));
            Assert.AreEqual(1500, r.Beams[bm.Id].StartReaction.Dead, Tol);
            Assert.AreEqual(-4500, r.Beams[bm.Id].StartReaction.Wind, Tol);
            Assert.AreEqual(0, r.Beams[bm.Id].StartReaction.RoofLive, Tol);
            Assert.AreEqual(-1800, r.Columns[c1.Id].Load.Net, Tol);
            Assert.AreEqual(-1800, r.Foundation.Single(x => x.Id == c1.Id).Point.Net, Tol);
        }

        /// <summary>A wall top entered by hand (D 50, L 100) keeps its dead load and takes the wind found for it: D 50, W −180.</summary>
        [TestMethod]
        public void HandEntered_KeepsItsDeadAndTakesTheWind()
        {
            var b = OneStory(out var l2, out var s, out _, out _);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            LoadSection.SetWallTop(b, s.Id, l2.Id, new LineLoad { Dead = 50, Live = 100 });
            var r = GravityLoadPath.Uplift(b, LoadSection.Read(b), Uniform(-30))[GravityLoadPath.Key(s.Id, l2.Id)];
            Assert.IsTrue(r.Entered);
            Assert.AreEqual(50, r.Load.Dead, Tol);
            Assert.AreEqual(0, r.Load.Live, Tol);
            Assert.AreEqual(-180, r.Load.Wind, Tol);
        }

        /// <summary>
        /// The S wall with a 2 ft eave: its strip is y −2 to 6. With 30 psf up on the roof and 50 up on the eave (x, y outside the
        /// plan): W = −(2 × 50 + 6 × 30) = −280 plf.
        /// </summary>
        [TestMethod]
        public void Eave_IsInTheStrip()
        {
            var b = OneStory(out var l2, out var s, out _, out _);
            b.Roof.Overhang = 2;
            var wind = new RoofWindCase { Name = "eave", PressureAt = (x, y) => y < 0 || y > 20 || x < 0 || x > 30 ? -50 : -30 };
            var rs = GravityLoadPath.Uplift(b, LoadSection.Read(b), wind)[GravityLoadPath.Key(s.Id, l2.Id)];
            Assert.AreEqual(8, rs.TributaryMax, Tol);
            Assert.AreEqual(-280, rs.Load.Wind, Tol);
        }

        /// <summary>
        /// Two whole-building cases, 20 and 35 psf up, and C&amp;C 45 psf up; roof D 15. The S wall (6 ft): the 35 psf case governs,
        /// 0.6 × 90 − 0.6 × 210 = −72 plf. Its tie: 0.6 × 90 − 0.6 × 270 = −108 plf, at 24 in. −216 lb a rafter.
        /// </summary>
        [TestMethod]
        public void Envelope_GoverningCase_AndTheTies()
        {
            var b = OneStory(out var l2, out var s, out _, out var n);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            var lo = Uniform(-20); lo.Name = "low";
            var hi = Uniform(-35); hi.Name = "high";
            var u = WindUplift.Run(b, LoadSection.Read(b), new[] { lo, hi }, a => Uniform(-45));
            var g = u.WallTop(GravityLoadPath.Key(s.Id, l2.Id));
            Assert.AreEqual("high", g.Case.Name);
            Assert.AreEqual(-72, g.Net, Tol);
            Assert.IsTrue(g.Lifts);
            var t = u.Ties[s.Id];
            Assert.AreEqual(-108, t.Line.Net, Tol);
            Assert.AreEqual(24, t.SpacingIn, Tol);
            Assert.AreEqual(-216, t.PerRafter, Tol);
            Assert.AreEqual(12, t.SpanFt, Tol);                     // to the interior wall
            Assert.AreEqual(48, t.Area, Tol);                       // 12 × max(2, 12 / 3)
            Assert.AreEqual(21, u.Ties[n.Id].Area, Tol);            // 8 × 8 / 3 = 21.3, rounded
            // the owner's rule: the C&C where it is higher than the MWFRS in the same place (here 45 > 35 psf)
            Assert.AreEqual("C&C", t.Governs);
            Assert.AreEqual(-72, t.Mwfrs.Net, Tol);
            Assert.AreEqual("high", t.MwfrsCase.Name);
            Assert.AreEqual(-108, t.Cladding.Net, Tol);
        }

        /// <summary>
        /// The same with C&amp;C 25 psf up, under the 35 psf whole-building case: the MWFRS governs the tie, 0.6 × 90 − 0.6 × 210 =
        /// −72 plf, −144 lb a rafter (owner: the worst case). With no C&amp;C at all, the MWFRS alone.
        /// </summary>
        [TestMethod]
        public void Ties_TheMwfrs_WhereItIsHigherThanTheCandC()
        {
            var b = OneStory(out var l2, out var s, out _, out _);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            var lo = Uniform(-20); lo.Name = "low";
            var hi = Uniform(-35); hi.Name = "high";
            var u = WindUplift.Run(b, LoadSection.Read(b), new[] { lo, hi }, a => Uniform(-25));
            var t = u.Ties[s.Id];
            Assert.AreEqual("MWFRS", t.Governs);
            Assert.AreEqual(-72, t.Line.Net, Tol);
            Assert.AreEqual(-144, t.PerRafter, Tol);
            Assert.AreEqual("high", t.Case.Name);
            Assert.AreEqual(0.6 * 90 - 0.6 * 150, t.Cladding.Net, Tol, "the C&C kept beside it");

            var none = WindUplift.Run(b, LoadSection.Read(b), new[] { lo, hi }, null);
            Assert.AreEqual("MWFRS", none.Ties[s.Id].Governs);
            Assert.IsNull(none.Ties[s.Id].Cladding);
            Assert.AreEqual(-72, none.Ties[s.Id].Line.Net, Tol);
        }

        /// <summary>
        /// The tie place by place along its wall (ROADMAP step 4): the C&amp;C −45 psf in a corner zone 6 ft from the west end,
        /// −20 beyond; the MWFRS −35 everywhere. S wall, 6 ft of roof on it, D 15 → 90 plf. 1 to 6 ft (the first foot is the W wall's edge rafter): the C&amp;C,
        /// 0.6 × 90 − 0.6 × 270 = −108 plf (the MWFRS there −72); 6 to 29 ft: the MWFRS, 0.6 × 90 − 0.6 × 210 = −72 (the C&amp;C −18). The
        /// whole tie its worst, −108 (C&amp;C).
        /// </summary>
        [TestMethod]
        public void Ties_PlaceByPlace_TheCandCInItsZone_TheMwfrsBeyond()
        {
            var b = OneStory(out var l2, out var s, out _, out _);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15 });
            var hi = Uniform(-35); hi.Name = "high";
            var cc = new RoofWindCase { Name = "C&C", PressureAt = (x, y) => x < 6 ? -45 : -20 };
            var u = WindUplift.Run(b, LoadSection.Read(b), new[] { hi }, a => cc);
            var t = u.Ties[s.Id];
            Console.WriteLine(string.Join("\n", t.Along));
            // its first and last foot: the W and E walls' edge rafters (owner 2026-11-11), nothing on the S wall there
            Assert.AreEqual(4, t.Along.Count, string.Join("; ", t.Along));
            Assert.AreEqual(0, t.Along[0].A, Tol); Assert.AreEqual(1, t.Along[0].B, 0.01); Assert.AreEqual(0, t.Along[0].Line.Net, Tol);
            Assert.AreEqual(29, t.Along[3].A, 0.01); Assert.AreEqual(30, t.Along[3].B, Tol); Assert.AreEqual(0, t.Along[3].Line.Net, Tol);
            Assert.AreEqual(1, t.Along[1].A, 0.01); Assert.AreEqual(6, t.Along[1].B, 0.01);
            Assert.AreEqual("C&C", t.Along[1].Governs); Assert.AreEqual(-108, t.Along[1].Line.Net, Tol);
            Assert.AreEqual(6, t.Along[2].A, 0.01); Assert.AreEqual(29, t.Along[2].B, 0.01);
            Assert.AreEqual("MWFRS", t.Along[2].Governs); Assert.AreEqual(-72, t.Along[2].Line.Net, Tol);
            Assert.AreEqual("high", t.Along[2].Case.Name);
            Assert.AreEqual(-216, t.Along[1].PerRafter(t.SpacingIn), Tol, "per rafter at 24 in");
            Assert.AreEqual("C&C", t.Governs); Assert.AreEqual(-108, t.Line.Net, Tol, "the whole tie: its worst place");
        }

        // ------------------------------------------------------------------ the wind calculator's pressures on the plan

        /// <summary>
        /// A flat roof 30 × 20 ft, 10 ft high, ASCE 7-16, 115 mph, exposure C, enclosed, the building's south-west corner at (5, 7)
        /// on the model's plan. qh Kd = 0.00256 × Kz 0.849 (2.01 (15/900)^(2/9.5), 15 ft, C) × Kd 0.85 × 115² = 24.43 psf. Fig. 27.3-1, h/L ≤ 0.5 both
        /// ways: 0 to h/2 from the windward edge Cp −0.9; G 0.85, GCpi +0.18: p = 24.43 × (−0.9 × 0.85 − 0.18) = −23.09 psf. The
        /// soffit of the windward eave: 0.8 × 0.85 × 24.43 = 16.61 psf up; the eave's top −0.9 × 0.85 × 24.43 = −18.69, so −35.30.
        /// With −GCpi the same zone: 24.43 × (−0.9 × 0.85 + 0.18) = −14.29. The cases are the wind calculator's own (212 for
        /// ASCE 7-16 Ch. 27: 16 + 32 + 32 + 128 + 4 minimum), each under its name; the minimum puts no pressure on the roof.
        /// </summary>
        [TestMethod]
        public void Cases_TheCalculatorsOwn_ZonesOnThePlan_HandCalc()
        {
            var data = new BuildingData { BuildingLength = 30, BuildingWidth = 20, BuildingHeight = 10, RoofType = RoofTypes.ROOF_TYPE_FLAT, EnclosureType = BuildingEnclosures.BLDG_ENCLOSED, OverhangWidth = 2 };
            var w = WindAnalysis.Run(ASCE7_Versions.ASCE_VER_7_16, new WindParameters_Base(), data);
            var box = new Rect2(5, 7, 35, 27);
            var own = WindCases.For(w);
            var cases = RoofWind.Cases(box, own);
            Assert.AreEqual(212, cases.Count);
            CollectionAssert.AreEqual(own.Select(c => c.Name).ToList(), cases.Select(c => c.Name).ToList(), "the calculator's cases, in its order");
            double qhKd = 0.00256 * 2.01 * Math.Pow(15.0 / 900, 2 / 9.5) * 0.85 * 115 * 115;
            double edge = qhKd * (-0.9 * 0.85 - 0.18);
            Assert.AreEqual(-23.09, edge, 0.01);
            RoofWindCase Case(string from, string gcpi = "+GCpi") => cases.Single(c => c.Name == $"Case 1, from {from}, roof Case A, {gcpi}");
            Assert.AreEqual(edge, Case("W").PressureAt(5 + 1, 17), 0.01);           // 1 ft in from the west edge
            Assert.AreEqual(edge, Case("E").PressureAt(35 - 1, 17), 0.01);          // mirrored
            Assert.AreEqual(edge, Case("S").PressureAt(20, 7 + 1), 0.01);           // the along-width calculation, turned
            Assert.AreEqual(edge, Case("N").PressureAt(20, 27 - 1), 0.01);
            Assert.AreEqual(qhKd * (-0.9 * 0.85 + 0.18), Case("W", "−GCpi").PressureAt(5 + 1, 17), 0.01, "−GCpi: its own case");
            Assert.AreEqual(-14.29, Case("W", "−GCpi").PressureAt(5 + 1, 17), 0.01);
            Assert.IsTrue(Case("W").PressureAt(35 - 1, 17) > edge + 1);             // the leeward end lifts less
            Assert.AreEqual(-0.9 * 0.85 * qhKd - 0.8 * 0.85 * qhKd, Case("W").PressureAt(5 - 1, 17), 0.01);   // windward eave, soffit too
            Assert.AreEqual(-35.30, Case("W").PressureAt(5 - 1, 17), 0.02);
            Assert.IsTrue(Case("W").PressureAt(35 + 1, 17) > Case("W").PressureAt(35 - 1, 17));            // leeward eave: no soffit, no internal
            var min = cases.Single(c => c.Name.StartsWith("Minimum (27.1.5), from W"));
            Assert.AreEqual(0, min.PressureAt(20, 17), 1e-12);
        }

        /// <summary>The C&amp;C case: any point of the roof lifts at least the 16 psf minimum, more at a corner than in the field.</summary>
        [TestMethod]
        public void Cladding_RoofSuction()
        {
            var data = new BuildingData { BuildingLength = 30, BuildingWidth = 20, BuildingHeight = 10, RoofType = RoofTypes.ROOF_TYPE_FLAT, EnclosureType = BuildingEnclosures.BLDG_ENCLOSED };
            var w = WindAnalysis.Run(ASCE7_Versions.ASCE_VER_7_16, new WindParameters_Base(), data);
            var cc = RoofWind.Cladding(new Rect2(0, 0, 30, 20), w.Cladding);
            Assert.IsNotNull(cc);
            double field = cc.PressureAt(15, 10), corner = cc.PressureAt(0.5, 0.5);
            Assert.IsTrue(field <= -16 + 1e-9);
            Assert.IsTrue(corner < field - 1);
        }

        /// <summary>
        /// Read at an area, the C&amp;C case matches the calculator's own tables at its component area (10 sq ft, the default), on
        /// the roof and on an eave, and lifts less at a larger area (GCp falls with area, Fig. 30.3-2A), not below the 16 psf minimum.
        /// </summary>
        [TestMethod]
        public void Cladding_AtAnArea_MatchesTheCalculator()
        {
            var data = new BuildingData { BuildingLength = 30, BuildingWidth = 20, BuildingHeight = 10, RoofType = RoofTypes.ROOF_TYPE_FLAT, EnclosureType = BuildingEnclosures.BLDG_ENCLOSED, OverhangWidth = 2 };
            foreach (var ed in new[] { ASCE7_Versions.ASCE_VER_7_16, ASCE7_Versions.ASCE_VER_7_22 })
            {
                var w = WindAnalysis.Run(ed, new WindParameters_Base(), data);
                var box = new Rect2(0, 0, 30, 20);
                var own = RoofWind.Cladding(box, w.Cladding);
                var at10 = RoofWind.Cladding(box, w.Cladding, 10);
                foreach (var (x, y) in new[] { (15.0, 10.0), (0.5, 0.5), (0.5, 10.0), (-1.0, 10.0), (-1.0, -1.0) })
                    Assert.AreEqual(own.PressureAt(x, y), at10.PressureAt(x, y), 1e-9, $"{ed} at ({x}, {y})");
                var at100 = RoofWind.Cladding(box, w.Cladding, 100);
                Assert.IsTrue(at100.PressureAt(0.5, 0.5) > at10.PressureAt(0.5, 0.5) + 1, ed.ToString());
                Assert.IsTrue(at100.PressureAt(15, 10) <= -16 + 1e-9);
            }
        }
    }
}

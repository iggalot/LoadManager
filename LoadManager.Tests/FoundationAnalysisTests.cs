using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>
    /// The foundation's combined loads and envelope against hand calculations. The one story of <see cref="WindUpliftTests"/>: 30 × 20 ft,
    /// framing S-N, walls at y = 0, 12 and 20 (the S wall's strip 6 ft), 10 ft high, walls 10 psf; roof D 15, Lr 20. The S wall at its
    /// base: D 6 × 15 + 10 × 10 = 190 plf, Lr 6 × 20 = 120 plf.
    /// </summary>
    [TestClass]
    public class FoundationAnalysisTests
    {
        private const double Tol = 1e-6;

        private static Building OneStory(out Wall s)
        {
            var b = Building.New("One");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            var l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            s = b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            b.AddWall(b.Ground, l2, 0, 0, 0, 20);
            b.AddWall(b.Ground, l2, 30, 0, 30, 20);
            b.AddWall(b.Ground, l2, 0, 12, 30, 12);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 15, RoofLive = 20 });
            LoadSection.SetWallDead(b, 10);
            return b;
        }

        /// <summary>
        /// Gravity only. ASD: the most is D + Lr = 190 + 120 = 310 plf, the least 0.6D (Eq. 7a with W set to zero), 114. Strength: 1.2D + 1.6Lr = 228 + 192 =
        /// 420 (more than 1.4D = 266); the least 0.9D (Eq. 5a, W zero), 171. Combinations with L, W, E and so on are left out.
        /// </summary>
        [TestMethod]
        public void Gravity_AsdAndStrength()
        {
            var b = OneStory(out var s);
            var asd = FoundationAnalysis.Run(b, LoadSection.Read(b), null, null, DesignMethod.Asd);
            var w = asd.Walls.Single(x => x.Id == s.Id);
            Assert.AreEqual(1, w.Segments.Count);
            Assert.AreEqual(190, w.Segments[0].Load.Dead, Tol);
            Assert.AreEqual(120, w.Segments[0].Load.RoofLive, Tol);
            Assert.AreEqual(310, w.Line.Max, Tol);
            Assert.AreEqual("D + Lr", w.Line.MaxBy.Name);
            Assert.AreEqual(114, w.Line.Min, Tol);
            Assert.IsFalse(asd.Combinations.Any(c => c.Has(LoadType.Wind) || c.Has(LoadType.Live)));
            Assert.AreEqual(30 * 190, w.Total().Dead, Tol);

            var lrfd = FoundationAnalysis.Run(b, LoadSection.Read(b), null, null, DesignMethod.Strength);
            var u = lrfd.Walls.Single(x => x.Id == s.Id);
            Assert.AreEqual(420, u.Line.Max, Tol);
            Assert.AreEqual(171, u.Line.Min, Tol);
        }

        /// <summary>
        /// The roof lifted 30 psf: on the S wall W = −6 × 30 = −180 plf. ASD: 0.6D + 0.6W = 114 − 108 = 6 plf (still down); the most
        /// is still D + Lr, 310 (the wind set to zero). Strength: 0.9D + 1.0W = 171 − 180 = −9 plf, uplift.
        /// </summary>
        [TestMethod]
        public void RoofWind_UpliftOnTheFooting()
        {
            var b = OneStory(out var s);
            var sec = LoadSection.Read(b);
            var up = WindUplift.Run(b, sec, new[] { new RoofWindCase { Name = "uniform", PressureAt = (x, y) => -30 } }, null);
            var asd = FoundationAnalysis.Run(b, sec, up, null, DesignMethod.Asd).Walls.Single(x => x.Id == s.Id);
            Assert.AreEqual(-180, asd.Segments[0].Load.WindMin, Tol);
            Assert.AreEqual(6, asd.Line.Min, Tol);
            Assert.AreEqual("0.6D + 0.6W", asd.Line.MinBy.Combination.Name);
            Assert.AreEqual("uniform", asd.Line.MinBy.Wind.Name);
            Assert.AreEqual(310, asd.Line.Max, Tol);
            var lrfd = FoundationAnalysis.Run(b, sec, up, null, DesignMethod.Strength).Walls.Single(x => x.Id == s.Id);
            Assert.AreEqual(-9, lrfd.Line.Min, Tol);
            Assert.IsTrue(lrfd.Line.Lifts);
        }

        /// <summary>
        /// A shear wall's end at the S wall's start: 1,000 lb up in case A, 1,000 down in case B; no post there (no dead load at the
        /// point). ASD: 0.6W = −600 the least, D + 0.6W = +600 the most. Strength: ±1,000. One with nothing drawn under it stands alone.
        /// </summary>
        [TestMethod]
        public void Lateral_HoldDownOnTheWallFooting()
        {
            var b = OneStory(out var s);
            var lat = new LateralSection
            {
                Source = "test", Cases = new List<string> { "A", "B" },
                Points =
                {
                    new LateralPoint { From = "SW1 end", WallId = s.Id, X = 0.2, Y = 0, Up = new Dictionary<string, double> { ["A"] = 1000, ["B"] = -1000 } },
                    new LateralPoint { From = "lost", X = 50, Y = 50, Up = new Dictionary<string, double> { ["A"] = 10 } },
                },
            };
            // the format reads back as written
            var back = Building.New("Back");
            LateralSection.Write(back, lat);
            Assert.AreEqual(1000, LateralSection.Read(back).Points[0].Up["A"], Tol);

            var r = FoundationAnalysis.Run(b, LoadSection.Read(b), null, lat, DesignMethod.Asd);
            var p = r.Walls.Single(x => x.Id == s.Id).Points.Single();
            Assert.AreEqual(FoundationPointKind.Lateral, p.Kind);
            Assert.AreEqual(0.2, p.At, Tol);
            Assert.AreEqual(-1000, p.Lateral["A"], Tol);
            Assert.AreEqual(-600, p.Envelope.Min, Tol);
            Assert.AreEqual(600, p.Envelope.Max, Tol);
            Assert.AreEqual("lost", r.Points.Single().From);
            Assert.IsTrue(r.Notes.Any(n => n.StartsWith("lost")));
            var u = FoundationAnalysis.Run(b, LoadSection.Read(b), null, lat, DesignMethod.Strength).Walls.Single(x => x.Id == s.Id).Points.Single();
            Assert.AreEqual(-1000, u.Envelope.Min, Tol);
            Assert.AreEqual(1000, u.Envelope.Max, Tol);
            Assert.IsNull(lat.Stale(b));
        }

        /// <summary>
        /// Each of the wind calculator's cases on its own, the shear walls' forces in the case of the same name (no pairing). Roof: case
        /// A 30 psf up (the S wall −6 × 30 = −180 plf), case B 10 psf up (−60 plf). A shear wall end at the S wall's start: case A
        /// 1,000 lb up, case B 1,000 lb down. Two wind cases, A and B. ASD 0.6D + 0.6W on the footing: A 114 − 0.6 × 180 = 6 plf,
        /// B 114 − 0.6 × 60 = 78 plf; the least 6, in case A. At the end: A 0.6 × −1000 = −600 lb, B +600 (D + 0.6W). Every
        /// combination with W twice; one without W once. A saved shear wall case the calculator does not have ("Z") is left out,
        /// with a note; without the calculator's cases, the shear walls' alone.
        /// </summary>
        /// <summary>
        /// The whole chain on the 30 × 20 ft box: the wind calculator's own 212 cases (ASCE 7-16 Ch. 27) down the load path, the
        /// foundation taking every one, by name, in its order; in Case 1 from W, roof Case A, +GCpi the S wall's footing is lifted
        /// all along. The cases run in parallel give exactly what each gives alone. Prints the time taken (the load manager runs
        /// this after every change).
        /// </summary>
        [TestMethod]
        public void WindCases_FullChain_EveryCalculatorCase()
        {
            var b = OneStory(out var s);
            var sec = LoadSection.Read(b);
            var data = new ASCE7WindLoadCalculator.BuildingData { BuildingLength = 30, BuildingWidth = 20, BuildingHeight = 10, RoofType = ASCE7WindLoadCalculator.RoofTypes.ROOF_TYPE_FLAT, EnclosureType = ASCE7WindLoadCalculator.BuildingEnclosures.BLDG_ENCLOSED };
            var w = ASCE7WindLoadCalculator.WindAnalysis.Run(ASCE7WindLoadCalculator.ASCE7_Versions.ASCE_VER_7_16, new ASCE7WindLoadCalculator.WindParameters_Base(), data);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var roof = RoofWind.Cases(new Rect2(0, 0, 30, 20), ASCE7WindLoadCalculator.WindCases.For(w));
            var up = WindUplift.Run(b, sec, roof, null);
            var r = FoundationAnalysis.Run(b, sec, up, null, DesignMethod.Asd);
            watch.Stop();
            System.Console.WriteLine($"212 cases: {watch.ElapsedMilliseconds} ms");
            Assert.AreEqual(212, r.WindCases.Count);
            CollectionAssert.AreEqual(roof.Select(c => c.Name).ToList(), r.WindCases.Select(c => c.Name).ToList());
            var seg = r.Walls.Single(x => x.Id == s.Id).Segments;
            var c1 = r.WindCases.Single(c => c.Name == "Case 1, from W, roof Case A, +GCpi");
            Assert.IsTrue(seg.All(g => g.Load.Wind(c1.Name) < 0), "the roof lifts in that case");

            // the cases run side by side give exactly what each gives on its own, in the calculator's order
            for (int i = 0; i < roof.Count; i++)
            {
                Assert.AreSame(roof[i], up.Runs[i].Case);
                var alone = GravityLoadPath.Uplift(b, sec, roof[i]);
                foreach (var f in alone.Foundation.Where(x => x.IsWall))
                {
                    var g = up.Runs[i].Path.Foundation.Single(x => x.IsWall && x.Id == f.Id);
                    foreach (var at in new[] { 0.1, f.Length / 2, f.Length - 0.1 })
                        Assert.AreEqual(f.Profile.At(at).Wind, g.Profile.At(at).Wind, 0, $"{roof[i].Name}, {f.Id} at {at}");
                }
            }
        }

        /// <summary>
        /// The shear walls share each wind two ways (the diaphragm rigid and flexible), saved apart (owner 2026-11-07: the same
        /// wind, two analyses, never mixed): the wind case is taken once for each, with the same roof pressures.
        /// </summary>
        [TestMethod]
        public void WindCases_OncePerWayTheWallsShareIt_TheSameRoof()
        {
            var b = OneStory(out var s);
            var sec = LoadSection.Read(b);
            var up = WindUplift.Run(b, sec, new[] { new RoofWindCase { Name = "A", From = "W", PressureAt = (x, y) => -30 } }, null);
            var lat = new LateralSection
            {
                Source = "test", Cases = new List<string> { "A (rigid)", "A (flexible)" },
                Points = { new LateralPoint { From = "SW1 end", WallId = s.Id, X = 0.2, Y = 0, Up = new Dictionary<string, double> { ["A (rigid)"] = 1000, ["A (flexible)"] = 1400 } } },
            };
            var r = FoundationAnalysis.Run(b, sec, up, lat, DesignMethod.Asd);
            CollectionAssert.AreEqual(new[] { "A (rigid)", "A (flexible)" }, r.WindCases.Select(w => w.Name).ToArray());
            Assert.IsTrue(r.WindCases.All(w => w.Roof == 0 && w.RoofName == "A"), "the one wind's roof in both");
            Assert.AreEqual("A", LateralSection.WindOf("A (flexible)"));
            Assert.IsFalse(r.Notes.Any(n => n.Contains("not among the wind calculator's cases")), "both are the calculator's case A");
            var seg = r.Walls.Single(x => x.Id == s.Id).Segments.Single();
            Assert.AreEqual(seg.Load.Wind("A (rigid)"), seg.Load.Wind("A (flexible)"), Tol, "the roof's wind the same in both");
        }

        [TestMethod]
        public void WindCases_TheCalculatorsOwn_ShearWallsByName()
        {
            var b = OneStory(out var s);
            var sec = LoadSection.Read(b);
            var up = WindUplift.Run(b, sec, new[]
            {
                new RoofWindCase { Name = "A", From = "W", PressureAt = (x, y) => -30 },
                new RoofWindCase { Name = "B", From = "S", PressureAt = (x, y) => -10 },
            }, null);
            var lat = new LateralSection
            {
                Source = "test", Cases = new List<string> { "A", "B", "Z" },
                Points = { new LateralPoint { From = "SW1 end", WallId = s.Id, X = 0.2, Y = 0, Up = new Dictionary<string, double> { ["A"] = 1000, ["B"] = -1000, ["Z"] = 5000 } } },
            };
            var r = FoundationAnalysis.Run(b, sec, up, lat, DesignMethod.Asd);
            CollectionAssert.AreEqual(new[] { "A", "B" }, r.WindCases.Select(w => w.Name).ToArray(), "the calculator's cases, each once, in its order");
            Assert.AreEqual(0, r.WindCases.Single(w => w.Lateral == "A").Roof);
            Assert.AreEqual(1, r.WindCases.Single(w => w.Lateral == "B").Roof);
            Assert.IsTrue(r.Notes.Any(n => n.Contains("1 of the 3 shear wall cases") && n.Contains("\"Z\"")));
            int withW = r.Combinations.Count(c => c.Has(LoadType.Wind));
            Assert.AreEqual(2 * withW + (r.Combinations.Count - withW), r.Cases.Count);

            var w0 = r.Walls.Single(x => x.Id == s.Id);
            var seg = w0.Segments.Single();
            var a = r.WindCases.Single(w => w.Lateral == "A");
            var bb = r.WindCases.Single(w => w.Lateral == "B");
            Assert.AreEqual(-180, seg.Load.Wind(a.Name), Tol);
            Assert.AreEqual(-60, seg.Load.Wind(bb.Name), Tol);
            var c06 = r.Cases.Where(c => c.Combination.Name == "0.6D + 0.6W").ToList();
            Assert.AreEqual(6, seg.Combined[r.Cases.IndexOf(c06.Single(c => c.Wind == a))].Min, Tol);
            Assert.AreEqual(78, seg.Combined[r.Cases.IndexOf(c06.Single(c => c.Wind == bb))].Min, Tol);
            Assert.AreEqual(6, w0.Line.Min, Tol);
            Assert.AreSame(a, w0.Line.MinBy.Wind);

            var p = w0.Points.Single();
            Assert.AreEqual(-1000, p.Load.Wind(a.Name), Tol);
            Assert.AreEqual(1000, p.Load.Wind(bb.Name), Tol);
            Assert.AreEqual(-600, p.Envelope.Min, Tol);
            Assert.AreSame(a, p.Envelope.MinBy.Wind);
            Assert.AreEqual(600, p.Envelope.Max, Tol);
            Assert.AreSame(bb, p.Envelope.MaxBy.Wind);

            Assert.AreEqual(-600, p.Envelope.Min, Tol, "Z (5,000 lb) is not a wind calculator case: left out");

            var alone = FoundationAnalysis.Run(b, sec, null, lat, DesignMethod.Asd);
            CollectionAssert.AreEqual(new[] { "A", "B", "Z" }, alone.WindCases.Select(w => w.Name).ToArray());
            Assert.IsTrue(alone.WindCases.All(w => w.Roof < 0 && w.Lateral == w.Name));
        }
    }
}

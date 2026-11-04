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
            Assert.AreEqual("0.6D + 0.6W", asd.Line.MinBy.Name);
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
    }
}

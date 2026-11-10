using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    /// <summary>
    /// A beam on supports anywhere along it (owner 2026-11-10): a cantilever past its last support by statics, a beam
    /// continuous over three (EI the same along it), one that cannot stand. A flat roof 30 (W-E) × 20 (S-N), framing S-N onto
    /// the S and N walls; a beam along y = 10, 30 ft: D 10 psf × 10 ft = 100 plf, 3000 lb, its resultant at 15 ft.
    /// </summary>
    [TestClass]
    public class BeamSupportTests
    {
        private const double Tol = 1e-6;

        private static Building Story(out Level l2, out Beam bm)
        {
            var b = Building.New("Supports");
            b.Roof.Shape = RoofShape.Flat;
            b.Roof.Overhang = 0;
            l2 = b.AddLevel("Level 2", 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 30, 20));
            b.AddWall(b.Ground, l2, 0, 0, 30, 0);
            b.AddWall(b.Ground, l2, 0, 20, 30, 20);
            bm = b.AddBeam(l2, 0, 10, 30, 10);
            LoadSection.SetLevel(b, l2.Id, new AreaLoad { Dead = 10 });
            return b;
        }

        /// <summary>
        /// Posts at 0 and 12 ft: 18 ft past the second. ΣM about the first: R2 × 12 = 3000 × 15, R2 = 3750; R1 = 3000 − 3750 =
        /// −750: the back span is pulled up, and it is said so. Each post takes its own, the uplift too; the beam balances.
        /// </summary>
        [TestMethod]
        public void Cantilever_ByStatics_TheBackSupportPulledUp()
        {
            var b = Story(out var l2, out var bm);
            var c1 = b.AddColumn(b.Ground, l2, 0, 10);
            var c2 = b.AddColumn(b.Ground, l2, 12, 10);
            var s = LoadSection.Read(b);
            var r = GravityLoadPath.Run(b, s);
            var rb = r.Beams[bm.Id];
            Assert.IsTrue(rb.Stands);
            Assert.AreEqual(2, rb.Supports.Count);
            Assert.AreEqual("start", rb.Supports[0].Name);
            Assert.AreEqual("at 12 ft", rb.Supports[1].Name);
            Assert.AreEqual(c2.Id, rb.Supports[1].On);
            Assert.AreEqual(-750, rb.Supports[0].Reaction.Dead, Tol);
            Assert.AreEqual(3750, rb.Supports[1].Reaction.Dead, Tol);
            Assert.IsNull(rb.EndOn, "its end is the cantilever's tip");
            Assert.AreEqual(-750, r.Columns[c1.Id].Load.Dead, Tol);
            Assert.AreEqual(3750, r.Columns[c2.Id].Load.Dead, Tol);
            Assert.IsTrue(rb.Notes.Any(n => n.Contains("pulls up on " + c1.Id)), string.Join(" | ", rb.Notes));
            Assert.IsTrue(rb.Notes.Any(n => n.Contains("18 ft past its last support")), string.Join(" | ", rb.Notes));
            Assert.IsNull(FreeBody.Of(b, s, r, bm.Id).OutOfBalance(null));
        }

        /// <summary>Posts at 0, 15 and 30 ft: two equal spans, w L each: 3/8, 10/8, 3/8 of 1500 (562.5, 1875, 562.5).</summary>
        [TestMethod]
        public void Continuous_TwoEqualSpans_TheTextbook()
        {
            var b = Story(out var l2, out var bm);
            var c1 = b.AddColumn(b.Ground, l2, 0, 10);
            var c2 = b.AddColumn(b.Ground, l2, 15, 10);
            var c3 = b.AddColumn(b.Ground, l2, 30, 10);
            var s = LoadSection.Read(b);
            var r = GravityLoadPath.Run(b, s);
            var rb = r.Beams[bm.Id];
            Assert.AreEqual(3, rb.Supports.Count);
            Assert.AreEqual(562.5, r.Columns[c1.Id].Load.Dead, Tol);
            Assert.AreEqual(1875, r.Columns[c2.Id].Load.Dead, Tol);
            Assert.AreEqual(562.5, r.Columns[c3.Id].Load.Dead, Tol);
            Assert.AreEqual(0, rb.Notes.Count, string.Join(" | ", rb.Notes));
            Assert.IsNull(FreeBody.Of(b, s, r, bm.Id).OutOfBalance(null));
        }

        /// <summary>A wall under the beam's middle, square to it, is a support there: the beam is continuous over it.</summary>
        [TestMethod]
        public void AWallCrossingUnder_IsASupportThere()
        {
            var b = Story(out var l2, out var bm);
            b.AddColumn(b.Ground, l2, 0, 10);
            b.AddColumn(b.Ground, l2, 30, 10);
            var w = b.AddWall(b.Ground, l2, 15, 2, 15, 18);
            var s = LoadSection.Read(b);
            var r = GravityLoadPath.Run(b, s);
            var rb = r.Beams[bm.Id];
            Assert.AreEqual(3, rb.Supports.Count);
            Assert.AreEqual("at 15 ft", rb.Supports[1].Name);
            Assert.AreEqual(w.Id, rb.Supports[1].On);
            var p = r[GravityLoadPath.Key(w.Id, l2.Id)].Points.Single(x => x.From == bm.Id);
            Assert.AreEqual(8, p.At, Tol);
            Assert.AreEqual(rb.Supports[1].Reaction.Dead, p.Load.Dead, Tol);
            Assert.IsTrue(rb.Supports[1].Reaction.Dead > rb.Supports[0].Reaction.Dead * 2, "the middle takes the most");
            Assert.AreEqual(rb.Supports[0].Reaction.Dead, rb.Supports[2].Reaction.Dead, 1e-3, "symmetric");
            Assert.IsNull(FreeBody.Of(b, s, r, bm.Id).OutOfBalance(null));
        }

        /// <summary>One post: it cannot stand. Said so, and nothing is traced down.</summary>
        [TestMethod]
        public void OneSupport_CannotStand_NotTraced()
        {
            var b = Story(out var l2, out var bm);
            var c1 = b.AddColumn(b.Ground, l2, 10, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            var rb = r.Beams[bm.Id];
            Assert.IsFalse(rb.Stands);
            Assert.AreEqual(1, rb.Supports.Count);
            Assert.AreEqual(0, r.Columns[c1.Id].Load.Dead, Tol);
            Assert.IsTrue(rb.Notes.Any(n => n.Contains("cannot stand")), string.Join(" | ", rb.Notes));
        }
    }
}

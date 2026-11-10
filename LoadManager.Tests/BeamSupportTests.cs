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

        /// <summary>
        /// One post: it cannot stand. The model is not valid (owner 2026-11-10): nothing is calculated anywhere, here or in the
        /// foundation check, and the error names the beam and its level.
        /// </summary>
        [TestMethod]
        public void OneSupport_TheModelIsNotValid_NothingCalculated()
        {
            var b = Story(out var l2, out var bm);
            b.AddColumn(b.Ground, l2, 10, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.IsFalse(r.Calculated);
            var e = r.ModelErrors.Single(i => i.Code == "BeamNotHeldUp");
            CollectionAssert.AreEqual(new[] { bm.Id }, e.Ids);
            Assert.AreEqual(l2.Id, e.LevelId);
            Assert.AreEqual(0, r.Beams.Count + r.Count + r.Foundation.Count + r.Columns.Values.Count(c => !c.Load.IsEmpty), "nothing calculated");
            var f = FoundationCheck.Run(b);
            StringAssert.Contains(f.NotChecked, "error");
            Assert.AreEqual(e.Message, f.ModelErrors.Single().Message);
        }

        /// <summary>
        /// Both ends cantilevered: posts at 5 and 25 ft of the 30 ft beam, 5 ft past each. By statics, symmetric: 1500 each
        /// (3000 lb at 15 ft); the free body balances.
        /// </summary>
        [TestMethod]
        public void DoubleCantilever_ByStatics()
        {
            var b = Story(out var l2, out var bm);
            var c1 = b.AddColumn(b.Ground, l2, 5, 10);
            var c2 = b.AddColumn(b.Ground, l2, 25, 10);
            var s = LoadSection.Read(b);
            var r = GravityLoadPath.Run(b, s);
            var rb = r.Beams[bm.Id];
            CollectionAssert.AreEqual(new[] { "at 5 ft", "at 25 ft" }, rb.Supports.Select(x => x.Name).ToArray());
            Assert.AreEqual(1500, r.Columns[c1.Id].Load.Dead, Tol);
            Assert.AreEqual(1500, r.Columns[c2.Id].Load.Dead, Tol);
            Assert.AreEqual(2, rb.Notes.Count(n => n.Contains("cantilever")), string.Join(" | ", rb.Notes));
            Assert.IsNull(FreeBody.Of(b, s, r, bm.Id).OutOfBalance(null));
        }

        /// <summary>
        /// Unequal overhangs: posts at 3 and 21 ft (3 ft and 9 ft past), 100 plf over 30 ft. Î£M about the first: R2 Ã— 18 =
        /// 3000 Ã— (15 âˆ’ 3), R2 = 2000; R1 = 1000.
        /// </summary>
        [TestMethod]
        public void DoubleCantilever_Unequal_ByStatics()
        {
            var b = Story(out var l2, out var bm);
            var c1 = b.AddColumn(b.Ground, l2, 3, 10);
            var c2 = b.AddColumn(b.Ground, l2, 21, 10);
            var r = GravityLoadPath.Run(b, LoadSection.Read(b));
            Assert.AreEqual(1000, r.Columns[c1.Id].Load.Dead, Tol);
            Assert.AreEqual(2000, r.Columns[c2.Id].Load.Dead, Tol);
        }
    }
}

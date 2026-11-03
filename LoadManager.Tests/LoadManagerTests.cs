using System.Linq;
using BuildingModel.Model;
using BuildingModel.Serialization;
using LoadManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LoadManager.Tests
{
    [TestClass]
    public class LoadCombinationTests
    {
        [TestMethod]
        public void IbcAlternativeAsd_EveryEquationWithItsVariableLoadsZeroedInTurn_NoneDropped()
        {
            var names = LoadCombinations.IbcAlternativeAsd().Select(c => c.Name).ToList();
            CollectionAssert.AreEqual(new[]
            {
                // 16-1 (Lr, 0.7S, R)
                "D", "D + L", "D + Lr", "D + L + Lr", "D + 0.7S", "D + L + 0.7S", "D + R", "D + L + R",
                // 16-2
                "D + 0.6W", "D + L + 0.6W",
                // 16-3
                "D + 0.7S/2", "D + L + 0.7S/2", "D + 0.6W + 0.7S/2", "D + L + 0.6W + 0.7S/2",
                // 16-4
                "D + 0.6W/2", "D + L + 0.6W/2", "D + 0.7S + 0.6W/2", "D + L + 0.7S + 0.6W/2",
                // 16-5
                "D + E/1.4", "D + L + E/1.4", "D + 0.7S + E/1.4", "D + L + 0.7S + E/1.4",
                // 16-6
                "0.9D", "0.9D + E/1.4",
            }, names);
        }

        [TestMethod]
        public void Repeats_ListedOnceWithEveryEquation()
        {
            var all = LoadCombinations.IbcAlternativeAsd();
            var keys = all.Select(c => string.Join("|", c.Terms.OrderBy(t => t.Type).Select(t => $"{t.Type}:{t.Factor}"))).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
            var d = all.Single(c => c.Name == "D");
            CollectionAssert.AreEqual(new[] { "IBC Eq. 16-1", "IBC Eq. 16-2", "IBC Eq. 16-3", "IBC Eq. 16-4", "IBC Eq. 16-5" }, d.Equations);
            Assert.AreEqual("IBC Eq. 16-2; IBC Eq. 16-3", all.Single(c => c.Name == "D + L + 0.6W").Equation);
        }

        [TestMethod]
        public void Asce7Asd_EveryEquationAndAlternative()
        {
            var all = LoadCombinations.Asce7Asd();
            var n = all.Select(c => c.Name).ToList();
            // 6a, both wind kinds and every (Lr or 0.7S or R)
            foreach (var x in new[] { "D + 0.75L + 0.75(0.6W) + 0.75Lr", "D + 0.75L + 0.75(0.6W) + 0.75(0.7S)", "D + 0.75L + 0.75(0.6W) + 0.75R",
                                      "D + 0.75L + 0.75(0.6WT) + 0.75(0.7S)", "0.6D + 0.6W", "0.6D + 0.6WT", "D + 0.7Ev + 0.7Eh",
                                      "D + 0.525Ev + 0.525Eh + 0.75L + 0.1S", "0.6D − 0.7Ev + 0.7Eh", "D + 0.75(0.6W)", "0.6D" })
                CollectionAssert.Contains(n, x, x);
            var c = all.Single(x => x.Name == "D + 0.75L + 0.75(0.6W) + 0.75(0.7S)");
            Assert.AreEqual("ASCE 7-22 2.4 Eq. 6a", c.Equation);
            Assert.AreEqual(0.75, c.Factor(LoadType.Live), 1e-12);
            Assert.AreEqual(0.45, c.Factor(LoadType.Wind), 1e-12);
            Assert.AreEqual(0.525, c.Factor(LoadType.Snow), 1e-12);
            var e = all.Single(x => x.Name == "0.6D − 0.7Ev + 0.7Eh");
            Assert.AreEqual(-0.7, e.Factor(LoadType.SeismicVertical), 1e-12);
            Assert.AreEqual(0.6, e.Factor(LoadType.Dead), 1e-12);
        }

        [TestMethod]
        public void Asd_IsBothSets_NothingLost()
        {
            var asd = LoadCombinations.Asd();
            var keys = new System.Collections.Generic.HashSet<string>(asd.Select(c => c.Name));
            foreach (var c in LoadCombinations.Asce7Asd().Concat(LoadCombinations.IbcAlternativeAsd()))
                Assert.IsTrue(keys.Contains(c.Name), c.Name);
            var d = asd.Single(c => c.Name == "D");
            Assert.IsTrue(d.Equations.Contains("ASCE 7-22 2.4 Eq. 1a"));
            Assert.IsTrue(d.Equations.Contains("IBC Eq. 16-1"));
        }

        [TestMethod]
        public void Asce7Strength_EveryEquationAndAlternative()
        {
            var n = LoadCombinations.Asce7Strength().Select(c => c.Name).ToList();
            foreach (var x in new[] { "1.4D", "1.2D + 1.6L + 0.5Lr", "1.2D + 1.6L + 0.3S", "1.2D + 1.6L + 0.5R", "1.2D + 1.6Lr + L",
                                      "1.2D + 1.0S + 0.5W", "1.2D + 1.0W + L + 0.3S", "1.2D + 1.0WT + L + 0.5Lr", "0.9D + 1.0W",
                                      "0.9D + 1.0WT", "1.2D + Ev + Eh + L + 0.15S", "0.9D − Ev + Eh", "1.2D", "0.9D" })
                CollectionAssert.Contains(n, x, x);
        }

        [TestMethod]
        public void Combination_FactorsAndEquation()
        {
            var c = LoadCombinations.IbcAlternativeAsd().Single(x => x.Name == "D + L + 0.6W + 0.7S/2");
            Assert.AreEqual("IBC Eq. 16-3", c.Equation);
            Assert.AreEqual(1, c.Factor(LoadType.Dead));
            Assert.AreEqual(0.6, c.Factor(LoadType.Wind), 1e-12);
            Assert.AreEqual(0.35, c.Factor(LoadType.Snow), 1e-12);
            Assert.IsFalse(c.Has(LoadType.RoofLive));
            var e = LoadCombinations.IbcAlternativeAsd().Single(x => x.Name == "0.9D + E/1.4");
            Assert.AreEqual(0.9, e.Factor(LoadType.Dead), 1e-12);
            Assert.AreEqual(1 / 1.4, e.Factor(LoadType.Earthquake), 1e-12);
        }
    }

    [TestClass]
    public class LoadSectionTests
    {
        private static Building TwoStory(out Wall first, out Level l2)
        {
            var b = Building.New("Two");
            l2 = b.AddLevel("Level 2", 10);
            b.AddLevel("Roof", 19);
            first = b.AddWall(b.Ground, l2, 0, 0, 40, 0);
            return b;
        }

        [TestMethod]
        public void SetWallTop_SetsChangesAndClears()
        {
            var b = TwoStory(out var w, out var l2);
            Assert.IsNull(LoadSection.Read(b).WallTop(w.Id, l2.Id));
            Assert.IsFalse(LoadSection.SetWallTop(b, w.Id, l2.Id, new LineLoad()));    // nothing to clear
            Assert.IsTrue(LoadSection.SetWallTop(b, w.Id, l2.Id, new LineLoad { Dead = 300, Live = 400 }));
            var l = LoadSection.Read(b).WallTop(w.Id, l2.Id);
            Assert.AreEqual(300, l.Dead);
            Assert.AreEqual(400, l.Live);
            Assert.AreEqual(400, l.Of(LoadType.Live));
            Assert.AreEqual(0, l.Of(LoadType.Wind));
            Assert.IsFalse(LoadSection.SetWallTop(b, w.Id, l2.Id, new LineLoad { Dead = 300, Live = 400 }));
            Assert.IsTrue(LoadSection.SetWallTop(b, w.Id, l2.Id, new LineLoad { Dead = -5, Snow = 250 }));
            l = LoadSection.Read(b).WallTop(w.Id, l2.Id);
            Assert.AreEqual(0, l.Dead);
            Assert.AreEqual(250, l.Snow);

            Assert.IsTrue(LoadSection.SetWallTop(b, w.Id, l2.Id, null));
            Assert.IsNull(LoadSection.Read(b).WallTop(w.Id, l2.Id));
            Assert.IsNull(b.GetData(LoadSection.Key));                                 // nothing left: no section
            Assert.IsFalse(LoadSection.SetWallTop(b, "nope", l2.Id, new LineLoad { Dead = 1 }));
        }

        [TestMethod]
        public void Section_RoundTripsInTheProjectFile()
        {
            var b = TwoStory(out var w, out var l2);
            LoadSection.SetWallTop(b, w.Id, l2.Id, new LineLoad { Dead = 300, Live = 400, RoofLive = 20, Snow = 250 });
            var back = ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building;
            var l = LoadSection.Read(back).WallTop(w.Id, l2.Id);
            Assert.AreEqual(300, l.Dead);
            Assert.AreEqual(400, l.Live);
            Assert.AreEqual(20, l.RoofLive);
            Assert.AreEqual(250, l.Snow);
        }

        [TestMethod]
        public void Read_BadJsonGivesAnEmptySection()
        {
            var b = TwoStory(out var w, out var l2);
            b.SetData(LoadSection.Key, "{not json");
            Assert.IsNull(LoadSection.Read(b).WallTop(w.Id, l2.Id));
        }
    }
}

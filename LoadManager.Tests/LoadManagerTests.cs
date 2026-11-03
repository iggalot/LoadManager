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
        public void IbcAlternativeAsd_EveryEquationWithItsVariableLoadsZeroedInTurn()
        {
            var names = LoadCombinations.IbcAlternativeAsd().Select(c => c.Name).ToList();
            CollectionAssert.AreEqual(new[]
            {
                // 16-1 (Lr, 0.7S, R)
                "D", "D + L", "D + Lr", "D + L + Lr", "D + 0.7S", "D + L + 0.7S", "D + R", "D + L + R",
                // 16-2
                "D + 0.6W", "D + L + 0.6W",
                // 16-3
                "D + 0.7S/2", "D + L + 0.7S/2","D + 0.6W + 0.7S/2", "D + L + 0.6W + 0.7S/2",
                // 16-4
                "D + 0.6W/2", "D + L + 0.6W/2", "D + 0.7S + 0.6W/2", "D + L + 0.7S + 0.6W/2",
                // 16-5
                "D + E/1.4", "D + L + E/1.4", "D + 0.7S + E/1.4", "D + L + 0.7S + E/1.4",
                // 16-6
                "0.9D + E/1.4",
            }, names);
        }

        [TestMethod]
        public void IbcAlternativeAsd_NoRepeatsAndNoBareReducedDead()
        {
            var all = LoadCombinations.IbcAlternativeAsd();
            var keys = all.Select(c => string.Join("|", c.Terms.Select(t => $"{t.Type}:{t.Factor}"))).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
            Assert.IsFalse(all.Any(c => c.Name == "0.9D"));
            Assert.AreEqual(1, all.Count(c => c.Name == "D"));
        }

        [TestMethod]
        public void Combination_FactorsAndEquation()
        {
            var c = LoadCombinations.IbcAlternativeAsd().Single(x => x.Name == "D + L + 0.6W + 0.7S/2");
            Assert.AreEqual("16-3", c.Equation);
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

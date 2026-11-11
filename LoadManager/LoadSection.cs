using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>
    /// Loads by kind (service level): dead, floor live, roof live, snow (as ASCE 7 gives it), and the wind on the roof
    /// (<see cref="Wind"/>: found by the load path's uplift pass, never entered or saved).
    /// </summary>
    [DataContract]
    public abstract class GravityLoad
    {
        [DataMember(Order = 1, EmitDefaultValue = false)] public double Dead { get; set; }
        [DataMember(Order = 2, EmitDefaultValue = false)] public double Live { get; set; }
        [DataMember(Order = 3, EmitDefaultValue = false)] public double RoofLive { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public double Snow { get; set; }

        /// <summary>
        /// The wind on the roof, W, as the wind calculator gives it (ASCE 7 strength level): + down, − up. Only the load path's
        /// uplift pass (<see cref="GravityLoadPath.Uplift"/>) fills it; it is not saved.
        /// </summary>
        public double Wind { get; set; }

        public bool IsEmpty => !(Dead > 0) && !(Live > 0) && !(RoofLive > 0) && !(Snow > 0) && Wind == 0;
        public bool SameAs(GravityLoad o) => o != null && Dead == o.Dead && Live == o.Live && RoofLive == o.RoofLive && Snow == o.Snow && Wind == o.Wind;

        /// <summary>The load of one kind; 0 for a kind it does not carry (rain, earthquake).</summary>
        public double Of(LoadType t) => t == LoadType.Dead ? Dead : t == LoadType.Live ? Live : t == LoadType.RoofLive ? RoofLive : t == LoadType.Snow ? Snow : t == LoadType.Wind ? Wind : 0;

        /// <summary>
        /// 0.6D + 0.6W (ASCE 7-22 2.4 Eq. 7a), the allowable stress combination of wind lifting against dead load: negative is a net
        /// uplift. (IBC 1605.2's alternative combinations count two-thirds of the dead load against wind: less severe.)
        /// </summary>
        public double Net => 0.6 * Dead + 0.6 * Wind;

        public override string ToString() => $"D {Dead:0.##}, L {Live:0.##}, Lr {RoofLive:0.##}, S {Snow:0.##}" + (Wind != 0 ? $", W {Wind:0.##}" : "");
    }

    /// <summary>A line load (plf).</summary>
    [DataContract]
    public sealed class LineLoad : GravityLoad
    {
        public static LineLoad operator +(LineLoad a, LineLoad b) => new LineLoad
        {
            Dead = (a?.Dead ?? 0) + (b?.Dead ?? 0), Live = (a?.Live ?? 0) + (b?.Live ?? 0),
            RoofLive = (a?.RoofLive ?? 0) + (b?.RoofLive ?? 0), Snow = (a?.Snow ?? 0) + (b?.Snow ?? 0), Wind = (a?.Wind ?? 0) + (b?.Wind ?? 0),
        };

        /// <summary>The larger of each kind (of the wind, the more upward).</summary>
        public static LineLoad Max(LineLoad a, LineLoad b) => new LineLoad
        {
            Dead = System.Math.Max(a?.Dead ?? 0, b?.Dead ?? 0), Live = System.Math.Max(a?.Live ?? 0, b?.Live ?? 0),
            RoofLive = System.Math.Max(a?.RoofLive ?? 0, b?.RoofLive ?? 0), Snow = System.Math.Max(a?.Snow ?? 0, b?.Snow ?? 0),
            Wind = System.Math.Min(a?.Wind ?? 0, b?.Wind ?? 0),
        };

        /// <summary>
        /// Of two, the one with more net uplift (the lower <see cref="GravityLoad.Net"/>), its dead and wind kept together as they
        /// act at one place: the uplift pass's "most along the line".
        /// </summary>
        public static LineLoad MoreUplift(LineLoad a, LineLoad b) => a == null ? b : b == null ? a : b.Net < a.Net - 1e-9 ? b : a;

        /// <summary>The whole load over a length (ft): lb.</summary>
        public PointLoad Over(double lengthFt) => new PointLoad { Dead = Dead * lengthFt, Live = Live * lengthFt, RoofLive = RoofLive * lengthFt, Snow = Snow * lengthFt, Wind = Wind * lengthFt };
    }

    /// <summary>A concentrated load (lb): a beam's reaction, what a column carries.</summary>
    [DataContract]
    public sealed class PointLoad : GravityLoad
    {
        public static PointLoad operator +(PointLoad a, PointLoad b) => new PointLoad
        {
            Dead = (a?.Dead ?? 0) + (b?.Dead ?? 0), Live = (a?.Live ?? 0) + (b?.Live ?? 0),
            RoofLive = (a?.RoofLive ?? 0) + (b?.RoofLive ?? 0), Snow = (a?.Snow ?? 0) + (b?.Snow ?? 0), Wind = (a?.Wind ?? 0) + (b?.Wind ?? 0),
        };

        /// <summary>Each kind times a factor (a share of a load).</summary>
        public PointLoad Times(double k) => new PointLoad { Dead = Dead * k, Live = Live * k, RoofLive = RoofLive * k, Snow = Snow * k, Wind = Wind * k };

        public override string ToString() => $"D {Dead:0}, L {Live:0}, Lr {RoofLive:0}, S {Snow:0}" + (Wind != 0 ? $", W {Wind:0}" : "") + " lb";
    }

    /// <summary>An area load (psf, on the horizontal projection).</summary>
    [DataContract]
    public sealed class AreaLoad : GravityLoad
    {
        /// <summary>Over a tributary width (ft): the line load (plf).</summary>
        public LineLoad Times(double widthFt) => new LineLoad { Dead = Dead * widthFt, Live = Live * widthFt, RoofLive = RoofLive * widthFt, Snow = Snow * widthFt, Wind = Wind * widthFt };
    }

    /// <summary>
    /// A load zone: a rectangle of a level (on the plan, ft) whose area loads take the place of the level's there — a deck,
    /// a corridor, a storage room. Where zones overlap, the larger of each kind applies.
    /// </summary>
    [DataContract]
    public sealed class LoadZone
    {
        [DataMember(Order = 1)] public string LevelId { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public double X0 { get; set; }
        [DataMember(Order = 4)] public double Y0 { get; set; }
        [DataMember(Order = 5)] public double X1 { get; set; }
        [DataMember(Order = 6)] public double Y1 { get; set; }
        [DataMember(Order = 7)] public AreaLoad Load { get; set; } = new AreaLoad();

        public bool IsValid => X1 > X0 && Y1 > Y0 && Load != null;
        public bool SameAs(LoadZone o) => o != null && LevelId == o.LevelId && (Name ?? "") == (o.Name ?? "") && X0 == o.X0 && Y0 == o.Y0 && X1 == o.X1 && Y1 == o.Y1 && (Load ?? new AreaLoad()).SameAs(o.Load ?? new AreaLoad());
        public override string ToString() => $"{Name} ({X0:0.##}, {Y0:0.##}) to ({X1:0.##}, {Y1:0.##}): {Load} psf";
    }

    /// <summary>
    /// An area use (owner 2026-11-10): the area loads of a kind of part of a floor or roof (a balcony, a corridor, a bath),
    /// set once and given to every framing area of that use. A framing area takes the use named as it is (its name in the
    /// Building Model), or the one picked for it here, else its level's loads: nothing to set for the ordinary floor.
    /// </summary>
    [DataContract]
    public sealed class AreaUse
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public AreaLoad Load { get; set; } = new AreaLoad();
        public override string ToString() => $"{Name}: {Load} psf";
    }

    /// <summary>
    /// The load manager's section of the project file (the building's "Loads" data), as the wind app keeps its "Wind"
    /// section: the loads are the project's, not the building model's. The building model only carries the section;
    /// the IDs in it are the model's (walls, levels), so loads stay on their objects.
    /// </summary>
    [DataContract]
    public sealed class LoadSection
    {
        public const string Key = "Loads";

        /// <summary>
        /// Wall ID → top level ID → loads on the wall's top on that story, entered by hand: they take the place of what the
        /// gravity load path works out (<see cref="GravityLoadPath"/>).
        /// </summary>
        [DataMember(Order = 1, EmitDefaultValue = false)] public Dictionary<string, Dictionary<string, LineLoad>> WallTops { get; set; }

        /// <summary>Level ID → the area loads on that level's floor or roof (psf, horizontal projection).</summary>
        [DataMember(Order = 2, EmitDefaultValue = false)] public Dictionary<string, AreaLoad> Levels { get; set; }

        /// <summary>The walls' own dead load (psf of wall elevation), carried down to what each wall stands on.</summary>
        [DataMember(Order = 3, EmitDefaultValue = false)] public double WallDead { get; set; }

        /// <summary>Load zones: parts of a level with their own area loads (a deck, a corridor, a storage room).</summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public List<LoadZone> Zones { get; set; }

        /// <summary>The area uses (a balcony, a corridor): each one's loads, given to the framing areas of that use.</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public List<AreaUse> Uses { get; set; }

        /// <summary>
        /// Framing area ID → the use picked for it here, over the one its name gives (<see cref="LevelsLoads"/>: the level's
        /// loads, even where its name is a use's).
        /// </summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public Dictionary<string, string> AreaUses { get; set; }

        /// <summary>An area's use picked as "the level's loads" (<see cref="AreaUses"/>).</summary>
        public const string LevelsLoads = "";

        /// <summary>The zones on a level.</summary>
        public IEnumerable<LoadZone> ZonesOn(string levelId) => (Zones ?? new List<LoadZone>()).Where(z => z.LevelId == levelId && z.IsValid);

        /// <summary>The use with this name (any case), or null.</summary>
        public AreaUse Use(string name) =>
            string.IsNullOrWhiteSpace(name) ? null : (Uses ?? new List<AreaUse>()).FirstOrDefault(u => string.Equals(u.Name?.Trim(), name.Trim(), System.StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A framing area's use: the one picked for it here, else the one its name gives, else null (the level's loads).
        /// <paramref name="picked"/>: whether it was picked here (not from its name).
        /// </summary>
        public AreaUse UseOf(FramingArea a, out bool picked)
        {
            picked = false;
            if (a == null) return null;
            if (AreaUses != null && AreaUses.TryGetValue(a.Id, out var name)) { picked = true; return Use(name); }
            return Use(a.Name);
        }

        public AreaUse UseOf(FramingArea a) => UseOf(a, out _);

        /// <summary>The area loads on a framing area of a level: its use's, else the level's (null: none entered).</summary>
        public AreaLoad LoadOn(FramingArea a, string levelId) => UseOf(a)?.Load ?? Level(levelId);

        public bool IsEmpty => (WallTops == null || WallTops.Count == 0) && (Levels == null || Levels.Count == 0) && !(WallDead > 0) && (Zones == null || Zones.Count == 0)
                               && (Uses == null || Uses.Count == 0) && (AreaUses == null || AreaUses.Count == 0);

        /// <summary>The loads entered on a wall's top on the story under <paramref name="topLevelId"/>, or null (none entered).</summary>
        public LineLoad WallTop(string wallId, string topLevelId) =>
            wallId != null && topLevelId != null && WallTops != null && WallTops.TryGetValue(wallId, out var byLevel) && byLevel.TryGetValue(topLevelId, out var l) ? l : null;

        /// <summary>The area loads on a level, or null (none entered).</summary>
        public AreaLoad Level(string levelId) => levelId != null && Levels != null && Levels.TryGetValue(levelId, out var a) ? a : null;

        // ------------------------------------------------------------------ in the project file

        private static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(LoadSection), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        /// <summary>The section stored in the building's project file, or an empty one.</summary>
        public static LoadSection Read(Building b)
        {
            string json = b?.GetData(Key);
            if (string.IsNullOrEmpty(json)) return new LoadSection();
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (LoadSection)Serializer().ReadObject(ms) ?? new LoadSection();
            }
            catch (SerializationException)
            {
                return new LoadSection();
            }
        }

        /// <summary>Stores the section in the building's project file (removed when it holds nothing).</summary>
        public void Write(Building b) => b.SetData(Key, IsEmpty ? null : ToJson());

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static double P(double v) => v > 0 ? v : 0;

        /// <summary>
        /// Sets the loads on a wall's top on a story, in the building's project file (one edit: undoable where the caller
        /// edits the building through its history). Negative values are taken as 0; all zero removes the entry (the gravity
        /// load path's values apply again). False when nothing changed or there is no such wall.
        /// </summary>
        public static bool SetWallTop(Building b, string wallId, string topLevelId, LineLoad load)
        {
            if (b == null || !(b.Find(wallId) is Wall) || string.IsNullOrEmpty(topLevelId)) return false;
            var next = new LineLoad { Dead = P(load?.Dead ?? 0), Live = P(load?.Live ?? 0), RoofLive = P(load?.RoofLive ?? 0), Snow = P(load?.Snow ?? 0) };
            var s = Read(b);
            if ((s.WallTop(wallId, topLevelId) ?? new LineLoad()).SameAs(next)) return false;
            if (next.IsEmpty)
            {
                s.WallTops[wallId].Remove(topLevelId);
                if (s.WallTops[wallId].Count == 0) s.WallTops.Remove(wallId);
            }
            else
            {
                s.WallTops = s.WallTops ?? new Dictionary<string, Dictionary<string, LineLoad>>();
                if (!s.WallTops.TryGetValue(wallId, out var byLevel)) s.WallTops[wallId] = byLevel = new Dictionary<string, LineLoad>();
                byLevel[topLevelId] = next;
            }
            s.Write(b);
            return true;
        }

        /// <summary>Sets the area loads on a level (all zero removes them). False when nothing changed or there is no such level.</summary>
        public static bool SetLevel(Building b, string levelId, AreaLoad load)
        {
            if (b?.Level(levelId) == null) return false;
            var next = new AreaLoad { Dead = P(load?.Dead ?? 0), Live = P(load?.Live ?? 0), RoofLive = P(load?.RoofLive ?? 0), Snow = P(load?.Snow ?? 0) };
            var s = Read(b);
            if ((s.Level(levelId) ?? new AreaLoad()).SameAs(next)) return false;
            if (next.IsEmpty) s.Levels.Remove(levelId);
            else (s.Levels = s.Levels ?? new Dictionary<string, AreaLoad>())[levelId] = next;
            s.Write(b);
            return true;
        }

        /// <summary>Sets every level's area loads and the walls' own dead load at once (one edit). False when nothing changed.</summary>
        public static bool SetAreaLoads(Building b, IDictionary<string, AreaLoad> levels, double wallDead)
        {
            bool changed = false;
            foreach (var kv in levels ?? new Dictionary<string, AreaLoad>()) changed |= SetLevel(b, kv.Key, kv.Value);
            changed |= SetWallDead(b, wallDead);
            return changed;
        }

        /// <summary>
        /// Sets every load zone at once (one edit): those on a level that does not exist, or with no area, are left out; the
        /// corners are put in order and negative loads taken as 0. False when nothing changed.
        /// </summary>
        public static bool SetZones(Building b, IEnumerable<LoadZone> zones)
        {
            if (b == null) return false;
            var next = (zones ?? Enumerable.Empty<LoadZone>())
                .Where(z => z != null && b.Level(z.LevelId) != null)
                .Select(z => new LoadZone
                {
                    LevelId = z.LevelId, Name = string.IsNullOrWhiteSpace(z.Name) ? "Zone" : z.Name.Trim(),
                    X0 = System.Math.Min(z.X0, z.X1), X1 = System.Math.Max(z.X0, z.X1), Y0 = System.Math.Min(z.Y0, z.Y1), Y1 = System.Math.Max(z.Y0, z.Y1),
                    Load = new AreaLoad { Dead = P(z.Load?.Dead ?? 0), Live = P(z.Load?.Live ?? 0), RoofLive = P(z.Load?.RoofLive ?? 0), Snow = P(z.Load?.Snow ?? 0) },
                })
                .Where(z => z.IsValid).ToList();
            var s = Read(b);
            var now = s.Zones ?? new List<LoadZone>();
            if (now.Count == next.Count && now.Zip(next, (x, y) => x.SameAs(y)).All(t => t)) return false;
            s.Zones = next.Count == 0 ? null : next;
            s.Write(b);
            return true;
        }

        /// <summary>
        /// Sets every area use at once (one edit): blank names left out, a name used twice kept once (the first), negative loads
        /// taken as 0. A use renamed or taken away leaves its areas on the level's loads (or the use their name gives). False
        /// when nothing changed.
        /// </summary>
        public static bool SetUses(Building b, IEnumerable<AreaUse> uses)
        {
            if (b == null) return false;
            var next = new List<AreaUse>();
            foreach (var u in uses ?? Enumerable.Empty<AreaUse>())
            {
                if (u == null || string.IsNullOrWhiteSpace(u.Name)) continue;
                string n = u.Name.Trim();
                if (next.Any(x => string.Equals(x.Name, n, System.StringComparison.OrdinalIgnoreCase))) continue;
                next.Add(new AreaUse { Name = n, Load = new AreaLoad { Dead = P(u.Load?.Dead ?? 0), Live = P(u.Load?.Live ?? 0), RoofLive = P(u.Load?.RoofLive ?? 0), Snow = P(u.Load?.Snow ?? 0) } });
            }
            var s = Read(b);
            var now = s.Uses ?? new List<AreaUse>();
            if (now.Count == next.Count && now.Zip(next, (x, y) => x.Name == y.Name && (x.Load ?? new AreaLoad()).SameAs(y.Load)).All(t => t)) return false;
            s.Uses = next.Count == 0 ? null : next;
            s.Write(b);
            return true;
        }

        /// <summary>
        /// Gives framing areas a use (one edit): <paramref name="use"/> a use's name, <see cref="LevelsLoads"/> the level's
        /// loads, or null: back to the use their name gives. False when nothing changed.
        /// </summary>
        public static bool SetAreaUse(Building b, IEnumerable<string> areaIds, string use)
        {
            if (b == null) return false;
            var s = Read(b);
            var map = s.AreaUses ?? new Dictionary<string, string>();
            bool changed = false;
            foreach (var id in areaIds ?? Enumerable.Empty<string>())
            {
                if (FramingAreas.Find(b, id).a == null) continue;
                if (use == null) { changed |= map.Remove(id); continue; }
                string v = use.Trim();
                if (map.TryGetValue(id, out var was) && was == v) continue;
                map[id] = v;
                changed = true;
            }
            if (!changed) return false;
            s.AreaUses = map.Count == 0 ? null : map;
            s.Write(b);
            return true;
        }

        /// <summary>Sets the walls' own dead load (psf of wall). False when nothing changed.</summary>
        public static bool SetWallDead(Building b, double psf)
        {
            if (b == null) return false;
            var s = Read(b);
            if (s.WallDead == P(psf)) return false;
            s.WallDead = P(psf);
            s.Write(b);
            return true;
        }
    }
}

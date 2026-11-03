using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>Gravity loads by kind (service level): dead, floor live, roof live, snow (as ASCE 7 gives it).</summary>
    [DataContract]
    public abstract class GravityLoad
    {
        [DataMember(Order = 1, EmitDefaultValue = false)] public double Dead { get; set; }
        [DataMember(Order = 2, EmitDefaultValue = false)] public double Live { get; set; }
        [DataMember(Order = 3, EmitDefaultValue = false)] public double RoofLive { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public double Snow { get; set; }

        public bool IsEmpty => !(Dead > 0) && !(Live > 0) && !(RoofLive > 0) && !(Snow > 0);
        public bool SameAs(GravityLoad o) => o != null && Dead == o.Dead && Live == o.Live && RoofLive == o.RoofLive && Snow == o.Snow;

        /// <summary>The load of one kind; 0 for a kind a gravity load does not carry (wind, earthquake, rain).</summary>
        public double Of(LoadType t) => t == LoadType.Dead ? Dead : t == LoadType.Live ? Live : t == LoadType.RoofLive ? RoofLive : t == LoadType.Snow ? Snow : 0;

        public override string ToString() => $"D {Dead:0.##}, L {Live:0.##}, Lr {RoofLive:0.##}, S {Snow:0.##}";
    }

    /// <summary>A line load (plf).</summary>
    [DataContract]
    public sealed class LineLoad : GravityLoad
    {
        public static LineLoad operator +(LineLoad a, LineLoad b) => new LineLoad
        {
            Dead = (a?.Dead ?? 0) + (b?.Dead ?? 0), Live = (a?.Live ?? 0) + (b?.Live ?? 0),
            RoofLive = (a?.RoofLive ?? 0) + (b?.RoofLive ?? 0), Snow = (a?.Snow ?? 0) + (b?.Snow ?? 0),
        };

        /// <summary>The larger of each kind.</summary>
        public static LineLoad Max(LineLoad a, LineLoad b) => new LineLoad
        {
            Dead = System.Math.Max(a?.Dead ?? 0, b?.Dead ?? 0), Live = System.Math.Max(a?.Live ?? 0, b?.Live ?? 0),
            RoofLive = System.Math.Max(a?.RoofLive ?? 0, b?.RoofLive ?? 0), Snow = System.Math.Max(a?.Snow ?? 0, b?.Snow ?? 0),
        };
    }

    /// <summary>An area load (psf, on the horizontal projection).</summary>
    [DataContract]
    public sealed class AreaLoad : GravityLoad
    {
        /// <summary>Over a tributary width (ft): the line load (plf).</summary>
        public LineLoad Times(double widthFt) => new LineLoad { Dead = Dead * widthFt, Live = Live * widthFt, RoofLive = RoofLive * widthFt, Snow = Snow * widthFt };
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

        public bool IsEmpty => (WallTops == null || WallTops.Count == 0) && (Levels == null || Levels.Count == 0) && !(WallDead > 0);

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

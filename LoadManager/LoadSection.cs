using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>A line load (plf, service level) by kind: dead, floor live, roof live, snow (as ASCE 7 gives it).</summary>
    [DataContract]
    public sealed class LineLoad
    {
        [DataMember(Order = 1, EmitDefaultValue = false)] public double Dead { get; set; }
        [DataMember(Order = 2, EmitDefaultValue = false)] public double Live { get; set; }
        [DataMember(Order = 3, EmitDefaultValue = false)] public double RoofLive { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public double Snow { get; set; }

        public bool IsEmpty => !(Dead > 0) && !(Live > 0) && !(RoofLive > 0) && !(Snow > 0);
        public bool SameAs(LineLoad o) => o != null && Dead == o.Dead && Live == o.Live && RoofLive == o.RoofLive && Snow == o.Snow;

        /// <summary>The load of one kind (plf); 0 for a kind a line load does not carry (wind, earthquake, rain).</summary>
        public double Of(LoadType t) => t == LoadType.Dead ? Dead : t == LoadType.Live ? Live : t == LoadType.RoofLive ? RoofLive : t == LoadType.Snow ? Snow : 0;
    }

    /// <summary>
    /// The load manager's section of the project file (the building's "Loads" data), as the wind app keeps its "Wind"
    /// section: the loads are the project's, not the building model's. The building model only carries the section;
    /// the IDs in it are the model's (walls, levels), so loads stay on their objects.
    /// For now: the gravity loads on wall tops, by wall and by the level on top of the story (entered by hand until the
    /// gravity load path computes them from floor and roof loads).
    /// </summary>
    [DataContract]
    public sealed class LoadSection
    {
        public const string Key = "Loads";

        /// <summary>Wall ID → top level ID → the loads on the wall's top on that story.</summary>
        [DataMember(Order = 1, EmitDefaultValue = false)] public Dictionary<string, Dictionary<string, LineLoad>> WallTops { get; set; }

        /// <summary>The loads on a wall's top on the story under <paramref name="topLevelId"/>, or null (none entered).</summary>
        public LineLoad WallTop(string wallId, string topLevelId) =>
            wallId != null && topLevelId != null && WallTops != null && WallTops.TryGetValue(wallId, out var byLevel) && byLevel.TryGetValue(topLevelId, out var l) ? l : null;

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
        public void Write(Building b) => b.SetData(Key, WallTops == null || WallTops.Count == 0 ? null : ToJson());

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>
        /// Sets the loads on a wall's top on a story, in the building's project file (one edit: undoable where the caller
        /// edits the building through its history). Negative values are taken as 0; all zero removes the entry. False when
        /// nothing changed or there is no such wall.
        /// </summary>
        public static bool SetWallTop(Building b, string wallId, string topLevelId, LineLoad load)
        {
            if (b == null || !(b.Find(wallId) is Wall) || string.IsNullOrEmpty(topLevelId)) return false;
            double P(double v) => v > 0 ? v : 0;
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
    }
}

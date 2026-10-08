using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chisel.Formatter
{
    public static class MapMigration
    {
        public const int CurrentFormatVersion = 1;

        public static RawMap LoadAndMigrate(string json)
        {
            var map = JsonConvert.DeserializeObject<RawMap>(json);

            if (map.FormatVersion < CurrentFormatVersion)
            {
                var legacy = ExtractLegacyBrushEntities(json);
                if (legacy.Count > 0)
                    MergeLegacyEntities(ref map, legacy);

                map.FormatVersion = CurrentFormatVersion;
            }

            return map;
        }

        private static List<(int brushIndex, JObject legacyEntity)> ExtractLegacyBrushEntities(string json)
        {
            var result = new List<(int, JObject)>();

            JObject root;
            try { root = JObject.Parse(json); }
            catch { return result; }

            if (root["brushes"] is not JArray brushesArray) return result;

            for (int i = 0; i < brushesArray.Count; i++)
            {
                if (brushesArray[i] is not JObject brushObj) continue;
                if (brushObj["brushEntity"] is not JObject legacyEntity) continue;

                var entityNameTok = legacyEntity["entityName"];
                if (entityNameTok == null || entityNameTok.Type != JTokenType.String) continue;
                if (string.IsNullOrEmpty(entityNameTok.Value<string>())) continue;

                result.Add((i, legacyEntity));
            }

            return result;
        }

        private static void MergeLegacyEntities(ref RawMap map, List<(int brushIndex, JObject legacyEntity)> legacy)
        {
            var byName = new Dictionary<string, EntityReference>();
            var migrated = new List<EntityReference>();

            foreach (var (brushIndex, legacyEntity) in legacy)
            {
                string name = legacyEntity["name"]?.Value<string>() ?? string.Empty;

                EntityReference entity;
                if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out var existing))
                {
                    entity = existing;
                }
                else
                {
                    entity = legacyEntity.ToObject<EntityReference>();
                    entity.BrushIndices = new List<int>();
                    migrated.Add(entity);
                    if (!string.IsNullOrEmpty(name)) byName[name] = entity;
                }

                entity.BrushIndices.Add(brushIndex);
            }

            var existingRefs = map.EntityReferences ?? Array.Empty<EntityReference>();
            map.EntityReferences = existingRefs.Concat(migrated).ToArray();
        }
    }
}

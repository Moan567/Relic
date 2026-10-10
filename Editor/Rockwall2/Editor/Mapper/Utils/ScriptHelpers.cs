using Avalonia.Platform;
using Relic.EXScript;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;

public static class EXHostSchemaLoader
{
    private static EXHostSchema cached;

    public static EXHostSchema Load()
    {
        if (cached != null) return cached;

        var uri = new Uri("avares://Rockwall2/Assets/ScriptFormat/fmtguide.json");
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);

        cached = JsonSerializer.Deserialize<EXHostSchema>(reader.ReadToEnd(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new EXHostSchema();

        return cached;
    }
}

public static class EXHostCompletionData
{
    public static IReadOnlyDictionary<string, string[]> InputsByClassname() =>
        GlobalEditorData.RegisteredEntityMeta.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Inputs?.ToArray() ?? Array.Empty<string>());

    public static IReadOnlyDictionary<string, string> PlacedEntityNames()
    {
        var map = new Dictionary<string, string>();
        foreach (var e in MapTools.Entities)
        {
            if (string.IsNullOrEmpty(e.Name)) continue;
            map[e.Name] = e.EntityName;
        }
        return map;
    }
}

public static class EXStdLibCompletionData
{
    public static IReadOnlyList<EXCompletionItem> GetCompletionItems()
    {
        return EXStdLib.GetRegisteredFunctions()
            .GroupBy(f => f.Name)
            .Select(g =>
            {
                var docs = g.Where(f => !string.IsNullOrEmpty(f.Doc)).Select(f => f.Doc).Distinct().ToList();
                var arities = g.Select(f => f.Arity).Where(a => a >= 0).Distinct().OrderBy(a => a).ToList();

                string doc = docs.Count > 0
                    ? string.Join(" / ", docs)
                    : arities.Count > 0
                        ? $"stdlib function ({string.Join(" or ", arities)} arg(s))"
                        : "stdlib function";

                return new EXCompletionItem(g.Key, "stdlib", doc);
            })
            .ToList();
    }
}
public class EXHostSchema
{
    public Dictionary<string, EXReservedIdentifier> ReservedIdentifiers { get; set; } = new();
    public Dictionary<string, EXTypeDef> Types { get; set; } = new();
    public List<EXGlobalFunctionDef> GlobalFunctions { get; set; } = new();
    public HashSet<string> HiddenInputs { get; set; } = new();
}
public class EXReservedIdentifier
{
    public string Type { get; set; }
    public bool Nullable { get; set; }
    public string Doc { get; set; }
}

public class EXTypeDef
{
    public List<EXMemberDef> Members { get; set; } = new();
    public EXIndexerDef Indexer { get; set; }
}
public class EXParamDef
{
    public string Name { get; set; }
    public string Type { get; set; }
    public bool Optional { get; set; }
}
public class EXMemberDef
{
    public string Name { get; set; }
    public string Kind { get; set; } // "field" | "method"
    public string ValueType { get; set; } // for fields
    public bool Variadic { get; set; }
    public string VariadicElementType { get; set; }
    public List<EXParamDef> Parameters { get; set; } = new();
    public bool ReadOnly { get; set; }
    public string Doc { get; set; }

    [JsonIgnore]
    public int Arity => Variadic ? -1 : Parameters.Count;
}

public class EXIndexerDef
{
    public string KeyType { get; set; }
    public string Doc { get; set; }
}

public class EXGlobalFunctionDef
{
    public string Name { get; set; }
    public bool Variadic { get; set; }
    public string VariadicElementType { get; set; }
    public List<EXParamDef> Parameters { get; set; } = new();
    public string Doc { get; set; }

    [JsonIgnore]
    public int Arity => Variadic ? -1 : Parameters.Count;
}
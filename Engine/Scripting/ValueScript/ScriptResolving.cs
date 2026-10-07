using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.ValueScript;

public static class InheritanceResolver
{
    public static ScriptBlock Resolve(ScriptBlock root)
    {
        var lookup = new Dictionary<string, ScriptEntry>();
        foreach (var entry in root.Entries)
        {
            if (entry.Key != null)
                lookup[entry.Key] = entry;
        }

        var resolved = new ScriptBlock();
        var resolving = new HashSet<string>();

        foreach (var entry in root.Entries)
        {
            resolved.Entries.Add(ResolveEntry(entry, lookup, resolving));
        }

        return resolved;
    }

    static ScriptEntry ResolveEntry(ScriptEntry entry, Dictionary<string, ScriptEntry> lookup, HashSet<string> resolving)
    {
        if (entry.Value.Kind != ScriptValueKind.Block || entry.Value.Block.BaseRef == null)
            return entry;

        string baseRef = entry.Value.Block.BaseRef;

        if (resolving.Contains(entry.Key))
            throw new Exception($"Script parse: circular inheritance detected involving '{entry.Key}'.");

        if (!lookup.TryGetValue(baseRef, out var baseEntry))
            throw new Exception($"Script parse: '{entry.Key}' inherits from unknown base '{baseRef}'.");

        resolving.Add(entry.Key);

        var resolvedBase = ResolveEntry(baseEntry, lookup, resolving);

        resolving.Remove(entry.Key);

        var merged = new Dictionary<string, ScriptEntry>();
        foreach (var baseChild in resolvedBase.Value.Block.Entries)
        {
            if (baseChild.Key != null)
                merged[baseChild.Key] = baseChild;
        }
        foreach (var ownChild in entry.Value.Block.Entries)
        {
            if (ownChild.Key != null)
                merged[ownChild.Key] = ownChild;
        }

        var mergedBlock = new ScriptBlock();
        mergedBlock.Entries.AddRange(merged.Values);

        return new ScriptEntry
        {
            Key = entry.Key,
            Line = entry.Line,
            Value = new ScriptValue { Kind = ScriptValueKind.Block, Block = mergedBlock }
        };
    }
}

public static class VariableResolver
{
    public static ScriptBlock Resolve(ScriptBlock root)
    {
        var symbols = new Dictionary<string, ScriptValue>();
        var remaining = new List<ScriptEntry>();

        foreach(var entry in root.Entries)
        {
            if(entry.Key != null && entry.Key.StartsWith("$"))
            {
                symbols[entry.Key] = entry.Value;
            }
            else
            {
                remaining.Add(entry);
            }
        }

        var result = new ScriptBlock();
        foreach (var entry in remaining)
        {
            result.Entries.Add(SubstituteEntry(entry,symbols));
        }
        return result;
    }
    static ScriptEntry SubstituteEntry(ScriptEntry entry, Dictionary<string, ScriptValue> symbols)
    {
        // return a new ScriptEntry with the same Key/Line, but with
        // Value replaced by SubstituteValue(entry.Value, symbols)

        ScriptEntry newEntry = new ScriptEntry();

        newEntry.Line = entry.Line;
        newEntry.Key = entry.Key;

        newEntry.Value = SubstituteValue(entry.Value, symbols);

        return newEntry;
    }

    static ScriptValue SubstituteValue(ScriptValue value, Dictionary<string, ScriptValue> symbols)
    {
        switch (value.Kind)
        {
            case ScriptValueKind.String:
            case ScriptValueKind.Number:
            case ScriptValueKind.Boolean:
                return value;

            case ScriptValueKind.Identifier:
                if (!value.StringValue.StartsWith("$"))
                    return value;

                if (!symbols.TryGetValue(value.StringValue, out var resolved))
                    throw new Exception($"Script parse: undefined variable '{value.StringValue}'.");

                return resolved;

            case ScriptValueKind.Block:
                return new ScriptValue
                {
                    Kind = ScriptValueKind.Block,
                    Block = SubstituteBlock(value.Block, symbols)
                };

            case ScriptValueKind.FunctionCall:
                return new ScriptValue
                {
                    Kind = ScriptValueKind.FunctionCall,
                    FunctionCall = SubstituteFunctionCall(value.FunctionCall, symbols)
                };

            default:
                throw new Exception($"Script parse: unhandled value kind {value.Kind} during variable substitution.");
        }
    }
    static ScriptBlock SubstituteBlock(ScriptBlock block, Dictionary<string, ScriptValue> symbols)
    {
        var result = new ScriptBlock();
        result.BaseRef = block.BaseRef;

        foreach (var entry in block.Entries)
        {
            result.Entries.Add(SubstituteEntry(entry, symbols));
        }

        return result;
    }

    static ScriptFunctionCall SubstituteFunctionCall(ScriptFunctionCall call, Dictionary<string, ScriptValue> symbols)
    {
        var result = new ScriptFunctionCall();
        result.Name = call.Name;

        foreach (var arg in call.Arguments)
        {
            result.Arguments.Add(SubstituteValue(arg, symbols));
        }

        if (call.TrailingBlock != null)
        {
            result.TrailingBlock = SubstituteBlock(call.TrailingBlock, symbols);
        }

        return result;
    }
}
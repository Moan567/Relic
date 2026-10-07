using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.ValueScript; 
public static class CoreScriptFunctions
{
    static Random rng = new();

    public static void RegisterDefaults(EvaluationContext context)
    {
        context.RegisterFunction("random", (call, ctx) =>
        {
            float min = (float)call.Arguments[0].NumberValue;
            float max = (float)call.Arguments[1].NumberValue;
            return new ScriptValue { Kind = ScriptValueKind.Number, NumberValue = min + rng.NextDouble() * (max - min) };
        });

        context.RegisterFunction("select", (call, ctx) =>
        {
            string strategy = call.Arguments[0].StringValue;
            var candidates = call.TrailingBlock.Entries;
            return strategy switch
            {
                "random" => candidates[rng.Next(candidates.Count)].Value,
                "sequential" => candidates[ScriptEvaluator.AdvanceSequentialCursor(ctx.CursorKey, candidates.Count)].Value,
                _ => throw new Exception($"Unknown select strategy '{strategy}'.")
            };
        });
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.ValueScript;
public class EvaluationContext
{
    public string CursorKey;
    public Dictionary<string, ScriptFunction> Functions = new();
    public Func<string, ScriptValue> ResolveVariable;

    public void RegisterFunction(string name, ScriptFunction fn) => Functions[name] = fn;
}
public delegate ScriptValue ScriptFunction(ScriptFunctionCall call, EvaluationContext context);
public static class ScriptEvaluator
{
    static Dictionary<string, int> sequentialCursors = new();

    public static int AdvanceSequentialCursor(string cursorKey, int candidateCount)
    {
        int index = sequentialCursors.TryGetValue(cursorKey, out var i) ? i : 0;
        sequentialCursors[cursorKey] = index + 1;
        return index % candidateCount;
    }
    public static ScriptValue Evaluate(ScriptValue value, EvaluationContext context)
    {
        switch (value.Kind)
        {
            case ScriptValueKind.Number:
            case ScriptValueKind.Boolean:
            case ScriptValueKind.String:
            case ScriptValueKind.Identifier:
                return value;

            case ScriptValueKind.Variable:
                if (context.ResolveVariable == null)
                    throw new Exception($"No variable resolver available for '#{value.StringValue}'.");
                return Evaluate(context.ResolveVariable(value.StringValue), context);

            case ScriptValueKind.FunctionCall:
                if (!context.Functions.TryGetValue(value.FunctionCall.Name, out var fn))
                    throw new Exception($"Unknown function '{value.FunctionCall.Name}'.");
                return Evaluate(fn(value.FunctionCall, context), context);

            case ScriptValueKind.BinaryOp:
                float left = EvaluateFloat(value.Left, context);
                float right = EvaluateFloat(value.Right, context);
                float result = value.Operator switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => left / right,
                    _ => throw new Exception($"Unknown operator '{value.Operator}'.")
                };
                return new ScriptValue { Kind = ScriptValueKind.Number, NumberValue = result };

            default:
                throw new Exception($"Cannot evaluate {value.Kind}.");
        }
    }

    public static float EvaluateFloat(ScriptValue value, EvaluationContext context)
    {
        var result = Evaluate(value, context);
        if (result.Kind != ScriptValueKind.Number)
            throw new Exception($"Cannot evaluate {result.Kind} as a number.");
        return (float)result.NumberValue;
    }

    public static bool EvaluateBool(ScriptValue value, EvaluationContext context)
    {
        var result = Evaluate(value, context);
        if (result.Kind != ScriptValueKind.Boolean)
            throw new Exception($"Cannot evaluate {result.Kind} as a boolean.");
        return result.BooleanValue;
    }

    public static string EvaluateString(ScriptValue value, EvaluationContext context)
    {
        var result = Evaluate(value, context);
        if (result.Kind != ScriptValueKind.String)
            throw new Exception($"Cannot evaluate {result.Kind} as a string.");
        return result.StringValue;
    }
    public static List<ScriptValue> EvaluateList(ScriptValue value, EvaluationContext context)
    {
        if (value.Kind == ScriptValueKind.Block)
        {
            return value.Block.Entries
                .Select(entry => Evaluate(entry.Value, context))
                .ToList();
        }

        return new List<ScriptValue> { Evaluate(value, context) };
    }
}
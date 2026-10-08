using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Chisel.EXScript;
public static class EXStdLib
{
    public readonly struct EXStdLibParamInfo
    {
        public string Name { get; init; }
        public string Type { get; init; } // "any" for unconstrained
    }

    public readonly struct EXStdLibFunctionInfo
    {
        public string Name { get; init; }
        public int Arity { get; init; } // -1 = variadic
        public string Doc { get; init; }
        public EXStdLibParamInfo[] Parameters { get; init; } // empty when variadic
        public string VariadicElementType { get; init; } // null unless variadic
    }

    public delegate EXValue StdFunc(EXValue[] args);

    static readonly Dictionary<string, List<(int arity, StdFunc fn, string doc, (string name, string type)[] parameters, string variadicElementType)>> functions = new();

    public static IEnumerable<EXStdLibFunctionInfo> GetRegisteredFunctions() =>
        functions.SelectMany(kvp => kvp.Value.Select(o => new EXStdLibFunctionInfo
        {
            Name = kvp.Key,
            Arity = o.arity,
            Doc = o.doc,
            Parameters = o.parameters.Select(p => new EXStdLibParamInfo { Name = p.name, Type = p.type }).ToArray(),
            VariadicElementType = o.variadicElementType
        }));

    static void Register(string name, StdFunc fn, string doc, params (string name, string type)[] parameters) =>
        RegisterInternal(name, parameters.Length, fn, doc, parameters, null);

    static void RegisterVariadic(string name, StdFunc fn, string doc, string elementType = "any") =>
        RegisterInternal(name, -1, fn, doc, Array.Empty<(string, string)>(), elementType);

    static void RegisterInternal(string name, int arity, StdFunc fn, string doc, (string name, string type)[] parameters, string variadicElementType)
    {
        if (!functions.TryGetValue(name, out var list))
            functions[name] = list = new();
        list.Add((arity, fn, doc, parameters, variadicElementType));
    }

    public static bool TryInvoke(string name, EXValue[] args, out EXValue result, int line)
    {
        result = null;
        if (!functions.TryGetValue(name, out var overloads)) return false;

        foreach (var (arity, fn, _, _, _) in overloads)
        {
            if (arity == args.Length) { result = fn(args); return true; }
        }

        foreach (var (arity, fn, _, _, _) in overloads)
        {
            if (arity == -1) { result = fn(args); return true; }
        }

        var expected = string.Join(" or ", overloads.Select(o => o.arity).Where(a => a >= 0).Distinct().OrderBy(a => a));
        throw new EXScriptRuntimeError($"'{name}' expects {expected} argument(s), got {args.Length}.", line);
    }

    // For the actual lib...

    static Random rng;

    static EXStdLib()
    {
        rng = new();

        // general math
        Register("random", _ => EXValue.Of(rng.NextDouble()),
            "Returns a random number between 0 and 1.");
        Register("random", a => EXValue.Of(rng.NextDouble() * Num(a[0], "random")),
            "Returns a random number between 0 and max.",
            ("max", "number"));
        Register("random", a => EXValue.Of(rng.NextDouble() * (Num(a[1], "random") - Num(a[0], "random")) + Num(a[0], "random")),
            "Returns a random number between min and max.",
            ("min", "number"), ("max", "number"));
        Register("min", a => EXValue.Of(Math.Min(Num(a[0], "min"), Num(a[1], "min"))),
            "Returns the smaller of two numbers.",
            ("a", "number"), ("b", "number"));
        Register("max", a => EXValue.Of(Math.Max(Num(a[0], "max"), Num(a[1], "max"))),
            "Returns the larger of two numbers.",
            ("a", "number"), ("b", "number"));
        Register("abs", a => EXValue.Of(Math.Abs(Num(a[0], "abs"))),
            "Returns the absolute value of a number.",
            ("n", "number"));
        Register("floor", a => EXValue.Of(Math.Floor(Num(a[0], "floor"))),
            "Rounds a number down to the nearest integer.",
            ("n", "number"));
        Register("ceil", a => EXValue.Of(Math.Ceiling(Num(a[0], "ceil"))),
            "Rounds a number up to the nearest integer.",
            ("n", "number"));
        Register("round", a => EXValue.Of(Math.Round(Num(a[0], "round"))),
            "Rounds a number to the nearest integer.",
            ("n", "number"));
        Register("clamp", a => EXValue.Of(Math.Clamp(Num(a[0], "clamp"), Num(a[1], "clamp"), Num(a[2], "clamp"))),
            "Clamps a number between a minimum and maximum, in that order.",
            ("value", "number"), ("min", "number"), ("max", "number"));
        Register("lerp", a => EXValue.Of(Num(a[0], "lerp") + (Num(a[1], "lerp") - Num(a[0], "lerp")) * Num(a[2], "lerp")),
            "Linearly interpolates from a to b by t, where t is typically 0-1.",
            ("a", "number"), ("b", "number"), ("t", "number"));

        // constants
        Register("pi", _ => EXValue.Of(Math.PI),
            "The constant π (3.14159...).");
        Register("e", _ => EXValue.Of(Math.E),
            "The constant e (2.71828...).");

        // strings
        Register("length", a => EXValue.Of((double)Str(a[0], "length").Length),
            "Returns the number of characters in a string.",
            ("s", "string"));
        Register("upper", a => EXValue.Of(Str(a[0], "upper").ToUpperInvariant()),
            "Returns an uppercase copy of a string.",
            ("s", "string"));
        Register("lower", a => EXValue.Of(Str(a[0], "lower").ToLowerInvariant()),
            "Returns a lowercase copy of a string.",
            ("s", "string"));
        Register("contains", a => EXValue.Of(Str(a[0], "contains").Contains(Str(a[1], "contains"))),
            "Returns true if the first string contains the second as a substring.",
            ("s", "string"), ("substring", "string"));

        // vectors
        Register("vec3", a => EXValue.Of(new EXVec3D(Num(a[0], "vec3"), Num(a[1], "vec3"), Num(a[2], "vec3"))),
            "Constructs a 3D vector from x, y, z.",
            ("x", "number"), ("y", "number"), ("z", "number"));
        Register("vec2", a => EXValue.Of(new EXVec2D(Num(a[0], "vec2"), Num(a[1], "vec2"))),
            "Constructs a 2D vector from x, y.",
            ("x", "number"), ("y", "number"));
        Register("dot", a => EXValue.Of(Dot(a[0], a[1])),
            "Returns the dot product of two vectors of the same dimension.",
            ("a", "vector"), ("b", "vector"));
        Register("cross", a => Cross(a[0], a[1]),
            "Returns the cross product of two 3D vectors, or the scalar cross of two 2D vectors.",
            ("a", "vector"), ("b", "vector"));
        Register("magnitude", a => EXValue.Of(Magnitude(a[0])),
            "Returns the length of a vector.",
            ("v", "vector"));
        Register("normalize", a => Normalize(a[0]),
            "Returns a unit-length copy of a vector. Throws if the vector has zero length.",
            ("v", "vector"));
        Register("distance", a => EXValue.Of(Magnitude(Subtract(a[0], a[1]))),
            "Returns the distance between two vectors of the same dimension.",
            ("a", "vector"), ("b", "vector"));

        // arrays
        Register("array", a => EXValue.Of(Enumerable.Repeat(EXValue.Null(), (int)Num(a[0], "array")).ToArray()),
            "Creates a new fixed-size array of the given length, filled with null.",
            ("size", "number"));
        RegisterVariadic("arrayof", a => EXValue.Of(a),
            "Creates an array containing exactly the given arguments, in order.",
            "any");
        Register("resize", a =>
        {
            if (a[0].Kind != EXKind.Array) throw new EXScriptRuntimeError($"'resize' expects an array, got {a[0].Kind}.", -1);
            int newSize = (int)Num(a[1], "resize");
            var newArr = new EXValue[newSize];
            for (int i = 0; i < newSize; i++)
                newArr[i] = i < a[0].Array.Length ? a[0].Array[i] : EXValue.Null();
            return EXValue.Of(newArr);
        },
            "Returns a copy of an array resized to the given length.",
            ("arr", "array"), ("newSize", "number"));
        Register("size", a =>
        {
            if (a[0].Kind != EXKind.Array) throw new EXScriptRuntimeError($"'size' expects an array, got {a[0].Kind}.", -1);
            return EXValue.Of(a[0].Array.Length);
        },
            "Returns the number of elements in an array.",
            ("arr", "array"));

        // conversion
        Register("tostring", a => EXValue.Of(EXRuntime.Stringify(a[0])),
            "Converts any value to its string representation.",
            ("value", "any"));
        Register("tonumber", a => EXValue.Of(ToNumber(a[0])),
            "Converts a number, numeric string, or bool to a number.",
            ("value", "any"));
        Register("tobool", a => EXValue.Of(ToBool(a[0])),
            "Converts a bool, or the strings \"1\"/\"true\" (case-insensitive), to a bool. Anything else throws.",
            ("value", "any"));

        // 'reflection'
        Register("hasmember", a =>
        {
            if (a[0].Kind != EXKind.Object) return EXValue.Of(false);
            return EXValue.Of(a[0].Object.Type.TryGetMember(a[0].Object.Instance, Str(a[1], "hasmember"), out _) ||
                              a[0].Object.Type.TryGetIndexer(a[0].Object.Instance, a[1], out _));
        },
            "Returns true if the given object has a readable member/indexer with this name.",
            ("obj", "object"), ("name", "string"));

        // assert
        Register("assert", a => { RequireTrue(a[0], "Assertion failed."); return EXValue.Null(); },
            "Throws a runtime error if the value is not exactly true.",
            ("condition", "bool"));
        Register("assert", a => { RequireTrue(a[0], a[1].Kind == EXKind.String ? a[1].String : "Assertion failed."); return EXValue.Null(); },
            "Throws a runtime error with the given message if the value is not exactly true.",
            ("condition", "bool"), ("message", "string"));
    }
    static double Dot(EXValue a, EXValue b)
    {
        if (a.Kind == EXKind.Vector3D && b.Kind == EXKind.Vector3D)
            return a.Vector3D.X * b.Vector3D.X + a.Vector3D.Y * b.Vector3D.Y + a.Vector3D.Z * b.Vector3D.Z;
        if (a.Kind == EXKind.Vector2D && b.Kind == EXKind.Vector2D)
            return a.Vector2D.X * b.Vector2D.X + a.Vector2D.Y * b.Vector2D.Y;
        throw new EXScriptRuntimeError($"'dot' expects two vectors of the same dimension, got {a.Kind} and {b.Kind}.", -1);
    }

    static EXValue Cross(EXValue a, EXValue b)
    {
        if (a.Kind == EXKind.Vector3D && b.Kind == EXKind.Vector3D)
        {
            var (av, bv) = (a.Vector3D, b.Vector3D);
            return EXValue.Of(new EXVec3D(
                av.Y * bv.Z - av.Z * bv.Y,
                av.Z * bv.X - av.X * bv.Z,
                av.X * bv.Y - av.Y * bv.X));
        }
        if (a.Kind == EXKind.Vector2D && b.Kind == EXKind.Vector2D)
            return EXValue.Of(a.Vector2D.X * b.Vector2D.Y - a.Vector2D.Y * b.Vector2D.X);
        throw new EXScriptRuntimeError($"'cross' expects two vectors of the same dimension, got {a.Kind} and {b.Kind}.", -1);
    }
    static double Magnitude(EXValue v) => v.Kind switch
    {
        EXKind.Vector3D => Math.Sqrt(v.Vector3D.X * v.Vector3D.X + v.Vector3D.Y * v.Vector3D.Y + v.Vector3D.Z * v.Vector3D.Z),
        EXKind.Vector2D => Math.Sqrt(v.Vector2D.X * v.Vector2D.X + v.Vector2D.Y * v.Vector2D.Y),
        _ => throw new EXScriptRuntimeError($"'magnitude' expects a vector, got {v.Kind}.", -1)
    };
    static EXValue Normalize(EXValue v)
    {
        var m = Magnitude(v);
        if (m == 0)
            throw new EXScriptRuntimeError("Cannot normalize a zero-length vector.", -1);
        return v.Kind == EXKind.Vector3D
            ? EXValue.Of(new EXVec3D(v.Vector3D.X / m, v.Vector3D.Y / m, v.Vector3D.Z / m))
            : EXValue.Of(new EXVec2D(v.Vector2D.X / m, v.Vector2D.Y / m));
    }
    static EXValue Subtract(EXValue a, EXValue b) => a.Kind switch
    {
        EXKind.Vector3D => EXValue.Of(new EXVec3D(a.Vector3D.X - b.Vector3D.X, a.Vector3D.Y - b.Vector3D.Y, a.Vector3D.Z - b.Vector3D.Z)),
        EXKind.Vector2D => EXValue.Of(new EXVec2D(a.Vector2D.X - b.Vector2D.X, a.Vector2D.Y - b.Vector2D.Y)),
        _ => throw new EXScriptRuntimeError($"Expected a vector, got {a.Kind}.", -1)
    };

    static double Num(EXValue v, string fn) =>
        v.Kind == EXKind.Number ? v.Number : throw new EXScriptRuntimeError($"'{fn}' expects a number, got {v.Kind}.", -1);

    static string Str(EXValue v, string fn) =>
        v.Kind == EXKind.String ? v.String : throw new EXScriptRuntimeError($"'{fn}' expects a string, got {v.Kind}.", -1);

    static void RequireTrue(EXValue v, string message)
    {
        if (v.Kind != EXKind.Bool || !v.Bool)
            throw new EXScriptRuntimeError(message, -1);
    }

    static double ToNumber(EXValue v) => v.Kind switch
    {
        EXKind.Number => v.Number,
        EXKind.String when double.TryParse(v.String, out var n) => n,
        EXKind.Bool => v.Bool ? 1 : 0,
        _ => throw new EXScriptRuntimeError($"Cannot convert {v.Kind} to a number.", -1)
    };

    static bool ToBool(EXValue v) => v.Kind switch
    {
        EXKind.Bool => v.Bool,
        EXKind.String => v.String == "1" || string.Equals(v.String, "true", StringComparison.OrdinalIgnoreCase),
        _ => throw new EXScriptRuntimeError($"Cannot convert {v.Kind} to a bool.", -1)
    };
}
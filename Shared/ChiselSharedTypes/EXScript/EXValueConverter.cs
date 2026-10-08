using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace Chisel.EXScript;

public static class EXValueConverter
{
    public static Vector3 AsVector3(EXValue value) => value.Kind == EXKind.Vector3D
        ? new Vector3((float)value.Vector3D.X, (float)value.Vector3D.Y, (float)value.Vector3D.Z)
        : throw Mismatch(EXKind.Vector3D, value);

    public static Vector2 AsVector2(EXValue value) => value.Kind == EXKind.Vector2D
        ? new Vector2((float)value.Vector2D.X, (float)value.Vector2D.Y)
        : throw Mismatch(EXKind.Vector2D, value);

    public static double AsDouble(EXValue value) => value.Kind == EXKind.Number
        ? value.Number
        : throw Mismatch(EXKind.Number, value);

    public static float AsFloat(EXValue value) => (float)AsDouble(value);
    public static int AsInt(EXValue value) => (int)AsDouble(value);

    public static bool AsBool(EXValue value) => value.Kind == EXKind.Bool
        ? value.Bool
        : throw Mismatch(EXKind.Bool, value);

    public static string AsString(EXValue value) => value.Kind == EXKind.String
        ? value.String
        : throw Mismatch(EXKind.String, value);

    public static EXValue[] AsArray(EXValue value) => value.Kind == EXKind.Array
        ? value.Array
        : throw Mismatch(EXKind.Array, value);

    public static T AsObject<T>(EXValue value) where T : class
    {
        if (value.Kind != EXKind.Object)
            throw new EXScriptRuntimeError($"Expected an object of type '{typeof(T).Name}', got {value.Kind}.", -1);

        if (value.Object.Instance is not T typed)
            throw new EXScriptRuntimeError(
                $"Expected an object backed by '{typeof(T).Name}', but got '{value.Object.Type.TypeName}' " +
                $"(backed by '{value.Object.Instance?.GetType().Name ?? "null"}').", -1);

        return typed;
    }

    public static bool TryAsObject<T>(EXValue value, out T result) where T : class
    {
        result = value.Kind == EXKind.Object ? value.Object.Instance as T : null;
        return result != null;
    }

    public static EXValue From(Vector3 v) => EXValue.Of(new EXVec3D(v.X, v.Y, v.Z));
    public static EXValue From(Vector2 v) => EXValue.Of(new EXVec2D(v.X, v.Y));
    public static EXValue From(double v) => EXValue.Of(v);
    public static EXValue From(bool v) => EXValue.Of(v);
    public static EXValue From(string v) => v == null ? EXValue.Null() : EXValue.Of(v);
    public static EXValue From(EXValue[] v) => v == null ? EXValue.Null() : EXValue.Of(v);

    public static EXValue From(object instance, IEXObjectType type) =>
        instance == null ? EXValue.Null() : EXValue.Of(new EXObject { Type = type, Instance = instance });

    static EXScriptRuntimeError Mismatch(EXKind expected, EXValue actual) =>
        new($"Expected {expected}, got {actual.Kind}.", -1);
}

using Chisel.EXScript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils;

public static class MapGlobals
{
    internal static Dictionary<string, EXValue> values = new Dictionary<string, EXValue>();

    public static bool HasValue(string name) => values.ContainsKey(name);

    public static EXValue ReadValue(string name) =>
        values.TryGetValue(name, out var v) ? v : EXValue.Null();

    public static void SetValue(string name, EXValue value) => values[name] = value;

    public static void RemoveValue(string name) => values.Remove(name);

    public static void Clear() => values.Clear();

    // ---- bool ----
    public static bool ReadBool(string name) => EXValueConverter.AsBool(ReadValue(name));
    public static void SetBool(string name, bool v) => SetValue(name, EXValueConverter.From(v));

    // ---- number ----
    public static double ReadNumber(string name) => EXValueConverter.AsDouble(ReadValue(name));
    public static void SetNumber(string name, double v) => SetValue(name, EXValueConverter.From(v));

    // ---- string ----
    public static string ReadString(string name) => EXValueConverter.AsString(ReadValue(name));
    public static void SetString(string name, string v) => SetValue(name, EXValueConverter.From(v));

    // ---- vectors ----
    public static Microsoft.Xna.Framework.Vector3 ReadVector3(string name) => EXValueConverter.AsVector3(ReadValue(name));
    public static void SetVector3(string name, Microsoft.Xna.Framework.Vector3 v) => SetValue(name, EXValueConverter.From(v));

    public static Microsoft.Xna.Framework.Vector2 ReadVector2(string name) => EXValueConverter.AsVector2(ReadValue(name));
    public static void SetVector2(string name, Microsoft.Xna.Framework.Vector2 v) => SetValue(name, EXValueConverter.From(v));

    public static Dictionary<string, PackedEXValue> Save() => EXValuePersistence.PackDict(values);
    public static void Load(Dictionary<string, PackedEXValue> packed) => values = EXValuePersistence.UnpackDict(packed);
}
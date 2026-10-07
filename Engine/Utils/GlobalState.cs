using Chisel.EXScript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils
{
    public static class GlobalState
    {
        internal static Dictionary<string, EXValue> states = new Dictionary<string, EXValue>();

        public static void AddState(string name, EXValue initialValue)
        {
            if (states.ContainsKey(name)) return;
            states.Add(name, initialValue);
        }

        public static void RemoveState(string name) => states.Remove(name);

        public static bool HasState(string name) => states.ContainsKey(name);

        public static EXValue ReadValue(string name) =>
            states.TryGetValue(name, out var v) ? v : EXValue.Null();

        public static bool TrySetValue(string name, EXValue value)
        {
            if (!states.ContainsKey(name)) return false;
            states[name] = value;
            return true;
        }

        // ---- bool ----

        public static void AddState(string name, bool initialValue) => AddState(name, EXValueConverter.From(initialValue));
        public static bool ReadState(string name) => EXValueConverter.AsBool(ReadValue(name));
        public static void SetState(string name, bool v) => TrySetValue(name, EXValueConverter.From(v));

        // ---- number ----

        public static void AddNumber(string name, double initialValue) => AddState(name, EXValueConverter.From(initialValue));
        public static double ReadNumber(string name) => EXValueConverter.AsDouble(ReadValue(name));
        public static void SetNumber(string name, double v) => TrySetValue(name, EXValueConverter.From(v));

        // ---- string ----

        public static void AddString(string name, string initialValue) => AddState(name, EXValueConverter.From(initialValue));
        public static string ReadString(string name) => EXValueConverter.AsString(ReadValue(name));
        public static void SetString(string name, string v) => TrySetValue(name, EXValueConverter.From(v));

        // ---- vectors ----

        public static void AddVector3(string name, Microsoft.Xna.Framework.Vector3 initialValue) => AddState(name, EXValueConverter.From(initialValue));
        public static Microsoft.Xna.Framework.Vector3 ReadVector3(string name) => EXValueConverter.AsVector3(ReadValue(name));
        public static void SetVector3(string name, Microsoft.Xna.Framework.Vector3 v) => TrySetValue(name, EXValueConverter.From(v));

        public static void AddVector2(string name, Microsoft.Xna.Framework.Vector2 initialValue) => AddState(name, EXValueConverter.From(initialValue));
        public static Microsoft.Xna.Framework.Vector2 ReadVector2(string name) => EXValueConverter.AsVector2(ReadValue(name));
        public static void SetVector2(string name, Microsoft.Xna.Framework.Vector2 v) => TrySetValue(name, EXValueConverter.From(v));

        public static Dictionary<string, PackedEXValue> Save() => EXValuePersistence.PackDict(states);
        public static void Load(Dictionary<string, PackedEXValue> packed) => states = EXValuePersistence.UnpackDict(packed);
    }
}

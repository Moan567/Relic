using Chisel.EXScript;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Engine.Utils
{
    public static class EXObjectTypeRegistry
    {
        static readonly Dictionary<string, IEXObjectType> types = new();
        public static void Register(IEXObjectType type) => types[type.TypeName] = type;
        public static bool TryGet(string typeName, out IEXObjectType type) => types.TryGetValue(typeName, out type);
    }

    public class PackedEXValue
    {
        public string Kind;
        public object Data;
    }

    public class ObjectToken
    {
        public string TypeName;
        public string Token;
    }

    public static class EXValuePersistence
    {
        static readonly List<Action> pendingResolutions = new();

        public static PackedEXValue Pack(EXValue v) => v.Kind switch
        {
            EXKind.Number => Tag("number", v.Number),
            EXKind.String => Tag("string", v.String),
            EXKind.Bool => Tag("bool", v.Bool),
            EXKind.Null => Tag("null", null),
            EXKind.Vector3D => Tag("vec3", new[] { v.Vector3D.X, v.Vector3D.Y, v.Vector3D.Z }),
            EXKind.Vector2D => Tag("vec2", new[] { v.Vector2D.X, v.Vector2D.Y }),
            EXKind.Array => Tag("array", v.Array.Select(Pack).ToList()),
            EXKind.Object => PackObject(v.Object),
            _ => throw new EXScriptRuntimeError($"No persistence support for EXKind.{v.Kind}.", -1)
        };

        static PackedEXValue PackObject(EXObject obj)
        {
            if (obj?.Type == null) return new PackedEXValue { Kind = "null" };
            if (!obj.Type.TryGetPersistToken(obj.Instance, out var token))
                throw new EXScriptRuntimeError($"'{obj.Type.TypeName}' does not support being saved.", -1);
            return new PackedEXValue { Kind = "object", Data = new ObjectToken { TypeName = obj.Type.TypeName, Token = token } };
        }

        static PackedEXValue Tag(string kind, object data) => new() { Kind = kind, Data = data };

        public static EXValue Unpack(PackedEXValue packed, Action<EXValue> onResolved = null)
        {
            if (packed == null) return EXValue.Null();

            if (packed.Kind == "object")
            {
                var tok = packed.Data switch
                {
                    ObjectToken ot => ot,
                    JObject jo => jo.ToObject<ObjectToken>(),
                    _ => throw new EXScriptRuntimeError($"Malformed object token during restore (got {packed.Data?.GetType().Name}).", -1)
                };

                pendingResolutions.Add(() =>
                {
                    EXValue resolved = EXObjectTypeRegistry.TryGet(tok.TypeName, out var type)
                                        && type.TryResolveFromToken(tok.Token, out var instance)
                        ? EXValue.Of(new EXObject { Type = type, Instance = instance })
                        : EXValue.Null(); // the object this pointed to no longer exists

                    onResolved?.Invoke(resolved);
                });

                return EXValue.Null(); // placeholder until ResolveAllPending runs
            }

            return packed.Kind switch
            {
                "number" => EXValue.Of(ToDouble(packed.Data)),
                "string" => EXValue.Of(ToStringValue(packed.Data)),
                "bool" => EXValue.Of(ToBool(packed.Data)),
                "null" => EXValue.Null(),
                "vec3" => UnpackVec3(packed.Data),
                "vec2" => UnpackVec2(packed.Data),
                "array" => UnpackArray(packed.Data),
                _ => throw new EXScriptRuntimeError($"Unknown persisted EXValue kind '{packed.Kind}'.", -1)
            };
        }

        public static void ResolveAllPending()
        {
            foreach (var fix in pendingResolutions) fix();
            pendingResolutions.Clear();
        }

        static double ToDouble(object raw) => raw switch
        {
            double d => d,
            long l => l,
            int i => i,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => throw new EXScriptRuntimeError($"Expected a number while restoring, got {raw?.GetType().Name}.", -1)
        };

        static string ToStringValue(object raw) => raw switch
        {
            string s => s,
            null => null,
            _ => throw new EXScriptRuntimeError($"Expected a string while restoring, got {raw.GetType().Name}.", -1)
        };

        static bool ToBool(object raw) => raw switch
        {
            bool b => b,
            long l => l != 0,
            double d => d != 0,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => throw new EXScriptRuntimeError($"Expected a bool while restoring, got {raw?.GetType().Name}.", -1)
        };

        static double[] ToDoubleArray(object raw) => raw switch
        {
            double[] arr => arr,
            long[] arr => arr.Select(x => (double)x).ToArray(),
            JArray ja => ja.Select(t => t.Value<double>()).ToArray(),
            _ => throw new EXScriptRuntimeError($"Expected a numeric array while restoring, got {raw?.GetType().Name}.", -1)
        };

        static EXValue UnpackVec3(object raw)
        {
            var a = ToDoubleArray(raw);
            return EXValue.Of(new EXVec3D(a[0], a[1], a[2]));
        }

        static EXValue UnpackVec2(object raw)
        {
            var a = ToDoubleArray(raw);
            return EXValue.Of(new EXVec2D(a[0], a[1]));
        }

        static EXValue UnpackArray(object raw)
        {
            var items = (raw switch
            {
                List<PackedEXValue> l => l,
                JArray ja => ja.Select(t => t.ToObject<PackedEXValue>()),
                _ => throw new EXScriptRuntimeError($"Expected an array while restoring, got {raw?.GetType().Name}.", -1)
            }).ToList();

            var result = new EXValue[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                result[i] = Unpack(items[i], resolved => result[idx] = resolved);
            }
            return EXValue.Of(result);
        }

        public static Dictionary<string, PackedEXValue> PackDict(Dictionary<string, EXValue> dict)
        {
            var result = new Dictionary<string, PackedEXValue>(dict.Count);
            foreach (var (k, v) in dict) result[k] = Pack(v);
            return result;
        }

        public static Dictionary<string, EXValue> UnpackDict(Dictionary<string, PackedEXValue> packed)
        {
            var result = new Dictionary<string, EXValue>(packed?.Count ?? 0);
            if (packed == null) return result;

            foreach (var (k, p) in packed)
            {
                var key = k; // capture for the closure
                result[key] = Unpack(p, resolved => result[key] = resolved);
            }
            return result;
        }
    }
}
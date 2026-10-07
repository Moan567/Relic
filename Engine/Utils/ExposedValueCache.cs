using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Engine.Utils;
public static class ExposedValueCache
{
    public record Accessor(Func<object, string> Getter, Action<object, string> Setter, Type ValueType);

    private static readonly Dictionary<Type, Dictionary<string, Accessor>> cache = new();

    public static Dictionary<string, Accessor> GetAccessors(Type type)
    {
        if (cache.TryGetValue(type, out var existing)) return existing;

        var accessors = new Dictionary<string, Accessor>();

        // Walk the full inheritance chain so base class attributes are included
        var current = type;
        while (current != null && current != typeof(object))
        {
            foreach (var field in current.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attr = field.GetCustomAttribute<ExposeValue>();
                if (attr == null) continue;

                var captured = field;
                var convert = ValueConversion.GetConverter(field.FieldType);

                accessors[attr.Key] = new Accessor(
                    instance => captured.GetValue(instance)?.ToString(),
                    (instance, str) => captured.SetValue(instance, convert(str)),
                    field.FieldType);
            }

            foreach (var prop in current.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!prop.CanRead) continue;
                var attr = prop.GetCustomAttribute<ExposeValue>();
                if (attr == null) continue;

                var captured = prop;
                Action<object, string> setter;

                if (prop.CanWrite)
                {
                    var convert = ValueConversion.GetConverter(prop.PropertyType);
                    setter = (instance, str) => captured.SetValue(instance, convert(str));
                }
                else
                {
                    setter = (instance, str) => throw new InvalidOperationException(
                        $"'{attr.Key}' is exposed as read-only on {type.Name}.");
                }

                accessors[attr.Key] = new Accessor(
                    instance => captured.GetValue(instance)?.ToString(),
                    setter,
                    prop.PropertyType);
            }

            current = current.BaseType;
        }

        cache[type] = accessors;
        return accessors;
    }
}
public static class SaveValueCache
{
    public record Accessor(Func<object, object> Getter, Action<object, object> Setter, Type ValueType);
    private static readonly Dictionary<Type, Dictionary<string, Accessor>> cache = new();

    public static Dictionary<string, Accessor> GetCache(Type type)
    {
        if (cache.TryGetValue(type, out var existing)) return existing;

        var accessors = new Dictionary<string, Accessor>();
        var current = type;

        while (current != null && current != typeof(object))
        {
            foreach (var field in current.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attr = field.GetCustomAttribute<SaveValue>();
                if (attr == null) continue;
                var captured = field;
                accessors[attr.Key] = new Accessor(
                    instance => captured.GetValue(instance),
                    (instance, value) => captured.SetValue(instance, value),
                    field.FieldType);
            }

            foreach (var prop in current.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attr = prop.GetCustomAttribute<SaveValue>();
                if (attr == null) continue;
                if (!prop.CanRead) continue;
                var captured = prop;
                accessors[attr.Key] = new Accessor(
                    instance => captured.GetValue(instance),
                    (instance, value) => captured.SetValue(instance, value),
                    prop.PropertyType);
            }

            current = current.BaseType;
        }

        cache[type] = accessors;
        return accessors;
    }
}
public static class ValueConversion
{
    public static object ConvertToFieldType(string value, Type targetType)
    {
        var convert = GetConverter(targetType);
        try
        {
            return convert(value);
        }
        catch (Exception e)
        {
            throw new FormatException(
                $"Could not convert '{value}' to {targetType.Name}: {e.Message}", e);
        }
    }

    public static Func<string, object> GetConverter(Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null) targetType = underlying;

        if (targetType == typeof(string))
            return s => s;

        if (targetType == typeof(bool))
            return s => s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

        if (targetType.IsEnum)
            return s => Enum.Parse(targetType, s, ignoreCase: true);

        if (targetType == typeof(Vector3))
            return ParseVector3;

        if (targetType == typeof(Color))
            return ParseColor;

        if (targetType.IsPrimitive || targetType == typeof(decimal))
            return s => Convert.ChangeType(s, targetType, CultureInfo.InvariantCulture);

        throw new NotSupportedException($"No string conversion registered for type '{targetType.Name}'.");
    }

    private static object ParseVector3(string s)
    {
        var p = s.Split(',');
        if (p.Length < 3)
            throw new FormatException($"Expected 3 comma-separated components, got '{s}'");

        return new Vector3(
            float.Parse(p[0], CultureInfo.InvariantCulture),
            float.Parse(p[1], CultureInfo.InvariantCulture),
            float.Parse(p[2], CultureInfo.InvariantCulture));
    }

    private static object ParseColor(string s)
    {
        var p = s.Split(',');
        if (p.Length < 3)
            throw new FormatException($"Expected 3 comma-separated components, got '{s}'");

        return new Color(byte.Parse(p[0]), byte.Parse(p[1]), byte.Parse(p[2]), (byte)255);
    }
}
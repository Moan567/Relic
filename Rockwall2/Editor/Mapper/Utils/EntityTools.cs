using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Globalization;
using System.Linq;

namespace Rockwall2.Editor.Mapper.Utils;
public static class EntityTools
{
    public static float GetFloatProperty(EntityReference entity, string name, float defaultValue)
    {
        var prop = entity.Properties.FirstOrDefault(p => p.Name == name);
        if (string.IsNullOrWhiteSpace(prop.Value))
            return defaultValue;

        return float.TryParse(prop.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    public static Color GetColorProperty(EntityReference entity, string name, Color defaultValue)
    {
        var prop = entity.Properties.FirstOrDefault(p => p.Name == name);
        if (string.IsNullOrWhiteSpace(prop.Value))
            return defaultValue;

        var parts = prop.Value.Split(',');
        if (parts.Length < 3)
            return defaultValue;

        if (!TryParseByte(parts[0], out var r) ||
            !TryParseByte(parts[1], out var g) ||
            !TryParseByte(parts[2], out var b))
            return defaultValue;

        byte a = 255;
        if (parts.Length >= 4 && byte.TryParse(parts[3].Trim(), out var parsedA))
            a = parsedA;

        return new Color(r, g, b, a);
    }

    private static bool TryParseByte(string s, out byte value)
    {
        if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
        {
            value = (byte)Math.Clamp(intVal, 0, 255);
            return true;
        }
        value = 0;
        return false;
    }
}

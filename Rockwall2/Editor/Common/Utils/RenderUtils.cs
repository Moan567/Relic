using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Utils;
public static class RenderUtils
{
    public static void DrawDashedPolyline(SpriteBatch sb, IReadOnlyList<Vector2> points, bool closed, Color color, float thickness, float time)
    {
        const float dash = 6f;
        const float gap = 4f;
        const float speed = 20f;
        const float period = dash + gap;

        if (points.Count < 2) return;

        float travelled = -((time * speed) % period);
        int segmentCount = closed ? points.Count : points.Count - 1;

        for (int i = 0; i < segmentCount; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];

            Vector2 dir = b - a;
            float length = dir.Length();
            if (length < 0.001f) continue;
            dir /= length;

            float t = travelled;
            while (t < length)
            {
                float segStart = MathF.Max(t, 0f);
                float segEnd = MathF.Min(t + dash, length);
                if (segEnd > segStart)
                {
                    sb.DrawLine(a + dir * segStart, a + dir * segEnd, color, thickness);
                }
                t += period;
            }

            travelled = t - length;
        }
    }
    public static void DrawDashedLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness, float time)
    {
        DrawDashedPolyline(sb, new[] { a, b }, false, color, thickness, time);
    }
    public static void DrawDashedBox(SpriteBatch sb, Vector2 min, Vector2 max, Color color, float thickness, float time)
    {
        var points = new[]
        {
            new Vector2(min.X, min.Y),
            new Vector2(max.X, min.Y),
            new Vector2(max.X, max.Y),
            new Vector2(min.X, max.Y),
        };

        DrawDashedPolyline(sb, points, true, color, thickness, time);
    }
    public static void DrawDashedCircle(SpriteBatch sb, Vector2 center, float radius, Color color, float thickness, float time, int segmentsPerDash = 4)
    {
        const float dash = 6f;
        const float gap = 4f;
        const float speed = 20f;
        const float period = dash + gap;

        float circumference = MathHelper.TwoPi * radius;
        float offset = (time * speed) % period;
        float t = -offset;

        while (t < circumference)
        {
            float dashStart = MathF.Max(t, 0f);
            float dashEnd = MathF.Min(t + dash, circumference);
            if (dashEnd > dashStart)
            {
                DrawArc(sb, center, radius, dashStart / radius, dashEnd / radius, color, thickness, segmentsPerDash);
            }
            t += period;
        }
    }

    static void DrawArc(SpriteBatch sb, Vector2 center, float radius, float angleStart, float angleEnd, Color color, float thickness, int segments)
    {
        Vector2 prev = center + new Vector2(MathF.Cos(angleStart), MathF.Sin(angleStart)) * radius;
        for (int i = 1; i <= segments; i++)
        {
            float a = angleStart + (angleEnd - angleStart) * i / segments;
            Vector2 next = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            sb.DrawLine(prev, next, color, thickness);
            prev = next;
        }
    }
}

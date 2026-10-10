using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Rockwall2.Editor.Common;

// Maps the visualizer type name baked into entMETA.gff back to an actual Type to instantiate.
public static class VisualizerRegistry
{
    static readonly Dictionary<string, Type> types = new()
    {
        { nameof(Rockwall.SphereVisualizer), typeof(Rockwall.SphereVisualizer) },
        { nameof(Rockwall.ConeVisualizer), typeof(Rockwall.ConeVisualizer) },
        { nameof(Rockwall.ArrowVisualizer), typeof(Rockwall.ArrowVisualizer) },
        { nameof(Rockwall.SpriteVisualizer), typeof(Rockwall.SpriteVisualizer) },
        { nameof(Rockwall.ModelVisualizer), typeof(Rockwall.ModelVisualizer) },
    };

    public static Rockwall.EntityVisualizer Create(string typeName) =>
        types.TryGetValue(typeName, out var t) ? (Rockwall.EntityVisualizer)Activator.CreateInstance(t) : null;
}

// LineList vertex generators for EntityVisualizer gizmos. All positions are world-space.
public static class GizmoShapes
{
    const int CircleSegments = 24;

    public static VertexPositionColor[] WireSphere(Vector3 center, float radius, Color color)
    {
        const int LongitudeLines = 4;
        const int LatitudeRings = 4;

        var verts = new List<VertexPositionColor>();

        for (int i = 0; i < LongitudeLines; i++)
        {
            float theta = i * MathHelper.Pi / LongitudeLines;
            var axisA = new Vector3(MathF.Cos(theta), 0f, MathF.Sin(theta));
            AddCircle(verts, center, radius, axisA, Vector3.Up, color);
        }

        for (int i = 1; i <= LatitudeRings; i++)
        {
            float phi = MathHelper.Pi * i / (LatitudeRings + 1) - MathHelper.PiOver2;
            float y = radius * MathF.Sin(phi);
            float ringRadius = radius * MathF.Cos(phi);
            AddCircle(verts, center + Vector3.Up * y, ringRadius, Vector3.Right, Vector3.Forward, color);
        }

        return verts.ToArray();
    }

    public static VertexPositionColor[] WireCone(Vector3 apex, Vector3 direction, float radius, float angleDegrees, Color color)
    {
        direction = direction.LengthSquared() > 0.0001f ? Vector3.Normalize(direction) : Vector3.Forward;
        var right = Vector3.Cross(direction, Vector3.Up);
        right = right.LengthSquared() > 0.0001f ? Vector3.Normalize(right) : Vector3.Right;
        var up = Vector3.Cross(right, direction);

        float angleRad = MathHelper.ToRadians(MathHelper.Clamp(angleDegrees, 0f, 180f));
        const int RadialLines = 8;
        const int LatitudeRings = 4;
        const int ArcSegments = 12;

        var verts = new List<VertexPositionColor>();

        for (int i = 1; i <= LatitudeRings; i++)
        {
            float phi = angleRad * i / LatitudeRings;
            var ringCenter = apex + direction * (radius * MathF.Cos(phi));
            float ringRadius = radius * MathF.Sin(phi);
            AddCircle(verts, ringCenter, ringRadius, right, up, color);
        }

        for (int i = 0; i < RadialLines; i++)
        {
            float theta = i * MathHelper.TwoPi / RadialLines;
            AddMeridianArc(verts, apex, direction, right, up, radius, theta, 0f, angleRad, ArcSegments, color);
        }

        float rimRadius = radius * MathF.Sin(angleRad);
        var rimCenter = apex + direction * (radius * MathF.Cos(angleRad));
        for (int i = 0; i < RadialLines; i++)
        {
            float theta = i * MathHelper.TwoPi / RadialLines;
            var rim = rimCenter + (right * MathF.Cos(theta) + up * MathF.Sin(theta)) * rimRadius;
            verts.Add(new VertexPositionColor(apex, color));
            verts.Add(new VertexPositionColor(rim, color));
        }

        return verts.ToArray();
    }

    static void AddMeridianArc(List<VertexPositionColor> verts, Vector3 apex, Vector3 axis, Vector3 right, Vector3 up, float radius, float theta, float phiStart, float phiEnd, int segments, Color color)
    {
        Vector3 Point(float phi)
        {
            var radial = right * MathF.Cos(theta) + up * MathF.Sin(theta);
            return apex + radius * (axis * MathF.Cos(phi) + radial * MathF.Sin(phi));
        }
        for (int i = 0; i < segments; i++)
        {
            float p0 = MathHelper.Lerp(phiStart, phiEnd, i / (float)segments);
            float p1 = MathHelper.Lerp(phiStart, phiEnd, (i + 1) / (float)segments);
            verts.Add(new VertexPositionColor(Point(p0), color));
            verts.Add(new VertexPositionColor(Point(p1), color));
        }
    }

    public static VertexPositionColor[] WireArrow(Vector3 origin, Vector3 direction, float length, Color color)
    {
        direction = direction.LengthSquared() > 0.0001f ? Vector3.Normalize(direction) : Vector3.Forward;
        var tip = origin + direction * length;
        var back = tip - direction * (length * 0.2f);
        var side = Vector3.Cross(direction, Vector3.Up);
        side = side.LengthSquared() > 0.0001f ? Vector3.Normalize(side) * length * 0.08f : Vector3.Right * length * 0.08f;

        return new[]
        {
            new VertexPositionColor(origin, color), new VertexPositionColor(tip, color),
            new VertexPositionColor(tip, color), new VertexPositionColor(back + side, color),
            new VertexPositionColor(tip, color), new VertexPositionColor(back - side, color),
        };
    }

    public static void AddCircle(List<VertexPositionColor> verts, Vector3 center, float radius, Vector3 axisA, Vector3 axisB, Color color)
    {
        Vector3 Point(float t) => center + (axisA * MathF.Cos(t) + axisB * MathF.Sin(t)) * radius;

        for (int i = 0; i < CircleSegments; i++)
        {
            float t0 = i / (float)CircleSegments * MathHelper.TwoPi;
            float t1 = (i + 1) / (float)CircleSegments * MathHelper.TwoPi;
            verts.Add(new VertexPositionColor(Point(t0), color));
            verts.Add(new VertexPositionColor(Point(t1), color));
        }
    }
}

using Relic.Utils;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Rockwall2.Editor.Mapper.Utils;
public static class PrimitiveGenerator
{
    const float snapTolerance = 1 / 32f;
    internal static float ScaleToleranceForSides(float baseTolerance, int sides, int baseSides = 12)
    {
        return baseTolerance * baseSides / sides;
    }
    internal static float ScaleToleranceForSize(float baseTolerance, float size, float baseSize = 4f)
    {
        float scaled = baseTolerance * size / baseSize;
        return Math.Clamp(scaled, baseTolerance * 0.125f, baseTolerance * 8f);
    }
    internal static float PickGridStep(float value, float tolerance, int maxHalvings = 8)
    {
        float step = 1f;
        for (int i = 0; i < maxHalvings; i++)
        {
            float snapped = MathF.Round(value / step) * step;
            if (MathF.Abs(snapped - value) <= tolerance)
                return step;
            step *= 0.5f;
        }
        return step;
    }
    internal static float SnapToGridStep(float value, float tolerance, int maxHalvings = 8)
    {
        float step = PickGridStep(value, tolerance, maxHalvings);
        return MathF.Round(value / step) * step;
    }

    internal static Vector3 SnapVertex(Vector3 v, float tolerance, int maxHalvings = 8)
    {
        return new Vector3(
            SnapToGridStep(v.X, tolerance, maxHalvings),
            SnapToGridStep(v.Y, tolerance, maxHalvings),
            SnapToGridStep(v.Z, tolerance, maxHalvings));
    }
    internal static float RoundToStep(float value, float step)
    {
        return MathF.Round(value / step) * step;
    }

    static Plane SnapPlaneDistance(Plane plane, float tolerance)
    {
        return new Plane(plane.Normal, SnapToGridStep(plane.D, tolerance));
    }

    public static List<Plane> Box(BoundingBox bounds)
    {
        return new List<Plane>
        {
            new Plane(Vector3.Right, -bounds.Max.X),
            new Plane(Vector3.Left, bounds.Min.X),
            new Plane(Vector3.Up, -bounds.Max.Y),
            new Plane(Vector3.Down, bounds.Min.Y),
            new Plane(Vector3.Backward, -bounds.Max.Z),
            new Plane(Vector3.Forward, bounds.Min.Z),
        };
    }

    public static List<Plane> Cylinder(BoundingBox bounds, int sides, Vector3 axis, float snapTolerance = snapTolerance)
    {
        Vector3 center = (bounds.Min + bounds.Max) * 0.5f;
        Vector3 extents = (bounds.Max - bounds.Min) * 0.5f;
        Vector3 lengthAxis, u, v;
        float radiusU, radiusV;
        Vector3 a = new Vector3(MathF.Abs(axis.X), MathF.Abs(axis.Y), MathF.Abs(axis.Z));

        if (a.X >= a.Y && a.X >= a.Z)
        {
            lengthAxis = Vector3.UnitX; u = Vector3.UnitY; v = Vector3.UnitZ;
            radiusU = extents.Y; radiusV = extents.Z;
        }
        else if (a.Y >= a.X && a.Y >= a.Z)
        {
            lengthAxis = Vector3.UnitY; u = Vector3.UnitX; v = Vector3.UnitZ;
            radiusU = extents.X; radiusV = extents.Z;
        }
        else
        {
            lengthAxis = Vector3.UnitZ; u = Vector3.UnitX; v = Vector3.UnitY;
            radiusU = extents.X; radiusV = extents.Y;
        }

        snapTolerance = ScaleToleranceForSides(ScaleToleranceForSize(snapTolerance, radiusU + radiusV), sides);

        float lengthMax = Vector3.Dot(bounds.Max, lengthAxis);
        float lengthMin = Vector3.Dot(bounds.Min, lengthAxis);

        var planes = new List<Plane>
        {
            SnapPlaneDistance(new Plane(lengthAxis, -lengthMax), snapTolerance),
            SnapPlaneDistance(new Plane(-lengthAxis, lengthMin), snapTolerance),
        };

        // Generate the base rim as real points, snap those to grid, then build
        // each side plane from a snapped edge extruded along the length axis.
        // The plane's slope now comes from actual on-grid vertices rather than
        // an analytically "clean" normal with a separately-snapped distance.
        var rim = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = MathHelper.TwoPi * i / sides;
            float cu = MathF.Cos(angle), cv = MathF.Sin(angle);
            Vector3 point = center + u * (cu * radiusU) + v * (cv * radiusV) + lengthAxis * lengthMin;
            rim[i] = SnapVertex(point, snapTolerance);
        }

        for (int i = 0; i < sides; i++)
        {
            Vector3 pA = rim[i];
            Vector3 pB = rim[(i + 1) % sides];

            if (Vector3.DistanceSquared(pA, pB) < 1e-8f) continue; // snapped onto itself, skip

            Vector3 edge = pB - pA;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(edge, lengthAxis));

            // orient outward: away from the central axis line
            Vector3 mid = (pA + pB) * 0.5f;
            Vector3 axisPoint = center + lengthAxis * Vector3.Dot(mid - center, lengthAxis);
            if (Vector3.Dot(normal, axisPoint - mid) > 0)
                normal = -normal;

            planes.Add(new Plane(normal, -Vector3.Dot(normal, pA)));
        }

        return planes;
    }
    public static List<Plane> Sphere(BoundingBox bounds, int rings, int segments, Vector3 axis, float snapTolerance = 0.03125f)
    {
        Vector3 center = (bounds.Min + bounds.Max) * 0.5f;
        Vector3 radii = (bounds.Max - bounds.Min) * 0.5f;

        snapTolerance = ScaleToleranceForSides(ScaleToleranceForSize(snapTolerance, radii.X + radii.Y + radii.Z), segments);

        const float minRadius = 1e-4f;
        if (radii.X < minRadius || radii.Y < minRadius || radii.Z < minRadius)
            return new List<Plane>();

        if (rings < 2 || segments < 3)
            return new List<Plane>();


        Vector3 poleAxis = Vector3.UnitY;

        int rowCount = rings - 1;
        if (rowCount < 1) return new List<Plane>();

        var allPoints = new List<Vector3>();

        for (int r = 1; r < rings; r++)
        {
            float phi = MathF.PI * r / rings;
            float yUnit = MathF.Cos(phi);
            float ringRadiusUnit = MathF.Sin(phi);

            float ringY = SnapToGridStep(center.Y + yUnit * radii.Y, snapTolerance);

            for (int s = 0; s < segments; s++)
            {
                float theta = MathHelper.TwoPi * s / segments;
                float rawX = center.X + ringRadiusUnit * MathF.Cos(theta) * radii.X;
                float rawZ = center.Z + ringRadiusUnit * MathF.Sin(theta) * radii.Z;

                allPoints.Add(new Vector3(
                    SnapToGridStep(rawX, snapTolerance),
                    ringY,
                    SnapToGridStep(rawZ, snapTolerance)));
            }
        }

        allPoints.Add(new Vector3(
            SnapToGridStep(center.X, snapTolerance),
            SnapToGridStep(center.Y + radii.Y, snapTolerance),
            SnapToGridStep(center.Z, snapTolerance)));

        allPoints.Add(new Vector3(
            SnapToGridStep(center.X, snapTolerance),
            SnapToGridStep(center.Y - radii.Y, snapTolerance),
            SnapToGridStep(center.Z, snapTolerance)));

        var hullFaces = ConvexHull3D.Compute(allPoints);
        var polygonal = ConvexHull3D.BuildPolygonalHull(hullFaces);

        return polygonal.Select(p => p.plane).ToList();
    }
    public static List<Plane> Cone(BoundingBox bounds, int sides, Vector3 axis, float snapTolerance = snapTolerance)
    {
        snapTolerance = ScaleToleranceForSides(snapTolerance, sides);

        Vector3 center = (bounds.Min + bounds.Max) * 0.5f;
        Vector3 extents = (bounds.Max - bounds.Min) * 0.5f;
        Vector3 lengthAxis, u, v;
        float radiusU, radiusV;
        Vector3 a = new Vector3(MathF.Abs(axis.X), MathF.Abs(axis.Y), MathF.Abs(axis.Z));

        if (a.X >= a.Y && a.X >= a.Z)
        {
            lengthAxis = Vector3.UnitX; u = Vector3.UnitY; v = Vector3.UnitZ;
            radiusU = extents.Y; radiusV = extents.Z;
        }
        else if (a.Y >= a.X && a.Y >= a.Z)
        {
            lengthAxis = Vector3.UnitY; u = Vector3.UnitX; v = Vector3.UnitZ;
            radiusU = extents.X; radiusV = extents.Z;
        }
        else
        {
            lengthAxis = Vector3.UnitZ; u = Vector3.UnitX; v = Vector3.UnitY;
            radiusU = extents.X; radiusV = extents.Y;
        }

        snapTolerance = ScaleToleranceForSides(ScaleToleranceForSize(snapTolerance, radiusU + radiusV), sides);

        float lengthMax = Vector3.Dot(bounds.Max, lengthAxis);
        float lengthMin = Vector3.Dot(bounds.Min, lengthAxis);

        Vector3 baseCenter = center - lengthAxis * ((lengthMax - lengthMin) * 0.5f);

        var planes = new List<Plane>
        {
            SnapPlaneDistance(new Plane(-lengthAxis, lengthMin), snapTolerance), // base cap
        };

        var rim = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = MathHelper.TwoPi * i / sides;
            float cu = MathF.Cos(angle), cv = MathF.Sin(angle);
            Vector3 point = baseCenter + u * (cu * radiusU) + v * (cv * radiusV);
            rim[i] = SnapVertex(point, snapTolerance);
        }

        Vector3 apex = SnapVertex(center + lengthAxis * (lengthMax - Vector3.Dot(center, lengthAxis)), snapTolerance);

        for (int i = 0; i < sides; i++)
        {
            Vector3 pA = rim[i];
            Vector3 pB = rim[(i + 1) % sides];

            if (Vector3.DistanceSquared(pA, pB) < 1e-8f) continue;

            Vector3 normal = Vector3.Normalize(Vector3.Cross(pB - pA, apex - pA));

            Vector3 mid = (pA + pB) * 0.5f;
            Vector3 axisPoint = center + lengthAxis * Vector3.Dot(mid - center, lengthAxis);
            if (Vector3.Dot(normal, axisPoint - mid) > 0)
                normal = -normal;

            planes.Add(new Plane(normal, -Vector3.Dot(normal, pA)));
        }

        return planes;
    }
}
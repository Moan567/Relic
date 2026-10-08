using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chisel.Utils;

public static class ConvexHull3D
{
    public class HullFace
    {
        public Vector3 a, b, c;
        public Vector3 normal;
        public List<Vector3> outsidePoints;
    }

    const float EPSILON = 0.0001f;
    static List<List<HullFace>> GroupCoplanarFaces(List<HullFace> faces)
    {
        var groups = new List<List<HullFace>>();

        foreach (var face in faces)
        {
            var group = groups.FirstOrDefault(g => PlanesMatch(g[0].normal, g[0].a, face.normal, face.a));
            if (group != null)
                group.Add(face);
            else
                groups.Add(new List<HullFace> { face });
        }

        return groups;
    }

    static bool PlanesMatch(Vector3 normalA, Vector3 pointA, Vector3 normalB, Vector3 pointB)
    {
        if (Vector3.Dot(normalA, normalB) < 1f - 0.0001f)
            return false;

        float distA = Vector3.Dot(normalA, pointA);
        float distB = Vector3.Dot(normalA, pointB);
        return Math.Abs(distA - distB) < 0.001f;
    }
    static List<Vector3> StitchEdgesIntoLoop(List<Edge> edges)
    {
        var loop = new List<Vector3> { edges[0].a, edges[0].b };
        var remaining = new List<Edge>(edges.Skip(1));

        while (remaining.Count > 0)
        {
            var last = loop[^1];
            int idx = remaining.FindIndex(e => e.a == last || e.b == last);
            var edge = remaining[idx];
            loop.Add(edge.a == last ? edge.b : edge.a);
            remaining.RemoveAt(idx);
        }

        loop.RemoveAt(loop.Count - 1);
        return loop;
    }
    public static List<(Plane plane, List<Vector3> verts)> BuildPolygonalHull(List<HullFace> triangles)
    {
        var result = new List<(Plane, List<Vector3>)>();

        foreach (var group in GroupCoplanarFaces(triangles))
        {
            var edges = FindHorizonEdges(group);
            var loop = StitchEdgesIntoLoop(edges);
            var plane = new Plane(group[0].normal, -Vector3.Dot(group[0].normal, group[0].a));
            result.Add((plane, loop));
        }

        return result;
    }
    public static List<HullFace> Compute(List<Vector3> points)
    {
        if (points.Count < 4)
            return new List<HullFace>();

        var faces = BuildSeedTetrahedron(points, out var remaining);

        bool progress = true;
        while (progress)
        {
            progress = false;

            foreach (var face in faces.ToList())
            {
                if (face.outsidePoints == null || face.outsidePoints.Count == 0)
                    continue;

                progress = true;

                Vector3 farthest = face.outsidePoints
                    .OrderByDescending(p => Vector3.Dot(face.normal, p - face.a))
                    .First();

                var horizonFaces = new List<HullFace>();
                var litFaces = new List<HullFace>();
                foreach (var f in faces)
                {
                    if (Vector3.Dot(f.normal, farthest - f.a) > EPSILON)
                        litFaces.Add(f);
                }

                var horizonEdges = FindHorizonEdges(litFaces);

                var orphanPoints = new List<Vector3>();
                foreach (var f in litFaces)
                {
                    if (f.outsidePoints != null)
                        orphanPoints.AddRange(f.outsidePoints);
                    faces.Remove(f);
                }
                orphanPoints.Remove(farthest);

                var newFaces = new List<HullFace>();
                foreach (var edge in horizonEdges)
                {
                    var newFace = MakeFace(edge.a, edge.b, farthest);
                    newFaces.Add(newFace);
                }

                foreach (var p in orphanPoints)
                {
                    HullFace bestFace = null;
                    float bestDist = EPSILON;
                    foreach (var nf in newFaces)
                    {
                        float d = Vector3.Dot(nf.normal, p - nf.a);
                        if (d > bestDist)
                        {
                            bestDist = d;
                            bestFace = nf;
                        }
                    }
                    if (bestFace != null)
                    {
                        var f = bestFace;
                        f.outsidePoints ??= new List<Vector3>();
                        f.outsidePoints.Add(p);
                    }
                }

                faces.AddRange(newFaces);
                break; // restart the foreach over faces since we mutated the list
            }
        }

        return faces;
    }

    static List<HullFace> BuildSeedTetrahedron(List<Vector3> points, out List<Vector3> remaining)
    {
        Vector3 minX = points[0], maxX = points[0];
        foreach (var p in points)
        {
            if (p.X < minX.X) minX = p;
            if (p.X > maxX.X) maxX = p;
        }

        Vector3 a = minX, b = maxX;

        Vector3 c = points
            .OrderByDescending(p => DistancePointToLine(p, a, b))
            .First();

        Vector3 normalGuess = Vector3.Cross(b - a, c - a);
        Vector3 d = points
            .OrderByDescending(p => float.Abs(Vector3.Dot(normalGuess, p - a)))
            .First();

        var faces = new List<HullFace>
        {
            MakeFace(a, b, c, d),
            MakeFace(a, c, d, b),
            MakeFace(a, d, b, c),
            MakeFace(b, d, c, a)
        };

        remaining = points.Where(p => p != a && p != b && p != c && p != d).ToList();

        foreach (var p in remaining)
        {
            foreach (var f in faces)
            {
                if (Vector3.Dot(f.normal, p - f.a) > EPSILON)
                {
                    var face = f;
                    face.outsidePoints ??= new List<Vector3>();
                    face.outsidePoints.Add(p);
                    break;
                }
            }
        }

        return faces;
    }

    static HullFace MakeFace(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
    {
        var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        if (Vector3.Dot(normal, inside - a) > 0)
        {
            (b, c) = (c, b);
            normal = -normal;
        }
        return new HullFace { a = a, b = b, c = c, normal = normal };
    }

    static HullFace MakeFace(Vector3 edgeA, Vector3 edgeB, Vector3 apex)
    {
        var normal = Vector3.Normalize(Vector3.Cross(edgeB - edgeA, apex - edgeA));
        return new HullFace { a = edgeA, b = edgeB, c = apex, normal = normal };
    }

    static float DistancePointToLine(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Vector3.Cross(ab, p - a);
        return t.LengthSquared() / ab.LengthSquared();
    }

    struct Edge
    {
        public Vector3 a, b;
    }

    static List<Edge> FindHorizonEdges(List<HullFace> litFaces)
    {
        var edgeCounts = new Dictionary<(Vector3, Vector3), int>();

        void CountEdge(Vector3 x, Vector3 y)
        {
            var key = (x, y);
            var reverseKey = (y, x);
            if (edgeCounts.ContainsKey(reverseKey))
                edgeCounts[reverseKey]++;
            else
                edgeCounts[key] = edgeCounts.TryGetValue(key, out var v) ? v + 1 : 1;
        }

        foreach (var f in litFaces)
        {
            CountEdge(f.a, f.b);
            CountEdge(f.b, f.c);
            CountEdge(f.c, f.a);
        }

        var horizon = new List<Edge>();
        foreach (var kv in edgeCounts)
        {
            if (kv.Value == 1)
                horizon.Add(new Edge { a = kv.Key.Item1, b = kv.Key.Item2 });
        }

        return horizon;
    }
}
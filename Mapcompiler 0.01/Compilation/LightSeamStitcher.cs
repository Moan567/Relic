using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MapCompiler;

public static class LightmapSeamStitcher
{
    private const float VertexMergeTolerance = 0.01f;
    private const float VertexMergeToleranceSq = VertexMergeTolerance * VertexMergeTolerance;
    private const float NormalThreshold = 0.9f;
    private const float CellSize = VertexMergeTolerance * 2f;

    public static void Stitch(
        Brush[] brushes,
        LightmapColor[] lmB1, LightmapColor[] lmB2, LightmapColor[] lmB3,
        int lightmapResolution)
    {
        var edges = new List<(Vector3 a, Vector3 b, int brush, int face)>();

        for (int bi = 0; bi < brushes.Length; bi++)
        {
            var brush = brushes[bi];
            for (int fi = 0; fi < brush.Faces.Length; fi++)
            {
                if (!brush.Faces[fi].Drawn) continue;
                var idx = brush.Faces[fi].Indices;
                var seen = new HashSet<(int, int)>();
                for (int t = 0; t < idx.Length; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int i0 = idx[t + e], i1 = idx[t + (e + 1) % 3];
                        int lo = Math.Min(i0, i1), hi = Math.Max(i0, i1);
                        if (!seen.Add((lo, hi))) continue;
                        edges.Add((
                            brush.Vertices[i0] + brush.Position,
                            brush.Vertices[i1] + brush.Position,
                            bi, fi));
                    }
                }
            }
        }

        var buckets = new Dictionary<(int x, int y, int z), List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            var key = CellOf((edges[i].a + edges[i].b) * 0.5f);
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<int>();
            list.Add(i);
        }

        var pairs = new List<(int eA, int eB, bool parallel)>();
        var seenPairs = new HashSet<long>();

        for (int i = 0; i < edges.Count; i++)
        {
            var baseCell = CellOf((edges[i].a + edges[i].b) * 0.5f);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var cell = (baseCell.x + dx, baseCell.y + dy, baseCell.z + dz);
                        if (!buckets.TryGetValue(cell, out var candidates)) continue;

                        foreach (int j in candidates)
                        {
                            if (j <= i) continue;

                            long pairKey = ((long)i << 32) | (uint)j;
                            if (!seenPairs.Add(pairKey)) continue;

                            if (!EdgesMatch(edges[i].a, edges[i].b, edges[j].a, edges[j].b)) continue;

                            Vector3 nA = brushes[edges[i].brush].Faces[edges[i].face].Normal;
                            Vector3 nB = brushes[edges[j].brush].Faces[edges[j].face].Normal;
                            if (Vector3.Dot(nA, nB) >= NormalThreshold)
                                pairs.Add((i, j, EdgesParallel(edges[i].a, edges[i].b, edges[j].a, edges[j].b)));
                        }
                    }
                }
            }
        }

        if (pairs.Count == 0) return;

        var accumB1 = new (float r, float g, float b, int count)[lmB1.Length];
        var accumB2 = new (float r, float g, float b, int count)[lmB2.Length];
        var accumB3 = new (float r, float g, float b, int count)[lmB3.Length];

        foreach (var (eA, eB, parallel) in pairs)
        {
            var (wA0, wA1, brushA, faceA) = edges[eA];
            var (wB0, wB1, brushB, faceB) = edges[eB];

            float edgeLen = Vector3.Distance(wA0, wA1);
            int sampleCount = Math.Max(2, (int)MathF.Ceiling(edgeLen * lightmapResolution * 2));

            for (int s = 0; s <= sampleCount; s++)
            {
                float t = (float)s / sampleCount;

                Vector3 worldPt = Vector3.Lerp(wA0, wA1, t);
                Vector2 uvA = BarycentricLightmapUV(worldPt, brushes[brushA], faceA);

                Vector3 worldPtB = Vector3.Lerp(wB0, wB1, parallel ? t : 1f - t);
                Vector2 uvB = BarycentricLightmapUV(worldPtB, brushes[brushB], faceB);

                int xA = (int)MathF.Round(uvA.X * lightmapResolution);
                int yA = (int)MathF.Round(uvA.Y * lightmapResolution);
                int xB = (int)MathF.Round(uvB.X * lightmapResolution);
                int yB = (int)MathF.Round(uvB.Y * lightmapResolution);

                if (!InBounds(xA, yA, lightmapResolution)) continue;
                if (!InBounds(xB, yB, lightmapResolution)) continue;

                int idxA = yA * lightmapResolution + xA;
                int idxB = yB * lightmapResolution + xB;

                AccumAdd(ref accumB1[idxA], lmB1[idxA]);
                AccumAdd(ref accumB1[idxA], lmB1[idxB]);
                AccumAdd(ref accumB1[idxB], lmB1[idxA]);
                AccumAdd(ref accumB1[idxB], lmB1[idxB]);

                AccumAdd(ref accumB2[idxA], lmB2[idxA]);
                AccumAdd(ref accumB2[idxA], lmB2[idxB]);
                AccumAdd(ref accumB2[idxB], lmB2[idxA]);
                AccumAdd(ref accumB2[idxB], lmB2[idxB]);

                AccumAdd(ref accumB3[idxA], lmB3[idxA]);
                AccumAdd(ref accumB3[idxA], lmB3[idxB]);
                AccumAdd(ref accumB3[idxB], lmB3[idxA]);
                AccumAdd(ref accumB3[idxB], lmB3[idxB]);
            }
        }

        for (int i = 0; i < lmB1.Length; i++)
        {
            if (accumB1[i].count == 0) continue;

            lmB1[i] = AccumToColor(accumB1[i]);
            lmB2[i] = AccumToColor(accumB2[i]);
            lmB3[i] = AccumToColor(accumB3[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int x, int y, int z) CellOf(Vector3 p) => (
        (int)MathF.Floor(p.X / CellSize),
        (int)MathF.Floor(p.Y / CellSize),
        (int)MathF.Floor(p.Z / CellSize));

    private static Vector2 BarycentricLightmapUV(Vector3 worldPt, Brush brush, int faceIdx)
    {
        var face = brush.Faces[faceIdx];
        var idx = face.Indices;
        var verts = brush.Vertices;
        var lmUvs = brush.LightmapUVs;
        Vector3 local = worldPt - brush.Position;

        float bestPenalty = float.MaxValue;
        Vector2 best = Vector2.Zero;

        for (int t = 0; t < idx.Length; t += 3)
        {
            Vector3 v0 = verts[idx[t]],
                    v1 = verts[idx[t + 1]],
                    v2 = verts[idx[t + 2]];

            Barycentric(local, v0, v1, v2, out float u, out float v, out float w);

            if (u >= -1e-4f && v >= -1e-4f && w >= -1e-4f)
                return lmUvs[idx[t]] * u
                     + lmUvs[idx[t + 1]] * v
                     + lmUvs[idx[t + 2]] * w;

            float penalty = MathF.Max(0, -u) + MathF.Max(0, -v) + MathF.Max(0, -w);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                float cu = MathF.Max(0, u), cv = MathF.Max(0, v), cw = MathF.Max(0, w);
                float sum = cu + cv + cw;
                if (sum > 1e-6f) { cu /= sum; cv /= sum; cw /= sum; }
                best = lmUvs[idx[t]] * cu
                     + lmUvs[idx[t + 1]] * cv
                     + lmUvs[idx[t + 2]] * cw;
            }
        }

        return best;
    }

    private static void Barycentric(Vector3 p,
        Vector3 a, Vector3 b, Vector3 c,
        out float u, out float v, out float w)
    {
        Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = Vector3.Dot(v0, v0);
        float d01 = Vector3.Dot(v0, v1);
        float d11 = Vector3.Dot(v1, v1);
        float d20 = Vector3.Dot(v2, v0);
        float d21 = Vector3.Dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;
        if (MathF.Abs(denom) < 1e-8f) { u = v = w = 1f / 3f; return; }
        v = (d11 * d20 - d01 * d21) / denom;
        w = (d00 * d21 - d01 * d20) / denom;
        u = 1f - v - w;
    }

    private static bool EdgesMatch(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
    {
        return (VertsMatch(a0, b0) && VertsMatch(a1, b1))
            || (VertsMatch(a0, b1) && VertsMatch(a1, b0));
    }

    private static bool EdgesParallel(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        => VertsMatch(a0, b0) && VertsMatch(a1, b1);

    private static bool VertsMatch(Vector3 a, Vector3 b)
        => (a - b).LengthSquared() < VertexMergeToleranceSq;

    private static bool InBounds(int x, int y, int res)
        => x >= 0 && x < res && y >= 0 && y < res;

    private static void AccumAdd(
        ref (float r, float g, float b, int count) accum,
        LightmapColor c)
    {
        accum.r += c.R;
        accum.g += c.G;
        accum.b += c.B;
        accum.count++;
    }

    private static LightmapColor AccumToColor(
        (float r, float g, float b, int count) accum)
    {
        float inv = 1f / (accum.count * 255f);
        return new LightmapColor(accum.r * inv, accum.g * inv, accum.b * inv);
    }
}
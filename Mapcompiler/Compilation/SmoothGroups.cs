using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MapCompiler.Compilation;

public struct SmoothedVertexData
{
    public Vector3 Normal, Tangent, Binormal, Basis1, Basis2, Basis3;
    public Vector3 Position;
}

internal class SmoothGroups
{
    static readonly Vector3 B1 = new(MathF.Sqrt(2f / 3f), 0f, 1f / MathF.Sqrt(3f));
    static readonly Vector3 B2 = new(-1f / MathF.Sqrt(6f), 1f / MathF.Sqrt(2f), 1f / MathF.Sqrt(3f));
    static readonly Vector3 B3 = new(-1f / MathF.Sqrt(6f), -1f / MathF.Sqrt(2f), 1f / MathF.Sqrt(3f));

    public static Dictionary<(int brush, int face, int vertex), SmoothedVertexData> Compute(
        Brush[] brushes, float smoothAngleDegrees = 45f, float positionTolerance = 1 / 512f)
    {
        float cosThreshold = MathF.Cos(MathHelper.ToRadians(smoothAngleDegrees));
        var result = new Dictionary<(int, int, int), SmoothedVertexData>();

        var refs = new List<(int brush, int face, int vert, Vector3 worldPos,
                              Vector3 normal, Vector3 tangent, Vector3 binormal, int group)>();

        for (int b = 0; b < brushes.Length; b++)
        {
            for (int f = 0; f < brushes[b].Faces.Length; f++)
            {
                var face = brushes[b].Faces[f];
                if (face.smoothGroup == 0 || face.Indices == null) continue;

                foreach (var vertIdx in face.Indices.Distinct())
                {
                    Vector3 worldPos = brushes[b].Vertices[vertIdx] + brushes[b].Position;
                    refs.Add((b, f, vertIdx, worldPos, face.Normal, face.Tangent, face.Binormal, face.smoothGroup));
                }
            }
        }

        float cell = positionTolerance;
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) KeyFor(Vector3 p) => ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));

        for (int i = 0; i < refs.Count; i++)
        {
            var key = KeyFor(refs[i].worldPos);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(i);
        }

        var parent = new int[refs.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

        float tolSq = positionTolerance * positionTolerance;

        for (int i = 0; i < refs.Count; i++)
        {
            var key = KeyFor(refs[i].worldPos);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var nk = (key.Item1 + dx, key.Item2 + dy, key.Item3 + dz);
                        if (!grid.TryGetValue(nk, out var list)) continue;
                        foreach (var j in list)
                        {
                            if (j <= i) continue;
                            if (Vector3.DistanceSquared(refs[i].worldPos, refs[j].worldPos) <= tolSq)
                                Union(i, j);
                        }
                    }
                }
            }
        }

        var clusters = new Dictionary<int, List<int>>();
        for (int i = 0; i < refs.Count; i++)
        {
            int root = Find(i);
            if (!clusters.TryGetValue(root, out var list)) clusters[root] = list = new List<int>();
            list.Add(i);
        }

        foreach (var (_, members) in clusters)
        {
            foreach (var i in members)
            {
                var (b, f, vertIdx, _, myNormal, myTangent, myBinormal, myGroup) = refs[i];

                Vector3 accumNormal = Vector3.Zero;
                Vector3 accumTangent = Vector3.Zero;
                int count = 0;

                foreach (var j in members)
                {
                    var (_, _, _, _, otherNormal, otherTangent, _, otherGroup) = refs[j];
                    if ((myGroup & otherGroup) == 0) continue;
                    if (Vector3.Dot(myNormal, otherNormal) < cosThreshold) continue;

                    accumNormal += otherNormal;
                    accumTangent += otherTangent;
                    count++;
                }

                Vector3 smoothNormal = count > 0 ? Vector3.Normalize(accumNormal) : myNormal;

                Vector3 smoothTangent = Vector3.Normalize(myTangent - smoothNormal * Vector3.Dot(myTangent, smoothNormal));

                float handedness = MathF.Sign(Vector3.Dot(Vector3.Cross(myNormal, myTangent), myBinormal));
                if (handedness == 0f) handedness = 1f;

                Vector3 smoothBinormal = Vector3.Cross(smoothNormal, smoothTangent) * handedness;

                var tbn = new Matrix(
                    smoothTangent.X, smoothTangent.Y, smoothTangent.Z, 0f,
                    smoothBinormal.X, smoothBinormal.Y, smoothBinormal.Z, 0f,
                    smoothNormal.X, smoothNormal.Y, smoothNormal.Z, 0f,
                    0f, 0f, 0f, 1f);

                result[(b, f, vertIdx)] = new SmoothedVertexData
                {
                    Normal = smoothNormal,
                    Tangent = smoothTangent,
                    Binormal = smoothBinormal,
                    Basis1 = Vector3.TransformNormal(B1, tbn),
                    Basis2 = Vector3.TransformNormal(B2, tbn),
                    Basis3 = Vector3.TransformNormal(B3, tbn),
                    Position = refs[i].worldPos,
                };
            }
        }

        return result;
    }
    public static int[] BuildFaceLoop(Brush brush, int faceIndex)
    {
        var face = brush.Faces[faceIndex];
        if (face.Indices == null || face.Indices.Length == 0) return Array.Empty<int>();
        return face.Indices.Distinct().OrderBy(x => x).ToArray();
    }

    public static SmoothedVertexData SampleAt(
        Brush brush, int brushIndex, int faceIndex, Vector3 worldPos,
        Dictionary<(int, int, int), SmoothedVertexData> smoothed)
        => SampleAt(brush, brushIndex, faceIndex, worldPos, smoothed, BuildFaceLoop(brush, faceIndex));

    public static SmoothedVertexData SampleAt(
        Brush brush, int brushIndex, int faceIndex, Vector3 worldPos,
        Dictionary<(int, int, int), SmoothedVertexData> smoothed,
        int[] precomputedLoop)
    {
        var face = brush.Faces[faceIndex];

        SmoothedVertexData flat = new()
        {
            Normal = face.Normal,
            Tangent = face.Tangent,
            Binormal = face.Binormal,
            Basis1 = face.Basis1,
            Basis2 = face.Basis2,
            Basis3 = face.Basis3,
            Position = worldPos,
        };

        if (face.smoothGroup == 0 || precomputedLoop == null || precomputedLoop.Length == 0)
            return flat;

        var loopIdx = precomputedLoop;
        int n = loopIdx.Length;
        if (n < 3) return flat;

        var data = new SmoothedVertexData[n];
        var pos = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            data[i] = smoothed.TryGetValue((brushIndex, faceIndex, loopIdx[i]), out var sd) ? sd : flat;
            pos[i] = brush.Vertices[loopIdx[i]] + brush.Position;
        }

        Vector3 normal = Vector3.Normalize(face.Normal);
        var d = new Vector3[n];
        var r = new float[n];
        const float eps = 1e-5f;

        for (int i = 0; i < n; i++)
        {
            d[i] = pos[i] - worldPos;
            r[i] = d[i].Length();
            if (r[i] < eps)
                return data[i];
        }

        var tanHalf = new float[n];
        for (int i = 0; i < n; i++)
        {
            int ip1 = (i + 1) % n;

            float A = Vector3.Dot(Vector3.Cross(d[i], d[ip1]), normal);
            float D = Vector3.Dot(d[i], d[ip1]);

            float relEps = eps * r[i] * r[ip1];

            if (MathF.Abs(A) <= relEps)
            {
                if (D < 0f)
                {
                    float t = r[i] / (r[i] + r[ip1]);
                    return Lerp(data[i], data[ip1], t);
                }

                tanHalf[i] = 0f;
                continue;
            }

            tanHalf[i] = (r[i] * r[ip1] - D) / A;
        }

        Vector3 accumNormal = Vector3.Zero, accumTangent = Vector3.Zero, accumBinormal = Vector3.Zero;
        Vector3 accumB1 = Vector3.Zero, accumB2 = Vector3.Zero, accumB3 = Vector3.Zero;
        float weightSum = 0f;

        for (int i = 0; i < n; i++)
        {
            int im1 = (i - 1 + n) % n;
            float w = (tanHalf[im1] + tanHalf[i]) / r[i];

            accumNormal += data[i].Normal * w;
            accumTangent += data[i].Tangent * w;
            accumBinormal += data[i].Binormal * w;
            accumB1 += data[i].Basis1 * w;
            accumB2 += data[i].Basis2 * w;
            accumB3 += data[i].Basis3 * w;
            weightSum += w;
        }

        if (MathF.Abs(weightSum) < eps) return flat;

        return new SmoothedVertexData
        {
            Normal = Vector3.Normalize(accumNormal / weightSum),
            Tangent = Vector3.Normalize(accumTangent / weightSum),
            Binormal = Vector3.Normalize(accumBinormal / weightSum),
            Basis1 = Vector3.Normalize(accumB1 / weightSum),
            Basis2 = Vector3.Normalize(accumB2 / weightSum),
            Basis3 = Vector3.Normalize(accumB3 / weightSum),
            Position = worldPos,
        };
    }

    static SmoothedVertexData Lerp(SmoothedVertexData a, SmoothedVertexData b, float t)
    {
        return new SmoothedVertexData
        {
            Normal = Vector3.Normalize(Vector3.Lerp(a.Normal, b.Normal, t)),
            Tangent = Vector3.Normalize(Vector3.Lerp(a.Tangent, b.Tangent, t)),
            Binormal = Vector3.Normalize(Vector3.Lerp(a.Binormal, b.Binormal, t)),
            Basis1 = Vector3.Normalize(Vector3.Lerp(a.Basis1, b.Basis1, t)),
            Basis2 = Vector3.Normalize(Vector3.Lerp(a.Basis2, b.Basis2, t)),
            Basis3 = Vector3.Normalize(Vector3.Lerp(a.Basis3, b.Basis3, t)),
            Position = Vector3.Lerp(a.Position, b.Position, t),
        };
    }
}
using MapCompiler.Compilation;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MapCompiler
{
    public static class GeometryUtils
    {
        public const int HemisphereCount = 256;
        public static readonly float[] HaltonU = new float[HemisphereCount];
        public static readonly float[] HaltonV = new float[HemisphereCount];


        public sealed class FaceUvGrid
        {
            private readonly Dictionary<(int, int), List<int>> cells = new();
            private readonly float invCellSize;

            public FaceUvGrid(Vector2[] uvs, int[] tris, float cellSize)
            {
                invCellSize = 1f / cellSize;

                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector2 u0 = uvs[tris[i]], u1 = uvs[tris[i + 1]], u2 = uvs[tris[i + 2]];
                    Vector2 umin = Vector2.Min(u0, Vector2.Min(u1, u2));
                    Vector2 umax = Vector2.Max(u0, Vector2.Max(u1, u2));

                    var cmin = CellOf(umin);
                    var cmax = CellOf(umax);

                    for (int cx = cmin.Item1; cx <= cmax.Item1; cx++)
                        for (int cy = cmin.Item2; cy <= cmax.Item2; cy++)
                        {
                            var key = (cx, cy);
                            if (!cells.TryGetValue(key, out var list))
                                cells[key] = list = new List<int>();
                            list.Add(i);
                        }
                }
            }

            private (int, int) CellOf(Vector2 uv) =>
                ((int)MathF.Floor(uv.X * invCellSize), (int)MathF.Floor(uv.Y * invCellSize));

            public List<int> QueryCandidates(Vector2 uv) =>
                cells.TryGetValue(CellOf(uv), out var list) ? list : null;
        }

        static GeometryUtils()
        {
            for (int i = 0; i < HemisphereCount; i++)
            {
                HaltonU[i] = Halton(i + 1, 2);
                HaltonV[i] = Halton(i + 1, 3);
            }
        }
        public static Vector2 ClosestPointOnSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = MathF.Max(Vector2.Dot(ab, ab), 1e-12f);
            float t = Clamp01(Vector2.Dot(p - a, ab) / denom);
            return a + ab * t;
        }

        public static bool PointInPolygonEdges(Vector2 p, (Vector2 a, Vector2 b)[] edges)
        {
            bool inside = false;

            foreach (var (a, b) in edges)
            {
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float t = (p.Y - a.Y) / (b.Y - a.Y);
                    float xCross = a.X + t * (b.X - a.X);
                    if (p.X < xCross)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        public static Vector2 ClampUvToFacePolygon(Vector2 uv, (Vector2 a, Vector2 b)[] insetBoundaryEdges)
        {
            if (insetBoundaryEdges.Length == 0 || PointInPolygonEdges(uv, insetBoundaryEdges))
                return uv;

            Vector2 best = insetBoundaryEdges[0].a;
            float bestDistSq = float.MaxValue;

            foreach (var (a, b) in insetBoundaryEdges)
            {
                Vector2 candidate = ClosestPointOnSegment2D(uv, a, b);
                float distSq = Vector2.DistanceSquared(uv, candidate);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = candidate;
                }
            }

            return best;
        }

        public static Vector2[] BuildInsetPolygon(Vector2[] loopUvs, float inwardPush)
        {
            int n = loopUvs.Length;
            if (n < 3 || inwardPush <= 0f) return loopUvs;

            Vector2 centroid = Vector2.Zero;
            foreach (var p in loopUvs) centroid += p;
            centroid /= n;

            var offsetPoint = new Vector2[n];
            var offsetDir = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                Vector2 a = loopUvs[i];
                Vector2 b = loopUvs[(i + 1) % n];
                Vector2 dir = b - a;
                dir = dir.LengthSquared() < 1e-12f ? Vector2.UnitX : Vector2.Normalize(dir);

                Vector2 normal = new Vector2(-dir.Y, dir.X);
                if (Vector2.Dot(centroid - a, normal) < 0f) normal = -normal;

                offsetPoint[i] = a + normal * inwardPush;
                offsetDir[i] = dir;
            }

            var result = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;

                if (!LineIntersect(offsetPoint[prev], offsetDir[prev], offsetPoint[i], offsetDir[i], out Vector2 corner))
                {
                    corner = loopUvs[i];
                }

                result[i] = corner;
            }

            return result;
        }

        private static bool LineIntersect(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 result)
        {
            float denom = d1.X * d2.Y - d1.Y * d2.X;
            if (MathF.Abs(denom) < 1e-9f)
            {
                result = Vector2.Zero;
                return false;
            }

            Vector2 diff = p2 - p1;
            float t = (diff.X * d2.Y - diff.Y * d2.X) / denom;
            result = p1 + d1 * t;
            return true;
        }

        public static (Vector2 a, Vector2 b)[] PolygonToEdges(Vector2[] polygon)
        {
            int n = polygon.Length;
            var edges = new (Vector2, Vector2)[n];
            for (int i = 0; i < n; i++)
            {
                edges[i] = (polygon[i], polygon[(i + 1) % n]);
            }
            return edges;
        }
        public static int[] GetFaceUvBoundaryLoop(Brush brush, int faceIdx)
        {
            var indices = brush.Faces[faceIdx].Indices;
            var edgeCounts = new Dictionary<(int, int), int>();

            void AddDirectedEdge(int a, int b)
            {
                var key = (a, b);
                edgeCounts[key] = edgeCounts.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                AddDirectedEdge(a, b);
                AddDirectedEdge(b, c);
                AddDirectedEdge(c, a);
            }

            var next = new Dictionary<int, int>();
            foreach (var edge in edgeCounts.Keys)
            {
                if (!edgeCounts.ContainsKey((edge.Item2, edge.Item1)))
                {
                    next[edge.Item1] = edge.Item2;
                }
            }

            if (next.Count == 0) return Array.Empty<int>();

            var loop = new List<int>();
            var visited = new HashSet<int>();
            int start = next.Keys.First();
            int current = start;

            do
            {
                loop.Add(current);
                visited.Add(current);
                if (!next.TryGetValue(current, out current)) break;
            }
            while (current != start && !visited.Contains(current));

            return loop.ToArray();
        }
        public static Vector3 LightmapUvTo3D(Vector2 uv, Brush brush, int face, out bool inside)
        {
            var f = brush.Faces[face];
            int[] tris = f.Indices;
            Vector2[] uvs = brush.LightmapUVs;
            Vector3[] verts = brush.Vertices;
            inside = false;

            float bestPenalty = float.MaxValue;
            Vector3 bestWorldPos = verts[tris[0]] + brush.Position;
            Vector2 bestUVOnTri = uvs[tris[0]];

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector2 u0 = uvs[tris[i]],
                        u1 = uvs[tris[i + 1]],
                        u2 = uvs[tris[i + 2]];

                float area = TriArea2D(u0, u1, u2);
                if (MathF.Abs(area) < 1e-10f) continue;

                float b0 = TriArea2D(u1, u2, uv) / area;
                float b1 = TriArea2D(u2, u0, uv) / area;
                float b2 = TriArea2D(u0, u1, uv) / area;

                if (b0 >= -1e-4f && b1 >= -1e-4f && b2 >= -1e-4f)
                {
                    inside = true;
                    return b0 * verts[tris[i]]
                         + b1 * verts[tris[i + 1]]
                         + b2 * verts[tris[i + 2]]
                         + brush.Position;
                }

                float penalty = MathF.Max(0, -b0) + MathF.Max(0, -b1) + MathF.Max(0, -b2);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;

                    float cb0 = MathF.Max(0, b0);
                    float cb1 = MathF.Max(0, b1);
                    float cb2 = MathF.Max(0, b2);
                    float sum = cb0 + cb1 + cb2;
                    if (sum > 1e-8f) { cb0 /= sum; cb1 /= sum; cb2 /= sum; }

                    bestWorldPos = cb0 * verts[tris[i]]
                                 + cb1 * verts[tris[i + 1]]
                                 + cb2 * verts[tris[i + 2]]
                                 + brush.Position;

                    bestUVOnTri = cb0 * u0 + cb1 * u1 + cb2 * u2;
                }
            }

            Vector2 uvDelta = uv - bestUVOnTri;

            float luxelScale = MathF.Max(f.LuxelScale, 0.01f);

            Vector3 worldDelta = (f.Tangent * uvDelta.X
                                + f.Binormal * uvDelta.Y) / luxelScale;

            return bestWorldPos + worldDelta;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryTriangle(Vector2 uv, int[] tris, Vector2[] uvs, Vector3[] verts, Vector3 posOffset, int i, out Vector3 hitPos)
        {
            Vector2 u0 = uvs[tris[i]], u1 = uvs[tris[i + 1]], u2 = uvs[tris[i + 2]];

            float area = TriArea2D(u0, u1, u2);
            if (MathF.Abs(area) < 1e-10f) { hitPos = default; return false; }

            float b0 = TriArea2D(u1, u2, uv) / area;
            float b1 = TriArea2D(u2, u0, uv) / area;
            float b2 = TriArea2D(u0, u1, uv) / area;

            if (b0 >= -1e-4f && b1 >= -1e-4f && b2 >= -1e-4f)
            {
                hitPos = b0 * verts[tris[i]] + b1 * verts[tris[i + 1]] + b2 * verts[tris[i + 2]] + posOffset;
                return true;
            }

            hitPos = default;
            return false;
        }

        public static Vector3 LightmapUvTo3D(Vector2 uv, Brush brush, int face, FaceUvGrid grid, out bool inside)
        {
            var candidates = grid.QueryCandidates(uv);
            if (candidates != null)
            {
                var tris = brush.Faces[face].Indices;
                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    if (TryTriangle(uv, tris, brush.LightmapUVs, brush.Vertices, brush.Position, candidates[ci], out Vector3 hitPos))
                    {
                        inside = true;
                        return hitPos;
                    }
                }
            }

            return LightmapUvTo3D(uv, brush, face, out inside);
        }

        public static Vector3 LightmapUvTo3D(Vector2 uv, Vector3[] verts, Vector2[] uvs, int[] tris, FaceUvGrid grid, out bool inside)
        {
            var candidates = grid.QueryCandidates(uv);
            if (candidates != null)
            {
                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    if (TryTriangle(uv, tris, uvs, verts, Vector3.Zero, candidates[ci], out Vector3 hitPos))
                    {
                        inside = true;
                        return hitPos;
                    }
                }
            }

            return LightmapUvTo3D(uv, verts, uvs, tris, out inside);
        }
        public static Vector3 LightmapUvTo3D(Vector2 uv, Vector3[] verts, Vector2[] uvs, int[] tris, out bool inside)
        {
            inside = false;

            float bestPenalty = float.MaxValue;
            Vector3 bestWorldPos = verts[tris[0]];
            Vector2 bestUVOnTri = uvs[tris[0]];

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector2 u0 = uvs[tris[i]],
                        u1 = uvs[tris[i + 1]],
                        u2 = uvs[tris[i + 2]];

                float area = TriArea2D(u0, u1, u2);
                if (MathF.Abs(area) < 1e-10f) continue;

                float b0 = TriArea2D(u1, u2, uv) / area;
                float b1 = TriArea2D(u2, u0, uv) / area;
                float b2 = TriArea2D(u0, u1, uv) / area;

                if (b0 >= -1e-4f && b1 >= -1e-4f && b2 >= -1e-4f)
                {
                    inside = true;
                    return b0 * verts[tris[i]]
                         + b1 * verts[tris[i + 1]]
                         + b2 * verts[tris[i + 2]];
                }

                float penalty = MathF.Max(0, -b0) + MathF.Max(0, -b1) + MathF.Max(0, -b2);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;

                    float cb0 = MathF.Max(0, b0);
                    float cb1 = MathF.Max(0, b1);
                    float cb2 = MathF.Max(0, b2);
                    float sum = cb0 + cb1 + cb2;
                    if (sum > 1e-8f) { cb0 /= sum; cb1 /= sum; cb2 /= sum; }

                    bestWorldPos = cb0 * verts[tris[i]]
                                 + cb1 * verts[tris[i + 1]]
                                 + cb2 * verts[tris[i + 2]];

                    bestUVOnTri = cb0 * u0 + cb1 * u1 + cb2 * u2;
                }
            }
            return bestWorldPos;
        }

        public static Vector2 Vector3ToUv(Vector3 point, Brush brush, int faceIdx)
        {
            var face = brush.Faces[faceIdx];

            for (int i = 0; i < face.Indices.Length; i += 3)
            {
                Vector3 p1 = brush.Vertices[face.Indices[i]]     + brush.Position;
                Vector3 p2 = brush.Vertices[face.Indices[i + 1]] + brush.Position;
                Vector3 p3 = brush.Vertices[face.Indices[i + 2]] + brush.Position;

                float area  = Vector3.Cross(p2 - p1, p3 - p1).Length();
                float area1 = Vector3.Cross(p2 - point, p3 - point).Length() / area;
                float area2 = Vector3.Cross(p3 - point, p1 - point).Length() / area;
                float area3 = Vector3.Cross(p1 - point, p2 - point).Length() / area;

                if (area1 >= 0 && area2 >= 0 && area3 >= 0)
                    return brush.UVs[face.Indices[i]]     * area1
                         + brush.UVs[face.Indices[i + 1]] * area2
                         + brush.UVs[face.Indices[i + 2]] * area3;
            }

            return Vector2.Zero;
        }

        public static Vector3? EscapeSolid(
            Vector3 point,
            Vector3 faceNormal,
            Vector3 faceTangent,
            Vector3 faceBinormal,
            float maxEscapeDist = 0.1f,
            bool allowInPlaneFallback = true)
        {
            if (!BSPRoot.Nodes[BSPRoot.Traverse(point)].solid)
                return point;

            const int NormalSteps = 16;
            float stepSize = maxEscapeDist / NormalSteps;

            for (int s = 1; s <= NormalSteps; s++)
            {
                Vector3 candidate = point + faceNormal * (stepSize * s);
                if (!BSPRoot.Nodes[BSPRoot.Traverse(candidate)].solid)
                    return candidate;
            }

            if (!allowInPlaneFallback)
                return null;

            Vector3[] inPlane = { faceTangent, -faceTangent, faceBinormal, -faceBinormal };
            float[] distances = { 0.05f, 0.1f, 0.25f, 0.5f, 1.0f };

            foreach (float dist in distances)
            {
                foreach (Vector3 dir in inPlane)
                {
                    Vector3 candidate = point + dir * dist;
                    if (!BSPRoot.Nodes[BSPRoot.Traverse(candidate)].solid)
                        return candidate;
                }
            }

            return null;
        }

        private static readonly Vector3[] DetailEnclosureTestDirs = BuildEnclosureTestDirs();

        private static Vector3[] BuildEnclosureTestDirs()
        {
            var dirs = new Vector3[5];
            dirs[0] = Vector3.Normalize(new Vector3(1f, 1f, 1f));
            dirs[1] = Vector3.Normalize(new Vector3(-1f, 1f, -1f));
            dirs[2] = Vector3.Normalize(new Vector3(1f, -1f, -1f));
            dirs[3] = Vector3.Normalize(new Vector3(-1f, -1f, 1f));
            dirs[4] = Vector3.UnitY;
            return dirs;
        }
        public static bool IsSolidForResolve(Vector3 point)
        {
            return BSPRoot.Nodes[BSPRoot.Traverse(point)].solid
                || IsPointEnclosedByDetail(point);
        }

        private static bool IsPointEnclosedByDetail(Vector3 point)
        {
            int enclosedVotes = 0;

            foreach (var testDir in DetailEnclosureTestDirs)
            {
                int hitCount = 0;
                Vector3 origin = point;
                const float maxDist = 8192f;
                const float epsilon = 0.001f;

                for (int i = 0; i < 64; i++)
                {
                    bool hit = TriangleOccluder.TraceRay(new Ray(origin, testDir), maxDist, out Vector3 hitPos);
                    if (!hit) break;

                    hitCount++;
                    origin = hitPos + testDir * epsilon;
                }

                if ((hitCount % 2) == 1)
                {
                    enclosedVotes++;
                }
            }

            return enclosedVotes * 2 > DetailEnclosureTestDirs.Length;
        }

        public static Vector3 EscapeSolidForResolve(
            Vector3 point,
            Vector3 faceNormal,
            Vector3 faceTangent,
            Vector3 faceBinormal,
            float maxEscapeDist = 0.1f,
            float safetyMargin = 0.05f)
        {
            Vector3? found = TryEscapeAlongNormal(point, faceNormal, maxEscapeDist, safetyMargin);
            if (found != null) return found.Value;

            found = TryEscapeInPlane(point, faceNormal, faceTangent, faceBinormal, maxEscapeDist, safetyMargin);
            if (found != null) return found.Value;

            return point + faceNormal * safetyMargin;
        }

        private static Vector3? TryEscapeAlongNormal(Vector3 point, Vector3 faceNormal, float maxEscapeDist, float safetyMargin)
        {
            const int NormalSteps = 24;
            float stepSize = maxEscapeDist / NormalSteps;

            Vector3? firstClear = null;

            for (int s = 0; s <= NormalSteps; s++)
            {
                Vector3 candidate = point + faceNormal * (stepSize * s);
                if (!IsSolidForResolve(candidate))
                {
                    firstClear = candidate;
                    break;
                }
            }

            if (firstClear == null)
                return null;

            float[] margins = { safetyMargin, safetyMargin * 0.5f, safetyMargin * 0.25f };
            foreach (var margin in margins)
            {
                Vector3 pushed = firstClear.Value + faceNormal * margin;
                if (!IsSolidForResolve(pushed))
                {
                    return pushed;
                }
            }

            return firstClear;
        }

        private static Vector3? TryEscapeInPlane(Vector3 point, Vector3 faceNormal, Vector3 faceTangent, Vector3 faceBinormal, float maxEscapeDist, float safetyMargin)
        {
            Vector3[] dirs =
            {
                faceTangent, -faceTangent, faceBinormal, -faceBinormal,
                Vector3.Normalize(faceTangent + faceBinormal),
                Vector3.Normalize(faceTangent - faceBinormal),
                Vector3.Normalize(-faceTangent + faceBinormal),
                Vector3.Normalize(-faceTangent - faceBinormal),
            };

            float[] distances = { maxEscapeDist, maxEscapeDist * 2f, maxEscapeDist * 4f, maxEscapeDist * 8f };

            foreach (var dist in distances)
            {
                foreach (var dir in dirs)
                {
                    Vector3 candidate = point + dir * dist + faceNormal * safetyMargin;
                    if (!IsSolidForResolve(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
        public static Vector3 ClampToFace(Vector3 point, Brush brush, int faceIdx)
        {
            var face = brush.Faces[faceIdx];
            Vector3 best = point;
            float bestDist = float.MaxValue;

            for (int i = 0; i < face.Indices.Length; i += 3)
            {
                Vector3 p1 = brush.Vertices[face.Indices[i]]     + brush.Position;
                Vector3 p2 = brush.Vertices[face.Indices[i + 1]] + brush.Position;
                Vector3 p3 = brush.Vertices[face.Indices[i + 2]] + brush.Position;

                if (PointInTriangle(point, p1, p2, p3)) return point;

                Vector3 candidate = ClosestPointOnTriangle(point, p1, p2, p3);
                float d = Vector3.Distance(candidate, point);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }

            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float TriArea2D(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            Vector2 v1 = p1 - p3, v2 = p2 - p3;
            return (v1.X * v2.Y - v1.Y * v2.X) / 2f;
        }

        public static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            static bool SameSide(Vector3 p1, Vector3 p2, Vector3 from, Vector3 to)
            {
                var cp1 = Vector3.Cross(to - from, p1 - from);
                var cp2 = Vector3.Cross(to - from, p2 - from);
                return Vector3.Dot(cp1, cp2) >= -0.1f;
            }

            return SameSide(p, a, b, c) && SameSide(p, b, a, c) && SameSide(p, c, a, b);
        }

        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 ta, Vector3 tb, Vector3 tc)
        {
            Vector3 edge0 = tb - ta, edge1 = tc - ta, v0 = ta - p;

            float a = Vector3.Dot(edge0, edge0);
            float b = Vector3.Dot(edge0, edge1);
            float c = Vector3.Dot(edge1, edge1);
            float d = Vector3.Dot(edge0, v0);
            float e = Vector3.Dot(edge1, v0);

            float det = a * c - b * b;
            float s   = b * e - c * d;
            float t   = b * d - a * e;

            if (s + t < det)
            {
                if (s < 0f)
                {
                    if (t < 0f) { s = d < 0f ? Clamp01(-d / a) : 0f; t = d < 0f ? 0f : Clamp01(-e / c); }
                    else        { s = 0f; t = Clamp01(-e / c); }
                }
                else if (t < 0f)
                {
                    s = Clamp01(-d / a); t = 0f;
                }
                else
                {
                    float inv = 1f / det; s *= inv; t *= inv;
                }
            }
            else
            {
                if (s < 0f)
                {
                    float tmp0 = b + d, tmp1 = c + e;
                    if (tmp1 > tmp0) { float num = tmp1 - tmp0, den = a - 2 * b + c; s = Clamp01(num / den); t = 1 - s; }
                    else             { t = Clamp01(-e / c); s = 0f; }
                }
                else if (t < 0f)
                {
                    if (a + d > b + e) { float num = c + e - b - d, den = a - 2 * b + c; s = Clamp01(num / den); t = 1 - s; }
                    else               { s = Clamp01(-e / c); t = 0f; }
                }
                else
                {
                    float num = c + e - b - d, den = a - 2 * b + c;
                    s = Clamp01(num / den); t = 1f - s;
                }
            }

            return ta + s * edge0 + t * edge1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float v) => MathF.Max(0f, MathF.Min(1f, v));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float v, float min, float max) => MathF.Max(min, MathF.Min(max, v));

        // ── Hemisphere sampling ──────────────────────────────────────────────

        public static float Halton(int index, int Base)
        {
            float result = 0f, f = 1f / Base;
            int i = index;
            while (i > 0) { result += f * (i % Base); i /= Base; f /= Base; }
            return result;
        }

        public static Vector3 GenerateHemisphereSample(Vector3 N, int index, float jitterU = 0f, float jitterV = 0f)
        {
            BuildOrthonormalBasis(N, out Vector3 T, out Vector3 B);

            float u = (Halton(index, 2) + jitterU) % 1f;
            float v = (Halton(index, 3) + jitterV) % 1f;

            float phi      = 2f * MathF.PI * u;
            float cosTheta = MathF.Sqrt(v);
            float sinTheta = MathF.Sqrt(1f - v);

            var L = new Vector3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
            return L.X * T + L.Y * B + L.Z * N;
        }

        public static void BuildOrthonormalBasis(Vector3 n, out Vector3 t, out Vector3 b)
        {
            Vector3 refAxis = MathF.Abs(n.Y) > 0.999f ? Vector3.UnitX : Vector3.UnitY;
            t = Vector3.Normalize(Vector3.Cross(refAxis, n));
            b = Vector3.Cross(n, t);
        }
    }
}

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace Chisel.Utils
{
    internal static class DelaunayTriangulator
    {
        private struct Triangle
        {
            public int A, B, C;
            public Vector2 CircumCenter;
            public float CircumRadiusSq;
        }

        public static List<(int a, int b, int c)> Triangulate(IReadOnlyList<Vector2> points)
        {
            var result = new List<(int a, int b, int c)>();
            int n = points.Count;
            if (n < 3) return result;

            float minX = points[0].X, minY = points[0].Y, maxX = points[0].X, maxY = points[0].Y;
            for (int i = 1; i < n; i++)
            {
                minX = Math.Min(minX, points[i].X);
                minY = Math.Min(minY, points[i].Y);
                maxX = Math.Max(maxX, points[i].X);
                maxY = Math.Max(maxY, points[i].Y);
            }

            float dx = maxX - minX;
            float dy = maxY - minY;
            float deltaMax = Math.Max(dx, dy);
            if (deltaMax <= 0f) deltaMax = 1f;
            float midX = (minX + maxX) * 0.5f;
            float midY = (minY + maxY) * 0.5f;

            var all = new Vector2[n + 3];
            for (int i = 0; i < n; i++)
            {
                all[i] = points[i];
            }
            all[n] = new Vector2(midX - 20f * deltaMax, midY - deltaMax);
            all[n + 1] = new Vector2(midX, midY + 20f * deltaMax);
            all[n + 2] = new Vector2(midX + 20f * deltaMax, midY - deltaMax);

            var triangles = new List<Triangle> { MakeTriangle(all, n, n + 1, n + 2) };

            for (int i = 0; i < n; i++)
            {
                Vector2 p = all[i];
                var badTriangles = new List<Triangle>();

                foreach (var tri in triangles)
                {
                    float dxc = p.X - tri.CircumCenter.X;
                    float dyc = p.Y - tri.CircumCenter.Y;
                    if (dxc * dxc + dyc * dyc <= tri.CircumRadiusSq)
                    {
                        badTriangles.Add(tri);
                    }
                }

                var polygon = new List<(int a, int b)>();
                foreach (var tri in badTriangles)
                {
                    AddBoundaryEdge(polygon, tri.A, tri.B, badTriangles);
                    AddBoundaryEdge(polygon, tri.B, tri.C, badTriangles);
                    AddBoundaryEdge(polygon, tri.C, tri.A, badTriangles);
                }

                triangles.RemoveAll(t => badTriangles.Contains(t));

                foreach (var (a, b) in polygon)
                {
                    triangles.Add(MakeTriangle(all, a, b, i));
                }
            }

            foreach (var tri in triangles)
            {
                if (tri.A >= n || tri.B >= n || tri.C >= n) continue;
                result.Add((tri.A, tri.B, tri.C));
            }

            return result;
        }

        private static void AddBoundaryEdge(List<(int a, int b)> polygon, int a, int b, List<Triangle> badTriangles)
        {
            int sharedCount = 0;
            foreach (var tri in badTriangles)
            {
                bool hasA = tri.A == a || tri.B == a || tri.C == a;
                bool hasB = tri.A == b || tri.B == b || tri.C == b;
                if (hasA && hasB) sharedCount++;
            }

            if (sharedCount == 1)
            {
                polygon.Add((a, b));
            }
        }

        private static Triangle MakeTriangle(Vector2[] points, int a, int b, int c)
        {
            CircumCircle(points[a], points[b], points[c], out Vector2 center, out float radiusSq);
            return new Triangle { A = a, B = b, C = c, CircumCenter = center, CircumRadiusSq = radiusSq };
        }

        private static void CircumCircle(Vector2 a, Vector2 b, Vector2 c, out Vector2 center, out float radiusSq)
        {
            float ax = a.X, ay = a.Y, bx = b.X, by = b.Y, cx = c.X, cy = c.Y;
            float d = 2f * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));

            if (Math.Abs(d) < 1e-9f)
            {
                center = a;
                radiusSq = float.MaxValue;
                return;
            }

            float ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            float uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;

            center = new Vector2(ux, uy);
            float ddx = ax - ux, ddy = ay - uy;
            radiusSq = ddx * ddx + ddy * ddy;
        }
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#if rockwall
using Rockwall2;
#if !unified
using Rockwall2.Utils;
using Xceed.Wpf.Toolkit;
#else
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Mapper;
using Rockwall3DView = Rockwall2.Editor.Common.EditorHost;
#endif
#endif
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Rockwall
{
    /// <summary>
    /// Provides static operations for manipulating and generating brushes and terrain in the editor.
    /// </summary>
    public static class BrushOperations
    {
#if rockwall
        /// <summary>
        /// Vertex positions for a unit cube, used for brush geometry.
        /// </summary>
        public static VertexPosition[] CubeVerts = {
                // Front face
                new VertexPosition(new Vector3(0, 1, 0)),
                new VertexPosition(new Vector3(0, 0, 0)),
                new VertexPosition(new Vector3(1, 1, 0)),
                new VertexPosition(new Vector3(1, 0, 0)),
                // Back face
                new VertexPosition(new Vector3(0, 1, 1)),
                new VertexPosition(new Vector3(0, 0, 1)),
                new VertexPosition(new Vector3(1, 1, 1)),
                new VertexPosition(new Vector3(1, 0, 1)),
                // Top face
                new VertexPosition(new Vector3(0, 1, 0)),
                new VertexPosition(new Vector3(1, 1, 0)),
                new VertexPosition(new Vector3(0, 1, 1)),
                new VertexPosition(new Vector3(1, 1, 1)),
                // Bottom face
                new VertexPosition(new Vector3(0, 0, 0)),
                new VertexPosition(new Vector3(1, 0, 0)),
                new VertexPosition(new Vector3(0, 0, 1)),
                new VertexPosition(new Vector3(1, 0, 1)),
                // Left face
                new VertexPosition(new Vector3(0, 1, 0)),
                new VertexPosition(new Vector3(0, 1, 1)),
                new VertexPosition(new Vector3(0, 0, 0)),
                new VertexPosition(new Vector3(0, 0, 1)),
                // Right face
                new VertexPosition(new Vector3(1, 1, 0)),
                new VertexPosition(new Vector3(1, 1, 1)),
                new VertexPosition(new Vector3(1, 0, 0)),
                new VertexPosition(new Vector3(1, 0, 1))
            };

        /// <summary>
        /// Corner positions for a unit cube.
        /// </summary>
        public static Vector3[] CubeCorners =
        {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, 0, 1),
                new Vector3(1, 1, 0),
                new Vector3(1, 0, 1),
                new Vector3(0, 1, 1),
                new Vector3(1, 1, 1),
            };

        /// <summary>
        /// Indices for triangles of a cube (6 faces, 2 triangles per face).
        /// </summary>
        public static short[] tri = {
                // Front
                0, 2, 1,
                1, 2, 3,
                // Back
                5, 6, 4,
                7, 6, 5,
                // Top
                8, 10, 9,
                9, 10, 11,
                // Bottom
                12, 13, 14,
                13, 15, 14,
                // Left
                18, 17, 16,
                18, 19, 17,
                // Right
                21, 22, 20,
                23, 22, 21
            };

        /// <summary>
        /// Indices for drawing cube outlines (edges).
        /// </summary>
        public static short[] outlineTris = {
                // Front
                0, 1,
                0, 2,
                1, 3,
                3, 2,
                // Back
                4, 5,
                4, 6,
                5, 7,
                7, 6,
                0, 4,
                1, 5,
                2, 6,
                3, 7,
            };

#if compiler || ENABLE_BSP
            private static BSPNode bspRoot;
#endif
        const float PlanarTolerance = 0.01f;

        /// <summary>
        /// Creates the 24 vertices for a box, given a bounding box.
        /// </summary>
        public static Vector3[] CreateVertices(BoundingBox box)
        {
            Vector3 dist = box.Max - box.Min;
            float width = dist.X;
            float height = dist.Y;
            float length = dist.Z;
            return new[]{
                    // Front face
                    new Vector3(0,     height, 0) + box.Min,
                    new Vector3(0,     0, 0) + box.Min,
                    new Vector3(width, height, 0) + box.Min,
                    new Vector3(width, 0, 0) + box.Min,
                    // Back face
                    new Vector3(0,     height, length) + box.Min,
                    new Vector3(0,     0,      length) + box.Min,
                    new Vector3(width, height, length) + box.Min,
                    new Vector3(width, 0,      length) + box.Min,
                    // Top face
                    new Vector3(0,     height, 0) + box.Min,
                    new Vector3(width, height, 0) + box.Min,
                    new Vector3(0,     height, length) + box.Min,
                    new Vector3(width, height, length) + box.Min,
                    // Bottom face
                    new Vector3(0,     0, 0) + box.Min,
                    new Vector3(width, 0, 0) + box.Min,
                    new Vector3(0,     0, length) + box.Min,
                    new Vector3(width, 0, length) + box.Min,
                    // Left face
                    new Vector3(0,     height, 0) + box.Min,
                    new Vector3(0,     height, length) + box.Min,
                    new Vector3(0,     0, 0) + box.Min,
                    new Vector3(0,     0, length) + box.Min,
                    // Right face
                    new Vector3(width, height, 0) + box.Min,
                    new Vector3(width, height, length) + box.Min,
                    new Vector3(width, 0, 0) + box.Min,
                    new Vector3(width, 0, length) + box.Min
                };
        }

#if rockwall
        public static void AddRing(List<VertexPositionColor> verts, List<int> inds, Vector3 axis, Color color, float radius, float thickness, int segments)
        {
            Vector3 u = Vector3.Cross(axis, Vector3.Up);
            if (u.LengthSquared() < 0.01f) u = Vector3.Cross(axis, Vector3.Right);
            u.Normalize();
            Vector3 v = Vector3.Cross(axis, u);

            int baseIndex = verts.Count;
            float inner = radius - thickness * 0.5f;
            float outer = radius + thickness * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float angle = MathHelper.TwoPi * i / segments;
                Vector3 dir = MathF.Cos(angle) * u + MathF.Sin(angle) * v;
                verts.Add(new VertexPositionColor(dir * inner, color));
                verts.Add(new VertexPositionColor(dir * outer, color));
            }

            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + i * 2;
                int i1 = baseIndex + ((i * 2 + 2) % (segments * 2));
                int i2 = baseIndex + i * 2 + 1;
                int i3 = baseIndex + ((i * 2 + 3) % (segments * 2));
                inds.Add(i0); inds.Add(i1); inds.Add(i2);
                inds.Add(i2); inds.Add(i1); inds.Add(i3);
            }
        }
        public static void AddAxis(List<VertexPositionColor> verts, List<int> inds, Vector3 dir, Color color, float length, float coneLength, float shaftRadius, float coneRadius)
        {
            Vector3 shaftEnd = dir * length;
            AddShaft(verts, inds, Vector3.Zero, shaftEnd, dir, color, shaftRadius);
            AddCone(verts, inds, shaftEnd, dir, color, coneLength, coneRadius);
        }

        public static void AddShaft(List<VertexPositionColor> verts, List<int> inds, Vector3 start, Vector3 end, Vector3 direction, Color color, float radius)
        {
            int segments = 8;
            Vector3 up = Vector3.Cross(direction, Vector3.Up);
            if (up.LengthSquared() < 0.01f)
                up = Vector3.Cross(direction, Vector3.Right);
            up.Normalize();
            Vector3 right = Vector3.Cross(direction, up);
            up = Vector3.Cross(right, direction);

            int baseIndex = verts.Count;

            for (int i = 0; i < segments; i++)
            {
                float angle = MathHelper.TwoPi * i / segments;
                Vector3 offset = (float)Math.Cos(angle) * right + (float)Math.Sin(angle) * up;
                offset *= radius;

                verts.Add(new VertexPositionColor(start + offset, color));
                verts.Add(new VertexPositionColor(end + offset, color));
            }

            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + i * 2;
                int i1 = baseIndex + ((i * 2 + 2) % (segments * 2));
                int i2 = baseIndex + i * 2 + 1;
                int i3 = baseIndex + ((i * 2 + 3) % (segments * 2));

                inds.Add(i0);
                inds.Add(i1);
                inds.Add(i2);

                inds.Add(i2);
                inds.Add(i1);
                inds.Add(i3);
            }
        }

        public static void AddCone(List<VertexPositionColor> verts, List<int> inds, Vector3 basePos, Vector3 direction, Color color, float length, float radius)
        {
            int segments = 16;
            Vector3 tip = basePos + direction * length;

            Vector3 up = Vector3.Cross(direction, Vector3.Up);
            if (up.LengthSquared() < 0.01f)
                up = Vector3.Cross(direction, Vector3.Right);
            up.Normalize();
            Vector3 right = Vector3.Cross(direction, up);
            up = Vector3.Cross(right, direction);

            int baseIndex = verts.Count;
            Vector3[] rimPoints = new Vector3[segments];

            for (int i = 0; i < segments; i++)
            {
                float angle = MathHelper.TwoPi * i / segments;
                Vector3 dir = (float)Math.Cos(angle) * right + (float)Math.Sin(angle) * up;
                rimPoints[i] = basePos + dir * radius;
                verts.Add(new VertexPositionColor(rimPoints[i], color));
            }
            verts.Add(new VertexPositionColor(tip, color));
            int tipIndex = verts.Count - 1;

            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + i;
                int i1 = baseIndex + ((i + 1) % segments);
                inds.Add(i0);
                inds.Add(i1);
                inds.Add(tipIndex);
            }

            verts.Add(new VertexPositionColor(basePos, color));
            int centerIndex = verts.Count - 1;

            for (int i = 0; i < segments; i++)
            {
                int i0 = baseIndex + ((i + 1) % segments);
                int i1 = baseIndex + i;
                inds.Add(i0);
                inds.Add(i1);
                inds.Add(centerIndex);
            }
        }
        /// <summary>
        /// Generates sphere vertices as triangles for a sphere mesh.
        /// </summary>
        public static VertexPosition[] GenerateSphereVerticesDirect(Vector3 origin, float radius = 1.0f, int latitudeSegments = 10, int longitudeSegments = 10)
        {
            List<VertexPosition> sphereVerts = new List<VertexPosition>();
            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                float phi1 = (float)Math.PI * lat / latitudeSegments;
                float phi2 = (float)Math.PI * (lat + 1) / latitudeSegments;
                float y1 = radius * (float)Math.Cos(phi1);
                float latRadius1 = radius * (float)Math.Sin(phi1);
                float y2 = radius * (float)Math.Cos(phi2);
                float latRadius2 = radius * (float)Math.Sin(phi2);
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    float theta1 = 2 * (float)Math.PI * lon / longitudeSegments;
                    float theta2 = 2 * (float)Math.PI * (lon + 1) / longitudeSegments;
                    Vector3 v1 = new Vector3(latRadius1 * (float)Math.Cos(theta1), y1, latRadius1 * (float)Math.Sin(theta1));
                    Vector3 v2 = new Vector3(latRadius1 * (float)Math.Cos(theta2), y1, latRadius1 * (float)Math.Sin(theta2));
                    Vector3 v3 = new Vector3(latRadius2 * (float)Math.Cos(theta1), y2, latRadius2 * (float)Math.Sin(theta1));
                    Vector3 v4 = new Vector3(latRadius2 * (float)Math.Cos(theta2), y2, latRadius2 * (float)Math.Sin(theta2));
                    // Two triangles per quad
                    sphereVerts.Add(new VertexPosition(v1 + origin));
                    sphereVerts.Add(new VertexPosition(v3 + origin));
                    sphereVerts.Add(new VertexPosition(v2 + origin));
                    sphereVerts.Add(new VertexPosition(v2 + origin));
                    sphereVerts.Add(new VertexPosition(v3 + origin));
                    sphereVerts.Add(new VertexPosition(v4 + origin));
                }
            }
            return sphereVerts.ToArray();
        }

        /// <summary>
        /// Returns the 12 edges of a bounding box as line segments for debug drawing.
        /// </summary>
        public static VertexPosition[] GetDebugEdges(BoundingBox box)
        {
            VertexPosition[] corners = new VertexPosition[8]
            {
                    new VertexPosition(new Vector3(box.Min.X,box.Min.Y,box.Min.Z)),
                    new VertexPosition(new Vector3(box.Max.X,box.Min.Y,box.Min.Z)),
                    new VertexPosition(new Vector3(box.Min.X,box.Max.Y,box.Min.Z)),
                    new VertexPosition(new Vector3(box.Min.X,box.Min.Y,box.Max.Z)),
                    new VertexPosition(new Vector3(box.Max.X,box.Max.Y,box.Min.Z)),
                    new VertexPosition(new Vector3(box.Max.X,box.Max.Y,box.Max.Z)),
                    new VertexPosition(new Vector3(box.Min.X,box.Max.Y,box.Max.Z)),
                    new VertexPosition(new Vector3(box.Max.X,box.Min.Y,box.Max.Z))
            };
            VertexPosition[] edges = new VertexPosition[]
            {
                    corners[0],corners[1],
                    corners[0],corners[2],
                    corners[1],corners[4],
                    corners[2],corners[4],
                    corners[0],corners[3],
                    corners[3],corners[6],
                    corners[6],corners[2],
                    corners[7],corners[1],
                    corners[7],corners[5],
                    corners[5],corners[4],
                    corners[3],corners[7],
                    corners[5],corners[6],
            };
            return edges;
        }

        /// <summary>
        /// Shared vertex buffer for cube rendering.
        /// </summary>
        public static VertexBuffer cubebuffer;

        /// <summary>
        /// Creates a brush with the given dimensions at the origin.
        /// </summary>
        public static Brush CreateBrush(float width, float height, float length)
        {
            return CreateBrush(Vector3.Zero, new Vector3(width, height, length));
        }

        /// <summary>
        /// Creates a terrain mesh from a face of a brush, using the specified grid power.
        /// </summary>
        public static Terrain? CreateTerrainFromFace(int brushID, int faceID, int power)
        {
            var brush = MapTools.Brushes[brushID];
            var face = MapTools.Brushes[brushID].Faces[faceID];

            // Collect unique vertices
            HashSet<Vector3> uniqueVertices = new HashSet<Vector3>();
            for (int i = 0; i < face.Indices.Length; i++)
            {
                uniqueVertices.Add(brush.Vertices[face.Indices[i]] + brush.Position);
            }
            // Ensure we have exactly 4 unique vertices
            if (uniqueVertices.Count != 4)
            {
                Console.WriteLine("Invalid face!");
                return null;
            }
            // Convert to a list for processing
            List<Vector3> corners = uniqueVertices.ToList();
            // Arrange vertices in counterclockwise order
            Vector3 normal = -Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
            normal.Normalize();
            corners.Sort((a, b) =>
            {
                Vector3 cross = Vector3.Cross(a - corners[0], b - corners[0]);
                float dot = Vector3.Dot(cross, normal);
                return dot > 0 ? -1 : 1; // Counterclockwise order
            });
            return CreateTerrain(corners[0], corners[1], corners[2], corners[3], face.Surface, power, face.Normal, brushID, faceID);
        }
        public static Terrain CreateTerrain(Vector3 corner0, Vector3 corner1, Vector3 corner2, Vector3 corner3,
            int surface, int power, Vector3 normal, int brushID, int faceID)
        {
            int resolution = (1 << power) + 1;
            List<Vector3> vertPos = new List<Vector3>();
            List<short> triangles = new List<short>();
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);

            for (int z = 0; z < resolution; z++)
            {
                float v = (float)z / (resolution - 1);
                Vector3 edge0 = Vector3.Lerp(corner0, corner3, v);
                Vector3 edge1 = Vector3.Lerp(corner1, corner2, v);
                for (int x = 0; x < resolution; x++)
                {
                    float u = (float)x / (resolution - 1);
                    Vector3 position = Vector3.Lerp(edge0, edge1, u);
                    vertPos.Add(position);
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }

            for (int z = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int topLeft = z * resolution + x;
                    int topRight = topLeft + 1;
                    int bottomLeft = (z + 1) * resolution + x;
                    int bottomRight = bottomLeft + 1;

                    if (((x + z) & 1) == 0)
                    {
                        triangles.Add((short)topLeft); triangles.Add((short)bottomLeft); triangles.Add((short)topRight);
                        triangles.Add((short)topRight); triangles.Add((short)bottomLeft); triangles.Add((short)bottomRight);
                    }
                    else
                    {
                        triangles.Add((short)topLeft); triangles.Add((short)bottomLeft); triangles.Add((short)bottomRight);
                        triangles.Add((short)topLeft); triangles.Add((short)bottomRight); triangles.Add((short)topRight);
                    }
                }
            }

            var uvs = UvCalculator.CalculateUVs(vertPos.ToArray(), triangles.ToArray(), 2f);
            var (normals, tangents, handedness) = ComputeNormalsAndTangents(vertPos, triangles, uvs);

            List<TerrainVertex> vertices = new List<TerrainVertex>();
            for (int v = 0; v < vertPos.Count; v++)
                vertices.Add(new TerrainVertex(vertPos[v], Vector2.Zero, normals[v],
                                               new Vector3(uvs[v], 1), tangents[v], handedness[v]));

            return new Terrain
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles.ToArray(),
                Surface = surface,
                Bounds = new BoundingBox(min, max),
                BrushSource = brushID,
                FaceSource = faceID,
                SourceNormal = normal,
            };
        }

        /// <summary>
        /// Moves all vertices of a terrain by a given amount and updates the mesh.
        /// </summary>
        public static void MoveTerrain(ref Terrain terrain, Vector3 moveAmount)
        {
            for (int v = 0; v < terrain.Vertices.Length; v++)
            {
                terrain.Vertices[v].Position += moveAmount;
            }
            UpdateTerrain(ref terrain);
        }

        public struct TerrainSyncEntry
        {
            public int terrainIndex;
            public Vector3[] oldCorners;
            public bool[] cornerMoved;
        }

        static readonly int[][] cornerPermutations = new int[][]
        {
            new[]{0,1,2,3}, new[]{0,1,3,2}, new[]{0,2,1,3}, new[]{0,2,3,1}, new[]{0,3,1,2}, new[]{0,3,2,1},
            new[]{1,0,2,3}, new[]{1,0,3,2}, new[]{1,2,0,3}, new[]{1,2,3,0}, new[]{1,3,0,2}, new[]{1,3,2,0},
            new[]{2,0,1,3}, new[]{2,0,3,1}, new[]{2,1,0,3}, new[]{2,1,3,0}, new[]{2,3,0,1}, new[]{2,3,1,0},
            new[]{3,0,1,2}, new[]{3,0,2,1}, new[]{3,1,0,2}, new[]{3,1,2,0}, new[]{3,2,0,1}, new[]{3,2,1,0},
        };

        public static Vector3[] TerrainReferenceCorners(Terrain terrain)
        {
            int resolution = (int)MathF.Round(MathF.Sqrt(terrain.Vertices.Length));
            return new Vector3[]
            {
                terrain.Vertices[0].Position,
                terrain.Vertices[resolution - 1].Position,
                terrain.Vertices[resolution * resolution - 1].Position,
                terrain.Vertices[(resolution - 1) * resolution].Position,
            };
        }

        public static Vector3[] MatchCornersToReference(Vector3[] corners, Vector3[] reference)
        {
            int[] bestPerm = cornerPermutations[0];
            float bestCost = float.MaxValue;
            foreach (var perm in cornerPermutations)
            {
                float cost = 0f;
                for (int i = 0; i < 4; i++)
                    cost += Vector3.DistanceSquared(corners[perm[i]], reference[i]);
                if (cost < bestCost) { bestCost = cost; bestPerm = perm; }
            }

            var result = new Vector3[4];
            for (int i = 0; i < 4; i++) result[i] = corners[bestPerm[i]];
            return result;
        }

        public static Vector3 BilinearQuad(Vector3[] corners, float u, float v)
        {
            Vector3 edge0 = Vector3.Lerp(corners[0], corners[3], v);
            Vector3 edge1 = Vector3.Lerp(corners[1], corners[2], v);
            return Vector3.Lerp(edge0, edge1, u);
        }

        public static List<TerrainSyncEntry> CaptureTerrainSync(int brushIndex, IEnumerable<Vector3> movedWorldPositions)
        {
            var moved = movedWorldPositions.ToList();
            var result = new List<TerrainSyncEntry>();
            if (brushIndex < 0 || brushIndex >= MapTools.Brushes.Length) return result;

            var brush = MapTools.Brushes[brushIndex];

            for (int i = 0; i < MapTools.Terrains.Length; i++)
            {
                var terrain = MapTools.Terrains[i];
                if (terrain.BrushSource != brushIndex) continue;
                if (terrain.FaceSource < 0 || terrain.FaceSource >= brush.Faces.Length) continue;

                var face = brush.Faces[terrain.FaceSource];
                if (GetUniqueFaceCorners(brush, face).Length != 4) continue;

                var faceCorners = GetUniqueFaceCorners(brush, face);
                var refCorners = TerrainReferenceCorners(terrain);
                var oldCorners = MatchCornersToReference(faceCorners, refCorners);

                var cornerMoved = new bool[4];
                for (int c = 0; c < 4; c++)
                    cornerMoved[c] = moved.Any(p => Vector3.DistanceSquared(p, oldCorners[c]) < 0.0025f);

                result.Add(new TerrainSyncEntry
                {
                    terrainIndex = i,
                    oldCorners = oldCorners,
                    cornerMoved = cornerMoved
                });
            }

            return result;
        }

        public static bool ApplyTerrainSync(int brushIndex, List<TerrainSyncEntry> captured, Vector3 delta, out List<int> brokenTerrains)
        {
            brokenTerrains = new List<int>();
            if (captured == null || captured.Count == 0) return true;
            if (brushIndex < 0 || brushIndex >= MapTools.Brushes.Length) return true;

            var brush = MapTools.Brushes[brushIndex];

            foreach (var entry in captured)
            {
                if (entry.terrainIndex < 0 || entry.terrainIndex >= MapTools.Terrains.Length) continue;

                var terrain = MapTools.Terrains[entry.terrainIndex];
                if (terrain.FaceSource < 0 || terrain.FaceSource >= brush.Faces.Length)
                {
                    brokenTerrains.Add(entry.terrainIndex);
                    continue;
                }

                var face = brush.Faces[terrain.FaceSource];
                if (GetUniqueFaceCorners(brush, face).Length != 4)
                    brokenTerrains.Add(entry.terrainIndex);
            }

            if (brokenTerrains.Count > 0) return false;

            foreach (var entry in captured)
            {
                var terrain = MapTools.Terrains[entry.terrainIndex];

                var newCorners = (Vector3[])entry.oldCorners.Clone();
                for (int c = 0; c < 4; c++)
                    if (entry.cornerMoved[c]) newCorners[c] += delta;

                int resolution = (int)MathF.Round(MathF.Sqrt(terrain.Vertices.Length));
                for (int z = 0; z < resolution; z++)
                {
                    float v = (float)z / (resolution - 1);
                    for (int x = 0; x < resolution; x++)
                    {
                        float u = (float)x / (resolution - 1);
                        int idx = z * resolution + x;

                        Vector3 oldFlat = BilinearQuad(entry.oldCorners, u, v);
                        Vector3 newFlat = BilinearQuad(newCorners, u, v);

                        terrain.Vertices[idx].Position += newFlat - oldFlat;
                    }
                }

                UpdateTerrain(ref terrain);
                MapTools.Terrains[entry.terrainIndex] = terrain;
            }

            return true;
        }

        public static bool ApplyTerrainSyncTransform(int brushIndex, List<TerrainSyncEntry> captured, Func<Vector3, Vector3> transform, out List<int> brokenTerrains)
        {
            brokenTerrains = new List<int>();
            if (captured == null || captured.Count == 0) return true;
            if (brushIndex < 0 || brushIndex >= MapTools.Brushes.Length) return true;

            var brush = MapTools.Brushes[brushIndex];

            foreach (var entry in captured)
            {
                if (entry.terrainIndex < 0 || entry.terrainIndex >= MapTools.Terrains.Length) continue;
                var terrain = MapTools.Terrains[entry.terrainIndex];
                if (terrain.FaceSource < 0 || terrain.FaceSource >= brush.Faces.Length)
                {
                    brokenTerrains.Add(entry.terrainIndex);
                    continue;
                }
                var face = brush.Faces[terrain.FaceSource];
                if (GetUniqueFaceCorners(brush, face).Length != 4)
                    brokenTerrains.Add(entry.terrainIndex);
            }

            if (brokenTerrains.Count > 0) return false;

            foreach (var entry in captured)
            {
                var terrain = MapTools.Terrains[entry.terrainIndex];

                var newCorners = new Vector3[4];
                for (int c = 0; c < 4; c++)
                    newCorners[c] = transform(entry.oldCorners[c]);

                int resolution = (int)MathF.Round(MathF.Sqrt(terrain.Vertices.Length));
                for (int z = 0; z < resolution; z++)
                {
                    float v = (float)z / (resolution - 1);
                    for (int x = 0; x < resolution; x++)
                    {
                        float u = (float)x / (resolution - 1);
                        int idx = z * resolution + x;

                        Vector3 oldFlat = BilinearQuad(entry.oldCorners, u, v);
                        Vector3 newFlat = BilinearQuad(newCorners, u, v);

                        terrain.Vertices[idx].Position += newFlat - oldFlat;
                    }
                }

                UpdateTerrain(ref terrain);
                MapTools.Terrains[entry.terrainIndex] = terrain;
            }

            return true;
        }

        public static Terrain? ResampleTerrainOntoFace(Terrain original, Vector3[] originalFaceCorners, int newBrushIndex, int newFaceIndex)
        {
            var brush = MapTools.Brushes[newBrushIndex];
            var face = brush.Faces[newFaceIndex];
            if (GetUniqueFaceCorners(brush, face).Length != 4) return null;

            var newCornersRaw = GetUniqueFaceCorners(brush, face);
            var newCorners = MatchCornersToReference(newCornersRaw, originalFaceCorners);

            int resolution = (int)MathF.Round(MathF.Sqrt(original.Vertices.Length));

            var oldRes = resolution;
            Vector3 SampleOriginal(float u, float v)
            {
                float gx = u * (oldRes - 1);
                float gz = v * (oldRes - 1);
                int x0 = (int)MathF.Floor(gx);
                int z0 = (int)MathF.Floor(gz);
                x0 = Math.Clamp(x0, 0, oldRes - 1);
                z0 = Math.Clamp(z0, 0, oldRes - 1);
                int x1 = Math.Clamp(x0 + 1, 0, oldRes - 1);
                int z1 = Math.Clamp(z0 + 1, 0, oldRes - 1);
                float tx = Math.Clamp(gx - x0, 0f, 1f);
                float tz = Math.Clamp(gz - z0, 0f, 1f);

                Vector3 p00 = original.Vertices[z0 * oldRes + x0].Position;
                Vector3 p10 = original.Vertices[z0 * oldRes + x1].Position;
                Vector3 p01 = original.Vertices[z1 * oldRes + x0].Position;
                Vector3 p11 = original.Vertices[z1 * oldRes + x1].Position;

                return Vector3.Lerp(Vector3.Lerp(p00, p10, tx), Vector3.Lerp(p01, p11, tx), tz);
            }

            List<Vector3> vertPos = new List<Vector3>();
            List<short> triangles = new List<short>();
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);

            for (int z = 0; z < resolution; z++)
            {
                float v = (float)z / (resolution - 1);
                for (int x = 0; x < resolution; x++)
                {
                    float u = (float)x / (resolution - 1);

                    Vector3 newFlat = BilinearQuad(newCorners, u, v);
                    Vector3 oldUv = InverseBilinearQuad(originalFaceCorners, newFlat);

                    Vector3 position = SampleOriginal(oldUv.X, oldUv.Y);
                    vertPos.Add(position);
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }

            for (int z = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int topLeft = z * resolution + x;
                    int topRight = topLeft + 1;
                    int bottomLeft = (z + 1) * resolution + x;
                    int bottomRight = bottomLeft + 1;

                    if (((x + z) & 1) == 0)
                    {
                        triangles.Add((short)topLeft); triangles.Add((short)bottomLeft); triangles.Add((short)topRight);
                        triangles.Add((short)topRight); triangles.Add((short)bottomLeft); triangles.Add((short)bottomRight);
                    }
                    else
                    {
                        triangles.Add((short)topLeft); triangles.Add((short)bottomLeft); triangles.Add((short)bottomRight);
                        triangles.Add((short)topLeft); triangles.Add((short)bottomRight); triangles.Add((short)topRight);
                    }
                }
            }

            var uvs = UvCalculator.CalculateUVs(vertPos.ToArray(), triangles.ToArray(), 2f);
            var (normals, tangents, handedness) = ComputeNormalsAndTangents(vertPos, triangles, uvs);

            List<TerrainVertex> vertices = new List<TerrainVertex>();
            for (int v = 0; v < vertPos.Count; v++)
                vertices.Add(new TerrainVertex(vertPos[v], Vector2.Zero, normals[v],
                                               new Vector3(uvs[v], 1), tangents[v], handedness[v]));

            return new Terrain
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles.ToArray(),
                Surface = original.Surface,
                BlendedSurface = original.BlendedSurface,
                Bounds = new BoundingBox(min, max),
                BrushSource = newBrushIndex,
                FaceSource = newFaceIndex,
                SourceNormal = face.Normal,
            };
        }

        static Vector3 InverseBilinearQuad(Vector3[] corners, Vector3 point)
        {
            float bestU = 0.5f, bestV = 0.5f, bestDist = float.MaxValue;
            for (int iz = 0; iz <= 8; iz++)
            {
                float tv = iz / 8f;
                for (int ix = 0; ix <= 8; ix++)
                {
                    float tu = ix / 8f;
                    Vector3 sample = BilinearQuad(corners, tu, tv);
                    float d = Vector3.DistanceSquared(sample, point);
                    if (d < bestDist) { bestDist = d; bestU = tu; bestV = tv; }
                }
            }

            for (int iter = 0; iter < 6; iter++)
            {
                float step = 1f / (8f * MathF.Pow(2f, iter + 1));
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float tu = Math.Clamp(bestU + dx * step, 0f, 1f);
                        float tv = Math.Clamp(bestV + dz * step, 0f, 1f);
                        Vector3 sample = BilinearQuad(corners, tu, tv);
                        float d = Vector3.DistanceSquared(sample, point);
                        if (d < bestDist) { bestDist = d; bestU = tu; bestV = tv; }
                    }
                }
            }

            return new Vector3(bestU, bestV, 0f);
        }
        public static Vector3[] GetUniqueFaceCorners(Brush brush, Face face)
        {
            var unique = new List<Vector3>();
            if (face.Indices != null)
            {
                foreach (var idx in face.Indices)
                {
                    if (idx < 0 || idx >= brush.Vertices.Length) continue;
                    var pos = brush.Vertices[idx] + brush.Position;
                    if (!unique.Any(p => Vector3.DistanceSquared(p, pos) < 0.0001f))
                        unique.Add(pos);
                }
            }
            return unique.ToArray();
        }

        /// <summary>
        /// Creates a brush from two points (start and end), defining its size and position.
        /// </summary>
        public static Brush CreateBrush(Vector3 start, Vector3 end)
        {
            Vector3 dist = end - start;
            float width = dist.X;
            float height = dist.Y;
            float length = dist.Z;
            Vector3[] vertices = {
                    // Front face
                    new Vector3(0,     height, 0),
                    new Vector3(0,     0, 0),
                    new Vector3(width, height, 0),
                    new Vector3(width, 0, 0),
                    // Back face
                    new Vector3(0,     height, length),
                    new Vector3(0,     0,      length),
                    new Vector3(width, height, length),
                    new Vector3(width, 0,      length),
                    // Top face
                    new Vector3(0,     height, 0),
                    new Vector3(width, height, 0),
                    new Vector3(0,     height, length),
                    new Vector3(width, height, length),
                    // Bottom face
                    new Vector3(0,     0, 0),
                    new Vector3(width, 0, 0),
                    new Vector3(0,     0, length),
                    new Vector3(width, 0, length),
                    // Left face
                    new Vector3(0,     height, 0),
                    new Vector3(0,     height, length),
                    new Vector3(0,     0, 0),
                    new Vector3(0,     0, length),
                    // Right face
                    new Vector3(width, height, 0),
                    new Vector3(width, height, length),
                    new Vector3(width, 0, 0),
                    new Vector3(width, 0, length)
                };
            int surf = GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture];
            Face[] faces = new[]
            {
                    new Face{Normal = new Vector3(0,  0, -1), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                    new Face{Normal = new Vector3(0,  0,  1), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                    new Face{Normal = new Vector3(0,  1,  0), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                    new Face{Normal = new Vector3(0, -1,  0), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                    new Face{Normal = new Vector3(-1, 0,  0), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                    new Face{Normal = new Vector3(1,  0,  0), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f}
                };
            Brush brush = new Brush
            {
                Position = start,
                Vertices = vertices,
                Width = width,
                Height = height,
                Length = length,
            };
            for (int h = 0; h < faces.Length; h++)
            {
                faces[h].TScaleX = 1;
                faces[h].TScaleY = 1;
                faces[h].Indices = new int[6];
                for (int i = 0; i < 6; i++)
                {
                    int triIndex = i + h * 6;
                    faces[h].Indices[i] = tri[triIndex];
                }
            }
            brush.Faces = faces;
            Vector2[] uvs = CreateUVs(brush);
            brush.Vertices = vertices;
            brush.UVs = uvs;
            for (int h = 0; h < faces.Length; h++)
            {
                for (int i = 0; i < 6; i++)
                {
                    brush.Faces[h].editorVerts.Add(new VertexLightmapped(brush.Vertices[brush.Faces[h].Indices[i]], brush.Faces[h].Normal, brush.UVs[brush.Faces[h].Indices[i]], Vector2.Zero));
                }
                brush.Faces[h].vertexBuffer = new VertexBuffer(Rockwall3DView.Instance.GraphicsDevice, typeof(VertexLightmapped), brush.Faces[h].editorVerts.Count, BufferUsage.WriteOnly);
                brush.Faces[h].vertexBuffer.SetData(brush.Faces[h].editorVerts.ToArray());
            }

            RecalculateBrushPlanes(ref brush);

            return brush;
        }
#endif
        /// <summary>
        /// Creates a brush from a set of planes.
        /// </summary>
        /// <param name="planes"></param>
        /// <param name="materialName"></param>
        /// <param name="surface"></param>
        /// <returns></returns>
        public static Brush? CreateBrushFromPlanes(List<Plane> planes, string materialName, int surface)
        {
            if (planes.Count < 4) return null;

            var faces = new Face[planes.Count];
            for (int i = 0; i < planes.Count; i++)
            {
                faces[i] = new Face
                {
                    Plane = planes[i],
                    Normal = Vector3.Normalize(planes[i].Normal),
                    Drawn = true,
                    MaterialName = materialName,
                    Surface = surface,
                    TScaleX = 1,
                    TScaleY = 1,
                    LuxelScale = 1f,
                    editorVerts = new List<VertexLightmapped>(),
                    Indices = new int[0]
                };
            }

            var result = new Brush
            {
                Position = Vector3.Zero,
                Faces = faces,
                Vertices = new Vector3[0],
                UVs = new Vector2[0],
            };

            RebuildBrush(ref result);
            return result.Faces.Length == 0 || result.Vertices.Length == 0 ? null : result;
        }

        /// <summary>
        /// Creates a brush from 8 corner points (4 base, 4 extruded).
        /// The corners should be ordered as: base0, base1, base2, base3, extruded0, extruded1, extruded2, extruded3
        /// </summary>
        public static Brush CreateBrushFromCorners(Vector3[] baseCorners, Vector3[] extrudedCorners)
        {
            // baseCorners: p0, p1, p2, p3 (rectangle on plane)
            // extrudedCorners: p0', p1', p2', p3' (rectangle offset by extrusion)
            if (baseCorners.Length != 4 || extrudedCorners.Length != 4)
                throw new ArgumentException("Must provide 4 base and 4 extruded corners.");

            // Build 24 vertices in the same order as CreateBrush (4 per face, 6 faces), but with reversed winding
            Vector3[] vertices = new Vector3[24];
            // Front face (base 0-1-2-3, reversed winding)
            vertices[0] = baseCorners[0];
            vertices[1] = baseCorners[2];
            vertices[2] = baseCorners[1];
            vertices[3] = baseCorners[3];
            // Back face (extruded 0-1-2-3, reversed winding)
            vertices[4] = extrudedCorners[0];
            vertices[5] = extrudedCorners[2];
            vertices[6] = extrudedCorners[1];
            vertices[7] = extrudedCorners[3];
            // Top face (base 2-3, extruded 2-3, reversed winding)
            vertices[8] = baseCorners[2];
            vertices[9] = extrudedCorners[2];
            vertices[10] = baseCorners[3];
            vertices[11] = extrudedCorners[3];
            // Bottom face (base 0-1, extruded 0-1, reversed winding)
            vertices[12] = baseCorners[0];
            vertices[13] = extrudedCorners[0];
            vertices[14] = baseCorners[1];
            vertices[15] = extrudedCorners[1];
            // Left face (base 2-0, extruded 2-0, reversed winding)
            vertices[16] = baseCorners[0];
            vertices[17] = extrudedCorners[0];
            vertices[18] = baseCorners[2];
            vertices[19] = extrudedCorners[2];
            // Right face (base 3-1, extruded 3-1, reversed winding)
            vertices[20] = baseCorners[1];
            vertices[21] = extrudedCorners[1];
            vertices[22] = baseCorners[3];
            vertices[23] = extrudedCorners[3];

            // Compute position as the minimum of all corners
            Vector3 position = baseCorners.Concat(extrudedCorners).Aggregate((a, b) => Vector3.Min(a, b));

            // Offset all vertices by position
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] -= position;

            int surf = GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture];
            Face[] faces = new[]
            {
                new Face{Normal = Vector3.Normalize(Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0])), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                new Face{Normal = Vector3.Normalize(Vector3.Cross(vertices[5] - vertices[4], vertices[6] - vertices[4])), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                new Face{Normal = Vector3.Normalize(Vector3.Cross(vertices[9] - vertices[8], vertices[10] - vertices[8])), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                new Face{Normal = Vector3.Normalize(Vector3.Cross(vertices[13] - vertices[12], vertices[14] - vertices[12])), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                new Face{Normal = Vector3.Normalize(Vector3.Cross(vertices[17] - vertices[16], vertices[18] - vertices[16])), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f},
                new Face{Normal = Vector3.Normalize(Vector3.Cross(vertices[21] - vertices[20], vertices[22] - vertices[20])), Drawn = true, editorVerts = new List<VertexLightmapped>(), MaterialName = Toolbelt.ActiveTexture, Surface = surf, LuxelScale = 1f}
            };

            // Assign indices for each face using tri array (6 indices per face)
            for (int h = 0; h < faces.Length; h++)
            {
                faces[h].TScaleX = 1;
                faces[h].TScaleY = 1;
                faces[h].Indices = new int[6];
                for (int i = 0; i < 6; i++)
                {
                    int triIndex = i + h * 6;
                    faces[h].Indices[i] = tri[triIndex];
                }
            }

            // Compute width, height, length as in CreateBrush
            Vector3 max = vertices[0], min = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }
            float width = max.X - min.X;
            float height = max.Y - min.Y;
            float length = max.Z - min.Z;

            Brush brush = new Brush
            {
                Position = position,
                Vertices = vertices,
                Width = width,
                Height = height,
                Length = length,
            };
            brush.Faces = faces;
            Vector2[] uvs = CreateUVs(brush);
            brush.UVs = uvs;
            for (int h = 0; h < faces.Length; h++)
            {
                for (int i = 0; i < 6; i++)
                {
                    brush.Faces[h].editorVerts.Add(new VertexLightmapped(brush.Vertices[brush.Faces[h].Indices[i]], brush.Faces[h].Normal, brush.UVs[brush.Faces[h].Indices[i]], Vector2.Zero));
                }
                brush.Faces[h].vertexBuffer = new VertexBuffer(Rockwall3DView.Instance.GraphicsDevice, typeof(VertexLightmapped), brush.Faces[h].editorVerts.Count, BufferUsage.WriteOnly);
                brush.Faces[h].vertexBuffer.SetData(brush.Faces[h].editorVerts.ToArray());
            }
            brush.Abnormal = true;

            // Lets just make sure everything is all kosher
            RebuildBrush(ref brush);

            uvs = CreateUVs(brush);
            brush.UVs = uvs;

            RebuildBrush(ref brush);

            return brush;
        }
#endif
        /// <summary>
        /// Generates brush geometry from face planes using CSG intersection
        /// </summary>
        public static void RecalculateBrushGeometry(ref Brush brush)
        {
            // Only regenerate if all faces have planes defined
            if (!AllFacesHavePlanes(brush))
            {
                ConvertLegacyBrush(ref brush);
            }

            // Find all vertices by intersecting sets of 3 planes
            List<Vector3> vertices = new List<Vector3>();
            List<int>[] faceIndices = new List<int>[brush.Faces.Length];

            for (int i = 0; i < brush.Faces.Length; i++)
            {
                faceIndices[i] = new List<int>();
            }

            // For each combination of 3 planes, find intersection point
            for (int i = 0; i < brush.Faces.Length - 2; i++)
            {
                for (int j = i + 1; j < brush.Faces.Length - 1; j++)
                {
                    for (int k = j + 1; k < brush.Faces.Length; k++)
                    {
                        var planeI = brush.Faces[i].Plane;
                        var planeJ = brush.Faces[j].Plane;
                        var planeK = brush.Faces[k].Plane;
                        if (!planeI.HasValue || !planeJ.HasValue || !planeK.HasValue)
                            continue;

                        Plane p1 = planeI.Value;
                        Plane p2 = planeJ.Value;
                        Plane p3 = planeK.Value;

                        Vector3? vertex = IntersectThreePlanes(p1, p2, p3);

                        if (vertex.HasValue)
                        {
                            Vector3 v = vertex.Value;

                            //v = Vector3.Round(v*512f)/512f;

                            // Check if this vertex is inside all other planes
                            bool inside = true;
                            for (int p = 0; p < brush.Faces.Length; p++)
                            {
                                if (p == i || p == j || p == k) continue;
                                var otherPlane = brush.Faces[p].Plane;
                                if (!otherPlane.HasValue) continue;

                                float dist = otherPlane.Value.DotCoordinate(v);
                                if (dist > 0.001f) // Outside this plane
                                {
                                    inside = false;
                                    break;
                                }
                            }

                            if (inside)
                            {
                                // Check if vertex already exists (within tolerance)
                                int existingIndex = FindVertex(vertices, v, 0.001f);
                                if (existingIndex == -1)
                                {
                                    existingIndex = vertices.Count;
                                    vertices.Add(v);
                                }

                                // Add to the three faces that created this vertex
                                if (!faceIndices[i].Contains(existingIndex))
                                    faceIndices[i].Add(existingIndex);
                                if (!faceIndices[j].Contains(existingIndex))
                                    faceIndices[j].Add(existingIndex);
                                if (!faceIndices[k].Contains(existingIndex))
                                    faceIndices[k].Add(existingIndex);
                            }
                        }
                    }
                }
            }
            for (int i = 0; i < brush.Faces.Length; i++)
            {
                if (faceIndices[i].Count < 3)
                {
                    brush.Faces[i].Indices = new int[0]; // will be stripped by RebuildBrush
                    continue;
                }

                var facePlane = brush.Faces[i].Plane;
                if (!facePlane.HasValue)
                {
                    brush.Faces[i].Indices = new int[0];
                    continue;
                }

                SortVerticesCCW(vertices, faceIndices[i], facePlane.Value);
                brush.Faces[i].Indices = TriangulateFace(faceIndices[i]);
            }

            // Create per-face-unique vertex list so no vertices are shared between faces (flat shading)
            List<Vector3> perFaceUniqueVerts = new List<Vector3>();

            for (int f = 0; f < brush.Faces.Length; f++)
            {
                var oldIndices = brush.Faces[f].Indices;
                if (oldIndices == null || oldIndices.Length == 0) continue;

                int[] newIndices = new int[oldIndices.Length];
                for (int i = 0; i < oldIndices.Length; i++)
                {
                    int sharedIndex = oldIndices[i];
                    Vector3 pos = vertices[sharedIndex];
                    perFaceUniqueVerts.Add(pos);
                    newIndices[i] = perFaceUniqueVerts.Count - 1;
                }

                brush.Faces[f].Indices = newIndices;
            }

            brush.Vertices = perFaceUniqueVerts.ToArray();
            brush.Abnormal = true;
        }
        /// <summary>
        /// Finds the intersection point of three planes
        /// </summary>
        public static Vector3? IntersectThreePlanes(Plane p1, Plane p2, Plane p3)
        {
            Vector3 n1 = p1.Normal;
            Vector3 n2 = p2.Normal;
            Vector3 n3 = p3.Normal;

            float denom = Vector3.Dot(n1, Vector3.Cross(n2, n3));

            // Planes are parallel or don't meet at a point
            if (Math.Abs(denom) < 0.00001f)
                return null;

            Vector3 point =
                (-p1.D * Vector3.Cross(n2, n3)
                 - p2.D * Vector3.Cross(n3, n1)
                 - p3.D * Vector3.Cross(n1, n2)) / denom;

            return point;
        }
        /// <summary>
        /// Finds an existing vertex within tolerance
        /// </summary>
        private static int FindVertex(List<Vector3> vertices, Vector3 v, float tolerance)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                if (Vector3.Distance(vertices[i], v) < tolerance)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Sorts vertices into counter-clockwise order around a plane
        /// </summary>
        private static void SortVerticesCCW(List<Vector3> allVertices, List<int> faceVertices, Plane plane)
        {
            if (faceVertices.Count < 3) return;

            // Find center of face
            Vector3 center = Vector3.Zero;
            foreach (int idx in faceVertices)
                center += allVertices[idx];
            center /= faceVertices.Count;

            // Choose an arbitrary reference direction
            Vector3 refDir = allVertices[faceVertices[0]] - center;
            refDir.Normalize();

            // Sort by angle around the plane normal
            faceVertices.Sort((a, b) =>
            {
                Vector3 dirA = allVertices[a] - center;
                Vector3 dirB = allVertices[b] - center;
                dirA.Normalize();
                dirB.Normalize();

                float angleA = (float)Math.Atan2(
                    Vector3.Dot(Vector3.Cross(refDir, dirA), plane.Normal),
                    Vector3.Dot(refDir, dirA)
                );
                float angleB = (float)Math.Atan2(
                    Vector3.Dot(Vector3.Cross(refDir, dirB), plane.Normal),
                    Vector3.Dot(refDir, dirB)
                );

                return angleA.CompareTo(angleB);
            });
        }

        /// <summary>
        /// Triangulates a convex face using fan triangulation
        /// </summary>
        private static int[] TriangulateFace(List<int> vertices)
        {
            if (vertices.Count < 3) return new int[0];

            List<int> indices = new List<int>();

            // Simple fan triangulation from first vertex
            for (int i = 1; i < vertices.Count - 1; i++)
            {
                indices.Add(vertices[0]);
                indices.Add(vertices[i]);
                indices.Add(vertices[i + 1]);
            }

            return indices.ToArray();
        }

        /// <summary>
        /// Checks if all faces in a brush have planes defined
        /// </summary>
        private static bool AllFacesHavePlanes(Brush brush)
        {
            foreach (var face in brush.Faces)
            {
                if (!face.Plane.HasValue)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Converts an old, vertex based brush, into the new plane based format.
        /// </summary>
        /// <param name="brush"></param>
        private static void ConvertLegacyBrush(ref Brush brush)
        {
            for (int f = 0; f < brush.Faces.Length; f++)
            {
                // This is a legacy map with the old brush method. Convert
                if (brush.Faces[f].Plane == null)
                {
                    brush.Faces[f].Plane = new Plane(brush.Vertices[brush.Faces[f].Indices[0]], brush.Faces[f].Normal);
                }
            }
        }
        public static void RecalculateBrushPlanes(ref Brush brush)
        {
            var faces = new List<Face>();

            // Thresholds for degeneracy checks (squared where appropriate).
            // These values are small absolute tolerances suitable for typical editor units.
            const float minEdgeDistanceSqr = 1e-8f;   // edges shorter than ~1e-4 units considered coincident
            const float minCrossLengthSqr = 1e-10f;   // cross product squared length threshold for near-zero area

            for (int f = 0; f < brush.Faces.Length; f++)
            {
                var face = brush.Faces[f];

                // Must have at least 3 indices to define a plane
                if (face.Indices == null || face.Indices.Length < 3)
                {
                    continue;
                }
                bool valid = false;
                for (int triOffset = 0; triOffset < face.Indices.Length / 3; triOffset++)
                {
                    int i0 = face.Indices[0 + (triOffset * 3)];
                    int i1 = face.Indices[1 + (triOffset * 3)];
                    int i2 = face.Indices[2 + (triOffset * 3)];

                    // Validate indices are in range
                    if (i0 < 0 || i1 < 0 || i2 < 0 ||
                        i0 >= brush.Vertices.Length || i1 >= brush.Vertices.Length || i2 >= brush.Vertices.Length)
                    {
                        continue;
                    }

                    Vector3 v0 = brush.Vertices[i0];
                    Vector3 v1 = brush.Vertices[i1];
                    Vector3 v2 = brush.Vertices[i2];

                    // Check for coincident vertices (all three nearly the same point)
                    float d01 = Vector3.DistanceSquared(v0, v1);
                    float d12 = Vector3.DistanceSquared(v1, v2);
                    float d02 = Vector3.DistanceSquared(v0, v2);
                    if (d01 < minEdgeDistanceSqr && d12 < minEdgeDistanceSqr && d02 < minEdgeDistanceSqr)
                    {
                        continue;
                    }

                    // Check for colinear / zero-area triangle using cross product magnitude.
                    Vector3 edge1 = v1 - v0;
                    Vector3 edge2 = v2 - v0;
                    Vector3 cross = Vector3.Cross(edge1, edge2);
                    if (cross.LengthSquared() < minCrossLengthSqr)
                    {
                        // Triangle area is effectively zero (points are colinear or nearly so)
                        continue;
                    }

                    valid = true;

                    face.Plane = new Plane(v0, v1, v2);
                    break;
                }

                // Valid face: compute the plane and write it back.
                if (valid) faces.Add(face);
            }

            brush.Faces = faces.ToArray();
        }
        /// <summary>
        /// Rebuilds a brush's UVs, normals, tangents, and binormals. If the brush is not abnormal, recalculates its geometry.
        /// </summary>
        public static void RebuildBrush(ref Brush brush)
        {
            RecalculateBrushGeometry(ref brush);

            brush.Faces = brush.Faces
                .Where(f => f.Indices != null && f.Indices.Length >= 3)
                .ToArray();

            if (brush.Faces.Length == 0) return;

            Vector2[] uvs = CreateUVs(brush);
            brush.UVs = uvs;

            for (int f = 0; f < brush.Faces.Length; f++)
            {
                if (GlobalMapData.MaterialNameToIndex != null && brush.Faces[f].MaterialName != null)
                {
                    if (GlobalMapData.MaterialNameToIndex.TryGetValue(brush.Faces[f].MaterialName, out int surf))
                        brush.Faces[f].Surface = surf;
                    else
                        brush.Faces[f].Surface = GlobalMapData.MaterialNameToIndex.FirstOrDefault(kv => kv.Key.StartsWith("Dev")).Value;
                }

                if (brush.Faces[f].TScaleX == 0) brush.Faces[f].TScaleX = 1;
                if (brush.Faces[f].TScaleY == 0) brush.Faces[f].TScaleY = 1;

                Face face = brush.Faces[f];
                if (face.Plane.HasValue)
                {
                    brush.Faces[f].Normal = Vector3.Normalize(face.Plane.Value.Normal);
                }
                else
                {
                    Vector3 newell = Vector3.Zero;
                    for (int i = 0; i < face.Indices.Length; i += 3)
                    {
                        Vector3 a = brush.Vertices[face.Indices[i]];
                        Vector3 b = brush.Vertices[face.Indices[i + 1]];
                        Vector3 c = brush.Vertices[face.Indices[i + 2]];
                        newell += Vector3.Cross(b - a, c - b);
                    }
                    brush.Faces[f].Normal = Vector3.Normalize(newell);
                }

                UvCalculator.GetUVAxes(brush.Faces[f], out Vector3 uAxis, out Vector3 vAxis);
                Vector3 n = brush.Faces[f].Normal;
                brush.Faces[f].Tangent = Vector3.Normalize(uAxis - n * Vector3.Dot(uAxis, n));
                brush.Faces[f].Binormal = Vector3.Normalize(vAxis - n * Vector3.Dot(vAxis, n));

                brush.IsLightNodeVolume = brush.Faces[f].MaterialName == "tool_lightnodevolume";
                brush.IsTrigger = brush.Faces[f].MaterialName == "tool_trigger" || brush.IsTrigger;
                brush.IsClip = brush.Faces[f].MaterialName == "tool_clip" || brush.IsTrigger;
#if rockwall
                if (face.editorVerts == null)
                {
                    brush.Faces[f].editorVerts = new List<VertexLightmapped>();
                }
                else
                {
                    brush.Faces[f].editorVerts.Clear();
                }
                for (int i = 0; i < face.Indices.Length; i++)
                {
                    brush.Faces[f].editorVerts.Add(new VertexLightmapped(brush.Vertices[face.Indices[i]], face.Normal, uvs.Length < brush.Vertices.Length ? Vector2.One : uvs[face.Indices[i]], Vector2.Zero));
                }
                brush.Faces[f].vertexBuffer = new VertexBuffer(Rockwall3DView.Instance.GraphicsDevice, typeof(VertexLightmapped), brush.Faces[f].editorVerts.Count, BufferUsage.WriteOnly);
                brush.Faces[f].vertexBuffer.SetData(brush.Faces[f].editorVerts.ToArray());
#endif
            }
        }

#if rockwall
        /// <summary>
        /// Duplicates a brush, copying its geometry and properties, and moves it up by one unit.
        /// </summary>
        public static Brush DuplicateBrush(Brush brush, bool offset = true)
        {
            Brush b = CreateBrush(brush.Width, brush.Height, brush.Length);
            b.Position = brush.Position;
            b.IsClip = brush.IsClip;
            b.Vertices = new Vector3[brush.Vertices.Length];
            b.Faces = new Face[brush.Faces.Length];
            b.UVs = new Vector2[brush.UVs.Length];
            for (int i = 0; i < brush.Vertices.Length; i++)
            {
                b.Vertices[i] = brush.Vertices[i];
                b.UVs[i] = brush.UVs[i];
            }
            for (int i = 0; i < brush.Faces.Length; i++)
            {
                b.Faces[i].Decals = brush.Faces[i].Decals;
                b.Faces[i].Drawn = brush.Faces[i].Drawn;
                b.Faces[i].LuxelScale = brush.Faces[i].LuxelScale;
                b.Faces[i].TScaleX = brush.Faces[i].TScaleX;
                b.Faces[i].TScaleY = brush.Faces[i].TScaleY;
                b.Faces[i].TOffX = brush.Faces[i].TOffX;
                b.Faces[i].TOffY = brush.Faces[i].TOffY;
                b.Faces[i].UvRotation = brush.Faces[i].UvRotation;
                b.Faces[i].UvProjectionMode = brush.Faces[i].UvProjectionMode;
                b.Faces[i].Normal = brush.Faces[i].Normal;
                b.Faces[i].Surface = brush.Faces[i].Surface;
                b.Faces[i].Indices = brush.Faces[i].Indices;
                b.Faces[i].MaterialName = brush.Faces[i].MaterialName;
            }
            if (offset) b.Position += Vector3.Up;
            RebuildBrush(ref b);
            return b;
        }
#endif

        /// <summary>
        /// Calculates UVs for a brush using its geometry and face properties.
        /// </summary>
        public static Vector2[] CreateUVs(Brush brush)
        {
            Vector2[] uvs = UvCalculator.CalculateUVs(brush);
            brush.UVs = uvs;
            return uvs;
        }
#if rockwall
        /// <summary>
        /// Moves all vertices of a face by a given amount, updating all coincident vertices.
        /// </summary>
        public static void MoveFace(int brushNum, int faceNum, Vector3 moveAmount)
        {
            Brush brush = MapTools.Brushes[brushNum];

            if (!brush.Faces[faceNum].Plane.HasValue)
            {
                return;
            }

            Plane plane = brush.Faces[faceNum].Plane.Value;

            var oldPlane = plane;

            // Project movement onto plane normal
            float distance = Vector3.Dot(moveAmount, Vector3.Normalize(plane.Normal));

            MoveFace(ref brush, faceNum, distance);

            MapTools.Brushes[brushNum] = brush;
        }
        public static void MoveFace(ref Brush brush, int faceNum, Vector3 moveAmount)
        {
            if (!brush.Faces[faceNum].Plane.HasValue)
            {
                return;
            }

            Plane plane = brush.Faces[faceNum].Plane.Value;

            var oldPlane = plane;

            // Project movement onto plane normal
            float distance = Vector3.Dot(moveAmount, Vector3.Normalize(plane.Normal));

            MoveFace(ref brush, faceNum, distance);
        }
        public static void MoveFace(int b, int faceNum, float delta) => MoveFace(ref MapTools.Brushes[b], faceNum, delta);
        public static float SnapMoveDistanceForFace(Brush brush, int faceNum, float moveDistance)
        {
            var face = brush.Faces[faceNum];
            if (!face.Plane.HasValue) return moveDistance;

            bool isIncidentEdge(int v1, int v2)
            {
                Vector3 p1 = brush.Vertices[v1];
                Vector3 p2 = brush.Vertices[v2];
                bool v1OnFace = face.Indices.Any(i => Vector3.DistanceSquared(brush.Vertices[i], p1) < 0.001f);
                bool v2OnFace = face.Indices.Any(i => Vector3.DistanceSquared(brush.Vertices[i], p2) < 0.001f);
                return v1OnFace ^ v2OnFace;
            }

            var edges = OtherMath.GetUniqueEdges(brush);
            var moveDirection = face.Normal;
            var snappedMoveDistance = float.MaxValue;
            bool flip = moveDistance < 0;

            foreach (var edge in edges)
            {
                if (!isIncidentEdge(edge.Item1, edge.Item2)) continue;

                Vector3 p1 = brush.Vertices[edge.Item1];
                Vector3 p2 = brush.Vertices[edge.Item2];
                bool firstOnFace = face.Indices.Any(i => Vector3.DistanceSquared(brush.Vertices[i], p1) < 0.001f);
                var v1 = firstOnFace ? p2 : p1;
                var v2 = firstOnFace ? p1 : p2;

                if (flip)
                {
                    v1 = firstOnFace ? p1 : p2;
                    v2 = firstOnFace ? p2 : p1;
                }

                var edir = v2 - v1;
                if (edir.LengthSquared() <= 0.0001f) continue;

                var edgeDirection = Vector3.Normalize(edir);
                var distanceOnEdge = moveDistance / Vector3.Dot(edgeDirection, moveDirection);
                var ray = new Ray(v1, edgeDirection);

                var snappedDistanceOnEdge = OtherMath.SnapToGridPlane(ray, distanceOnEdge);
                var snappedMoveDistanceForEdge = snappedDistanceOnEdge * Vector3.Dot(edgeDirection, moveDirection);

                if (float.Abs(snappedMoveDistanceForEdge - moveDistance) < float.Abs(snappedMoveDistance - moveDistance))
                {
                    snappedMoveDistance = snappedMoveDistanceForEdge;
                }
            }

            return snappedMoveDistance < float.MaxValue ? snappedMoveDistance : moveDistance;
        }

        public static void ApplyFaceMoveExact(ref Brush b, int faceNum, float exactDistance)
        {
            var brush = b;
            if (!brush.Faces[faceNum].Plane.HasValue) return;

            var oldPlane = brush.Faces[faceNum].Plane.Value;
            var plane = oldPlane;
            plane.D -= exactDistance;
            brush.Faces[faceNum].Plane = plane;

            if (!IsValidBrush(ref brush))
                brush.Faces[faceNum].Plane = oldPlane;

            b = brush;
            RebuildBrush(ref b);
        }

        public static void MoveFace(ref Brush b, int faceNum, float delta)
        {
            float snapped = SnapMoveDistanceForFace(b, faceNum, delta);
            ApplyFaceMoveExact(ref b, faceNum, snapped);
        }

        public static bool IsValidBrush(ref Brush brush)
        {
            if (brush.Faces.Length < 4) return false;

            // Find all vertices by intersecting triples of planes
            var vertices = new List<Vector3>();
            for (int i = 0; i < brush.Faces.Length - 2; i++)
            {
                for (int j = i + 1; j < brush.Faces.Length - 1; j++)
                {
                    for (int k = j + 1; k < brush.Faces.Length; k++)
                    {
                        var v = IntersectThreePlanes(
                            brush.Faces[i].Plane.Value,
                            brush.Faces[j].Plane.Value,
                            brush.Faces[k].Plane.Value);

                        if (!v.HasValue) continue;

                        // Vertex is valid only if it's behind or on every other plane
                        bool valid = true;
                        for (int p = 0; p < brush.Faces.Length; p++)
                        {
                            if (p == i || p == j || p == k) continue;
                            if (brush.Faces[p].Plane.Value.DotCoordinate(v.Value) > 0.001f)
                            {
                                valid = false;
                                break;
                            }
                        }
                        if (valid) vertices.Add(v.Value);
                    }
                }
            }

            // A valid solid needs at least 4 vertices
            if (vertices.Count < 4) return false;

            // Check no two vertices are too close (collapsed edge/face)
            for (int i = 0; i < vertices.Count; i++)
                for (int j = i + 1; j < vertices.Count; j++)
                    if (Vector3.DistanceSquared(vertices[i], vertices[j]) < 0.00001f)
                        return false;

            // Each face must have at least 3 vertices on it
            for (int f = 0; f < brush.Faces.Length; f++)
            {
                int count = 0;
                foreach (var v in vertices)
                    if (MathF.Abs(brush.Faces[f].Plane.Value.DotCoordinate(v)) <= 0.001f)
                        count++;

                if (count < 3) return false;
            }

            return true;
        }
        public static Brush? CreateHullFromFaces(int brushAIdx, int faceAIdx, int brushBIdx, int faceBIdx)
        {
            var brushA = MapTools.Brushes[brushAIdx];
            var brushB = MapTools.Brushes[brushBIdx];

            // Collect unique world-space verts from both faces
            var verts = new List<Vector3>();
            foreach (int vi in brushA.Faces[faceAIdx].Indices)
            {
                var wp = brushA.Vertices[vi] + brushA.Position;
                if (!verts.Any(v => Vector3.DistanceSquared(v, wp) < 0.001f))
                    verts.Add(wp);
            }
            foreach (int vi in brushB.Faces[faceBIdx].Indices)
            {
                var wp = brushB.Vertices[vi] + brushB.Position;
                if (!verts.Any(v => Vector3.DistanceSquared(v, wp) < 0.001f))
                    verts.Add(wp);
            }

            if (verts.Count < 4) return null;

            var hullFaces = new List<Face>();

            // Try every triple of points as a candidate hull plane
            for (int i = 0; i < verts.Count - 2; i++)
                for (int j = i + 1; j < verts.Count - 1; j++)
                    for (int k = j + 1; k < verts.Count; k++)
                    {
                        Vector3 v0 = verts[i], v1 = verts[j], v2 = verts[k];
                        Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
                        if (normal.LengthSquared() < 1e-6f) continue;
                        normal = Vector3.Normalize(normal);

                        // Try both orientations
                        foreach (var n in new[] { normal, -normal })
                        {
                            float d = -Vector3.Dot(n, v0);
                            bool valid = true;

                            // All other points must be on or behind this plane
                            foreach (var v in verts)
                            {
                                if (Vector3.Dot(n, v) + d > 0.001f)
                                {
                                    valid = false;
                                    break;
                                }
                            }

                            if (!valid) continue;

                            // Check not already added
                            bool duplicate = hullFaces.Any(f =>
                                f.Plane.HasValue &&
                                Vector3.DistanceSquared(f.Plane.Value.Normal, n) < 0.001f &&
                                MathF.Abs(f.Plane.Value.D - d) < 0.001f);

                            if (!duplicate)
                            {
                                hullFaces.Add(new Face
                                {
                                    Plane = new Plane(n, d),
                                    Normal = n,
                                    Drawn = true,
                                    MaterialName = brushA.Faces[faceAIdx].MaterialName,
                                    Surface = brushA.Faces[faceAIdx].Surface,
                                    TScaleX = 1,
                                    TScaleY = 1,
                                    LuxelScale = 1f,
                                    editorVerts = new List<VertexLightmapped>(),
                                    Indices = new int[0]
                                });
                            }
                        }
                    }

            if (hullFaces.Count < 4) return null;

            var result = new Brush
            {
                Position = Vector3.Zero,
                Faces = hullFaces.ToArray(),
                Vertices = new Vector3[0],
                UVs = new Vector2[0],
            };

            RebuildBrush(ref result);
            return result.Faces.Length == 0 ? null : result;
        }

        /// <summary>
        /// Moves all vertices at a given index by a given amount, updating all coincident vertices.
        /// </summary>
        public static void MoveVert(int bnum, int v, Vector3 moveAmount)
        {
            Brush brush = MapTools.Brushes[bnum];
            Vector3 vert = brush.Vertices[v];
            List<int> toMove = new List<int>();
            for (int i = 0; i < brush.Vertices.Length; i++)
            {
                if (Vector3.Distance(brush.Vertices[i], vert) > 0.0001f) continue;
                if (!toMove.Contains(i))
                    toMove.Add(i);
            }
            foreach (int move in toMove)
            {
                brush.Vertices[move] += moveAmount;
            }
            brush.Abnormal = true;
            MapTools.Brushes[bnum] = brush;

            RecalculateBrushPlanes(ref MapTools.Brushes[bnum]);

            RebuildBrush(ref MapTools.Brushes[bnum]);
        }
#endif
        /// <summary>
        /// Checks if a brush is "abnormal" (not a perfect box), and outputs its min/max bounds.
        /// </summary>
        public static bool CheckIsAbnormal(Brush brush, out Vector3 max, out Vector3 min)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            foreach (Vector3 vertice in brush.Vertices)
            {
                min = Vector3.Min(vertice + brush.Position, min);
                max = Vector3.Max(vertice + brush.Position, max);
            }
            foreach (Vector3 vertice in brush.Vertices)
            {
                if ((vertice.X + brush.Position.X > min.X && vertice.X + brush.Position.X < max.X) ||
                    (vertice.Y + brush.Position.Y > min.Y && vertice.Y + brush.Position.Y < max.Y) ||
                    (vertice.Z + brush.Position.Z > min.Z && vertice.Z + brush.Position.Z < max.Z)) return true;
            }
            return false;
        }
#if rockwall
        /// <summary>
        /// Determines if a point is inside a brush.
        /// </summary>
        public static bool PointInsideBrush(Vector3 point, int brush, ref Brush[] brushes)
        {
            for (int i = 0; i < brushes[brush].Faces.Length; i++)
            {
                if (Vector3.Dot(point - brushes[brush].Vertices[brushes[brush].Faces[i].Indices[0]], brushes[brush].Faces[i].Normal) > 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Intersects a ray with a brush, returning the intersection point if any.
        /// </summary>
        public static Vector3? RayVsBrush(ref Ray ray, int brush, ref Brush[] brushes, ref BoundingBox[] bounds)
        {
            Vector3? intersection = null;
            float? result = null;
            BoundingBox box = bounds[brush];
            result = ray.Intersects(box);
            if (result != null && brushes[brush].Abnormal)
            {
                float min = float.MaxValue;
                for (int i = 0; i < brushes[brush].Faces.Length; i++)
                {
                    float distance = RayVsFace(ref ray, brushes[brush], brushes[brush].Faces[i]);
                    if (distance > 0 && distance < min) min = distance;
                }
                if (min < float.MaxValue && min > 0)
                {
                    result = min;
                }
                else
                {
                    result = null;
                }
            }
            if (result != null)
                intersection = ray.Direction * result + ray.Position;
            return intersection;
        }

        /// <summary>
        /// Intersects a ray with a face of a brush, returning the closest intersection distance.
        /// </summary>
        public static float RayVsFace(ref Ray ray, Brush brush, Face face)
        {
            if (face.Indices.Length > 3)
            {
                return Math.Max(
                    IntersectRayTriangle(brush.Vertices[face.Indices[0]] + brush.Position, brush.Vertices[face.Indices[1]] + brush.Position, brush.Vertices[face.Indices[2]] + brush.Position, ray),
                    IntersectRayTriangle(brush.Vertices[face.Indices[3]] + brush.Position, brush.Vertices[face.Indices[4]] + brush.Position, brush.Vertices[face.Indices[5]] + brush.Position, ray)
                );
            }
            else
            {
                return IntersectRayTriangle(brush.Vertices[face.Indices[0]] + brush.Position, brush.Vertices[face.Indices[1]] + brush.Position, brush.Vertices[face.Indices[2]] + brush.Position, ray);
            }
        }

        /// <summary>
        /// Creates a quaternion from a forward and up vector (like Unity's Quaternion.LookRotation).
        /// </summary>
        private static Quaternion QuaternionLookRotation(Vector3 forward, Vector3 up)
        {
            forward.Normalize();
            Vector3 vector = Vector3.Normalize(forward);
            Vector3 vector2 = Vector3.Normalize(Vector3.Cross(up, vector));
            Vector3 vector3 = Vector3.Cross(vector, vector2);
            var m00 = vector2.X;
            var m01 = vector2.Y;
            var m02 = vector2.Z;
            var m10 = vector3.X;
            var m11 = vector3.Y;
            var m12 = vector3.Z;
            var m20 = vector.X;
            var m21 = vector.Y;
            var m22 = vector.Z;
            float num8 = (m00 + m11) + m22;
            var quaternion = new Quaternion();
            if (num8 > 0f)
            {
                var num = (float)Math.Sqrt(num8 + 1f);
                quaternion.W = num * 0.5f;
                num = 0.5f / num;
                quaternion.X = (m12 - m21) * num;
                quaternion.Y = (m20 - m02) * num;
                quaternion.Z = (m01 - m10) * num;
                return quaternion;
            }
            if ((m00 >= m11) && (m00 >= m22))
            {
                var num7 = (float)Math.Sqrt(((1f + m00) - m11) - m22);
                var num4 = 0.5f / num7;
                quaternion.X = 0.5f * num7;
                quaternion.Y = (m01 + m10) * num4;
                quaternion.Z = (m02 + m20) * num4;
                quaternion.W = (m12 - m21) * num4;
                return quaternion;
            }
            if (m11 > m22)
            {
                var num6 = (float)Math.Sqrt(((1f + m11) - m00) - m22);
                var num3 = 0.5f / num6;
                quaternion.X = (m10 + m01) * num3;
                quaternion.Y = 0.5f * num6;
                quaternion.Z = (m21 + m12) * num3;
                quaternion.W = (m20 - m02) * num3;
                return quaternion;
            }
            var num5 = (float)Math.Sqrt(((1f + m22) - m00) - m11);
            var num2 = 0.5f / num5;
            quaternion.X = (m20 + m02) * num2;
            quaternion.Y = (m21 + m12) * num2;
            quaternion.Z = 0.5f * num5;
            quaternion.W = (m01 - m10) * num2;
            return quaternion;
        }

        const float kEpsilon = 0.000001f;

        /// <summary>
        /// Ray-versus-triangle intersection test using the Möller–Trumbore algorithm.
        /// Returns the distance along the ray to the intersection, or NegativeInfinity if no intersection.
        /// </summary>
        public static float IntersectRayTriangle(Vector3 v0, Vector3 v1, Vector3 v2, Ray ray)
        {
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.Direction, e2);
            float a = Vector3.Dot(e1, h);
            if ((a > -kEpsilon) && (a < kEpsilon))
            {
                return float.NegativeInfinity;
            }
            float f = 1.0f / a;
            Vector3 s = ray.Position - v0;
            float u = f * Vector3.Dot(s, h);
            if ((u < 0.0f) || (u > 1.0f))
            {
                return float.NegativeInfinity;
            }
            Vector3 q = Vector3.Cross(s, e1);
            float v = f * Vector3.Dot(ray.Direction, q);
            if ((v < 0.0f) || (u + v > 1.0f))
            {
                return float.NegativeInfinity;
            }
            float t = f * Vector3.Dot(e2, q);
            if (t > kEpsilon)
            {
                return t;
            }
            else
            {
                return float.NegativeInfinity;
            }
        }

        /// <summary>
        /// Returns true if p1 and p2 are on the same side of the line ab.
        /// </summary>
        static bool SameSide(Vector3 p1, Vector3 p2, Vector3 a, Vector3 b)
        {
            var cp1 = Vector3.Cross(b - a, p1 - a);
            var cp2 = Vector3.Cross(b - a, p2 - a);
            return (Vector3.Dot(cp1, cp2) >= 0);
        }

        /// <summary>
        /// Returns true if point p is inside the triangle abc.
        /// </summary>
        static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            return (SameSide(p, a, b, c) && SameSide(p, b, a, c) && SameSide(p, c, a, b));
        }

        /// <summary>
        /// Projects a vector onto a plane defined by its normal.
        /// </summary>
        public static Vector3 Project(Vector3 vector, Vector3 planeNormal)
        {
            float sqrMag = Vector3.Dot(planeNormal, planeNormal);
            if (sqrMag < float.Epsilon)
                return vector;
            else
            {
                var dot = Vector3.Dot(vector, planeNormal);
                return new Vector3(vector.X - planeNormal.X * dot / sqrMag,
                    vector.Y - planeNormal.Y * dot / sqrMag,
                    vector.Z - planeNormal.Z * dot / sqrMag);
            }
        }
        public static bool SplitBrush(Brush brush, Plane splitPlane, out Brush? front, out Brush? back, int sourceFaceHint = 0)
        {
            front = null;
            back = null;

            bool anyFront = false, anyBack = false;
            foreach (var v in brush.Vertices)
            {
                float d = splitPlane.DotCoordinate(v + brush.Position);
                if (d > 0.001f) anyFront = true;
                if (d < -0.001f) anyBack = true;
            }

            if (!anyFront || !anyBack) return false;

            float positionOffset = Vector3.Dot(splitPlane.Normal, brush.Position);
            Plane localSplitPlane = new Plane(splitPlane.Normal, splitPlane.D + positionOffset);
            Plane localFlippedPlane = new Plane(-localSplitPlane.Normal, -localSplitPlane.D);

            bool PlanesEqual(Plane a, Plane b)
            {
                // Normalize both before comparing since D scale depends on normal length
                float la = a.Normal.Length();
                float lb = b.Normal.Length();
                if (la < 1e-6f || lb < 1e-6f) return false;

                Vector3 na = a.Normal / la;
                Vector3 nb = b.Normal / lb;
                float da = a.D / la;
                float db = b.D / lb;

                // Check same direction
                if (Vector3.Distance(na, nb) < 0.01f && MathF.Abs(da - db) < 0.01f) return true;
                // Check opposite direction (flipped plane)
                if (Vector3.Distance(na, -nb) < 0.01f && MathF.Abs(da + db) < 0.01f) return true;

                return false;
            }

            // Front brush gets all original planes + the flipped split plane as cap
            // (the flipped plane cuts off the front piece on the split side)
            Brush frontBrush = DeepCopyBrush(brush);
            frontBrush.GroupingID = null;
            var frontFaces = frontBrush.Faces.ToList();
            if (!frontFaces.Any(f => f.Plane.HasValue && PlanesEqual(f.Plane.Value, localFlippedPlane)))
            {
                var src = brush.Faces[sourceFaceHint];
                frontFaces.Add(new Face
                {
                    Plane = localFlippedPlane,
                    Normal = Vector3.Normalize(localFlippedPlane.Normal),
                    Drawn = true,
                    editorVerts = new List<VertexLightmapped>(),
                    MaterialName = src.MaterialName,
                    Surface = src.Surface,
                    TScaleX = src.TScaleX,
                    TScaleY = src.TScaleY,
                    UvRotation = src.UvRotation,
                    UvProjectionMode = src.UvProjectionMode,
                    LuxelScale = src.LuxelScale,
                    Indices = new int[0]
                });
            }
            frontBrush.Faces = frontFaces.ToArray();

            // Back brush gets all original planes + the split plane as cap
            Brush backBrush = DeepCopyBrush(brush);
            backBrush.GroupingID = null;
            var backFaces = backBrush.Faces.ToList();
            if (!backFaces.Any(f => f.Plane.HasValue && PlanesEqual(f.Plane.Value, localSplitPlane)))
            {
                var src = brush.Faces[sourceFaceHint];
                backFaces.Add(new Face
                {
                    Plane = localSplitPlane,
                    Normal = Vector3.Normalize(localSplitPlane.Normal),
                    Drawn = true,
                    editorVerts = new List<VertexLightmapped>(),
                    MaterialName = src.MaterialName,
                    Surface = src.Surface,
                    TScaleX = src.TScaleX,
                    TScaleY = src.TScaleY,
                    UvRotation = src.UvRotation,
                    UvProjectionMode = src.UvProjectionMode,
                    LuxelScale = src.LuxelScale,
                    Indices = new int[0]
                });
            }
            backBrush.Faces = backFaces.ToArray();

            RebuildBrush(ref frontBrush);
            RebuildBrush(ref backBrush);

            front = frontBrush;
            back = backBrush;
            return true;
        }

        /// <summary>
        /// Deep copies a brush's planes and face metadata, without carrying over stale geometry.
        /// </summary>
        private static Brush DeepCopyBrush(Brush src)
        {
            Face[] newFaces = new Face[src.Faces.Length];
            for (int i = 0; i < src.Faces.Length; i++)
            {
                newFaces[i] = new Face
                {
                    Plane = src.Faces[i].Plane,
                    Normal = src.Faces[i].Normal,
                    Drawn = src.Faces[i].Drawn,
                    MaterialName = src.Faces[i].MaterialName,
                    Surface = src.Faces[i].Surface,
                    LuxelScale = src.Faces[i].LuxelScale,
                    TScaleX = src.Faces[i].TScaleX,
                    TScaleY = src.Faces[i].TScaleY,
                    TOffX = src.Faces[i].TOffX,
                    TOffY = src.Faces[i].TOffY,
                    UvRotation = src.Faces[i].UvRotation,
                    UvProjectionMode = src.Faces[i].UvProjectionMode,
                    editorVerts = new List<VertexLightmapped>(),
                    Indices = new int[0]
                };
            }
            return new Brush
            {
                Position = src.Position,
                Faces = newFaces,
                // geometry gets rebuilt by RebuildBrush
                Vertices = new Vector3[0],
                UVs = new Vector2[0],
                isUsedForTerrain = src.isUsedForTerrain
            };
        }
#endif
        private static (Vector3[] normals, Vector3[] tangents, float[] handedness)
            ComputeNormalsAndTangents(List<Vector3> vertPos, List<short> triangles, Vector2[] uvs)
        {
            int count = vertPos.Count;
            var normals = new Vector3[count];
            var tanAccum = new Vector3[count];
            var bitAccum = new Vector3[count];

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];

                Vector3 p0 = vertPos[i0], p1 = vertPos[i1], p2 = vertPos[i2];
                Vector3 e1 = p1 - p0, e2 = p2 - p0;

                // Face normal
                Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(e1, e2));
                normals[i0] += faceNormal;
                normals[i1] += faceNormal;
                normals[i2] += faceNormal;

                // Tangent / bitangent from UV gradients
                Vector2 dUV1 = uvs[i1] - uvs[i0];
                Vector2 dUV2 = uvs[i2] - uvs[i0];
                float det = dUV1.X * dUV2.Y - dUV2.X * dUV1.Y;
                if (MathF.Abs(det) < 1e-10f) continue;

                float inv = 1f / det;
                Vector3 T = (e1 * dUV2.Y - e2 * dUV1.Y) * inv;
                Vector3 B = (e2 * dUV1.X - e1 * dUV2.X) * inv;

                tanAccum[i0] += T; tanAccum[i1] += T; tanAccum[i2] += T;
                bitAccum[i0] += B; bitAccum[i1] += B; bitAccum[i2] += B;
            }

            var tangents = new Vector3[count];
            var handedness = new float[count];

            for (int v = 0; v < count; v++)
            {
                Vector3 N = Vector3.Normalize(normals[v]);
                normals[v] = N;

                Vector3 T = Vector3.Normalize(tanAccum[v] - N * Vector3.Dot(N, tanAccum[v]));
                tangents[v] = T;

                handedness[v] = MathF.Sign(Vector3.Dot(Vector3.Cross(N, T), bitAccum[v]));
                if (handedness[v] == 0f) handedness[v] = 1f;
            }

            return (normals, tangents, handedness);
        }

        public static void UpdateTerrain(ref Terrain terrain)
        {
            List<Vector3> vertPos = new List<Vector3>();
            List<Vector3> vertTex = new List<Vector3>();
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);

            for (int v = 0; v < terrain.Vertices.Length; v++)
            {
                vertPos.Add(terrain.Vertices[v].Position);
                vertTex.Add(terrain.Vertices[v].TextureCoordinate);
                min = Vector3.Min(min, terrain.Vertices[v].Position);
                max = Vector3.Max(max, terrain.Vertices[v].Position);
            }

            var uvs = UvCalculator.CalculateUVs(vertPos.ToArray(), terrain.Triangles, 4f, projectionNormal: terrain.SourceNormal);
            var triList = new List<short>(terrain.Triangles);
            var (normals, tangents, handedness) = ComputeNormalsAndTangents(vertPos, triList, uvs);

            List<TerrainVertex> vertices = new List<TerrainVertex>();
            for (int v = 0; v < vertPos.Count; v++)
                vertices.Add(new TerrainVertex(vertPos[v], Vector2.Zero, normals[v],
                                               new Vector3(uvs[v], vertTex[v].Z), tangents[v], handedness[v]));

            terrain.Vertices = vertices.ToArray();
            terrain.Bounds = new BoundingBox(min, max);

            if (terrain.SurfaceName == null)
            {
                terrain.SurfaceName = GlobalMapData.LoadedMaterials[terrain.Surface].Name;
                terrain.BlendedSurfaceName = GlobalMapData.LoadedMaterials[terrain.BlendedSurface].Name;
            }

            terrain.Surface = GlobalMapData.MaterialNameToIndex[terrain.SurfaceName];
            terrain.BlendedSurface = GlobalMapData.MaterialNameToIndex[terrain.BlendedSurfaceName];
        }
    }
    public static class UvCalculator
    {
        public static Vector2[] CalculateUVs(Brush brush)
        {
            int vertCount = brush.Vertices.Length;
            var uvs = new Vector2[vertCount];
            var lmUvs = new Vector2[vertCount];

            foreach (var face in brush.Faces)
            {
                if (face.Indices == null || face.Indices.Length == 0) continue;

                GetUVAxes(face, out Vector3 uAxis, out Vector3 vAxis);

                foreach (int vi in face.Indices)
                {
                    Vector3 worldPos = brush.Vertices[vi] + brush.Position;

                    float u = Vector3.Dot(worldPos, uAxis);
                    float v = Vector3.Dot(worldPos, vAxis);

                    float sx = face.TScaleX;
                    float sy = face.TScaleY;
                    u /= sx;
                    v /= sy;

                    Vector2 texSize = GetTextureSize(face);
                    float texelsPerUnit = GetTexelsPerUnit(face);
                    u /= texSize.X / texelsPerUnit;
                    v /= texSize.Y / texelsPerUnit;

                    u += face.TOffX / texSize.X;
                    v += face.TOffY / texSize.Y;

                    uvs[vi] = new Vector2(u, v);

                    float luxel = 512f;
                    lmUvs[vi] = new Vector2(
                        Vector3.Dot(worldPos, uAxis) / luxel,
                        Vector3.Dot(worldPos, vAxis) / luxel);
                }
            }

            brush.LightmapUVs = lmUvs;

            return uvs;
        }

        public static Vector2[] CalculateUVs(Vector3[] vertices, short[] triangles,
                                              float scale, Vector3? offset = null,
                                              Vector3? projectionNormal = null)
        {
            var uvs = new Vector2[vertices.Length];

            if (projectionNormal.HasValue)
            {
                GetAxialAxes(projectionNormal.Value, out Vector3 uAxis, out Vector3 vAxis);
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 pos = vertices[i] + (offset ?? Vector3.Zero);
                    uvs[i] = ProjectLegacy(pos, uAxis, vAxis, scale);
                }
                return uvs;
            }

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];

                // TEMP GUARD: short[] indices can wrap negative once vertex count > 32767
                // (e.g. high-power terrain). Skip instead of crashing until triangles is widened to int[].
                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                {
                    continue;
                }

                Vector3 v0 = vertices[i0] + (offset ?? Vector3.Zero);
                Vector3 v1 = vertices[i1] + (offset ?? Vector3.Zero);
                Vector3 v2 = vertices[i2] + (offset ?? Vector3.Zero);

                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
                if (normal.LengthSquared() < 1e-10f) continue;
                normal = Vector3.Normalize(normal);

                GetAxialAxes(normal, out Vector3 uAxis, out Vector3 vAxis);

                uvs[i0] = ProjectLegacy(vertices[i0] + (offset ?? Vector3.Zero), uAxis, vAxis, scale);
                uvs[i1] = ProjectLegacy(vertices[i1] + (offset ?? Vector3.Zero), uAxis, vAxis, scale);
                uvs[i2] = ProjectLegacy(vertices[i2] + (offset ?? Vector3.Zero), uAxis, vAxis, scale);
            }
            return uvs;
        }

        public enum JustifyMode { Top, Bottom, Left, Right, Center, Fit }

        public static void Justify(ref Face face, Brush brush, JustifyMode mode)
        {
            GetUVAxes(face, out Vector3 uAxis, out Vector3 vAxis);

            float uMin = float.MaxValue, uMax = float.MinValue;
            float vMin = float.MaxValue, vMax = float.MinValue;

            float sx = MathF.Abs(face.TScaleX);
            float sy = MathF.Abs(face.TScaleY);
            Vector2 texSize = GetTextureSize(face);
            float texelsPerUnit = GetTexelsPerUnit(face);

            foreach (int vi in face.Indices)
            {
                Vector3 worldPos = brush.Vertices[vi] + brush.Position;

                // Project without offset/scale so we're working in raw texel space
                float u = Vector3.Dot(worldPos, uAxis) / (sx * (texSize.X / texelsPerUnit));
                float v = Vector3.Dot(worldPos, vAxis) / (sy * (texSize.Y / texelsPerUnit));

                if (u < uMin) uMin = u;
                if (u > uMax) uMax = u;
                if (v < vMin) vMin = v;
                if (v > vMax) vMax = v;
            }

            float uExtent = uMax - uMin;
            float vExtent = vMax - vMin;

            switch (mode)
            {
                case JustifyMode.Left:
                    face.TOffX = -uMin * texSize.X;
                    break;
                case JustifyMode.Right:
                    face.TOffX = -(uMin + uExtent) * texSize.X;
                    break;
                case JustifyMode.Top:
                    face.TOffY = -vMin * texSize.Y;
                    break;
                case JustifyMode.Bottom:
                    face.TOffY = -(vMin + vExtent) * texSize.Y;
                    break;
                case JustifyMode.Center:
                    face.TOffX = -(uMin + uExtent * 0.5f) * texSize.X;
                    face.TOffY = -(vMin + vExtent * 0.5f) * texSize.Y;
                    break;
                case JustifyMode.Fit:
                    // Scale so the face polygon fills exactly one texture tile.
                    // Preserve flip sign.
                    float signX = face.TScaleX < 0 ? -1f : 1f;
                    float signY = face.TScaleY < 0 ? -1f : 1f;
                    face.TScaleX = signX * (uExtent > 0f ? sx * uExtent : sx);
                    face.TScaleY = signY * (vExtent > 0f ? sy * vExtent : sy);
                    face.TOffX = -uMin * texSize.X;
                    face.TOffY = -vMin * texSize.Y;
                    break;
            }
        }

        public static void GetUVAxes(Face face, out Vector3 uAxis, out Vector3 vAxis)
        {
            Vector3 normal = Vector3.Normalize(face.Normal);
            GetAxialAxes(normal, out uAxis, out vAxis);

            if (face.UvProjectionMode == UVProjectionMode.Face)
            {
                // Project axes onto the face plane so they're always parallel to it.
                // v' = normalize(v - (v·n)n)
                uAxis = ProjectOntoPlane(uAxis, normal);
                vAxis = ProjectOntoPlane(vAxis, normal);
            }

            // Apply rotation inside the face plane
            if (MathF.Abs(face.UvRotation) > 0.001f)
                RotateAxesAroundNormal(normal, face.UvRotation, ref uAxis, ref vAxis);
        }
        public static void SetUVAxes(ref Face face, Vector3 desiredU, Vector3 desiredV)
        {
            Vector3 normal = Vector3.Normalize(face.Normal);

            GetAxialAxes(normal, out Vector3 baseU, out Vector3 baseV);

            if (face.UvProjectionMode == UVProjectionMode.Face)
            {
                baseU = ProjectOntoPlane(baseU, normal);
                baseV = ProjectOntoPlane(baseV, normal);
            }

            desiredU = Vector3.Normalize(desiredU);
            desiredV = Vector3.Normalize(desiredV);

            float dot = Vector3.Dot(baseU, desiredU);
            float cross = Vector3.Dot(Vector3.Cross(baseU, desiredU), normal);
            float angle = MathF.Atan2(cross, dot);

            face.UvRotation = -MathHelper.ToDegrees(angle);

            Vector3 impliedV = baseV;
            RotateAxesAroundNormal(normal, face.UvRotation, ref baseU, ref impliedV);
            if (Vector3.Dot(impliedV, desiredV) < 0f)
            {
                face.TScaleY = -MathF.Abs(face.TScaleY);
            }
        }
        private static void GetAxialAxes(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            float ax = MathF.Abs(normal.X);
            float ay = MathF.Abs(normal.Y);
            float az = MathF.Abs(normal.Z);

            if (az >= ax && az >= ay)        // floor / ceiling
            {
                u = Vector3.UnitX;
                v = -Vector3.UnitY;
            }
            else if (ax >= ay)               // east / west wall
            {
                u = Vector3.UnitZ;
                v = -Vector3.UnitY;
            }
            else                             // north / south wall
            {
                u = Vector3.UnitX;
                v = -Vector3.UnitZ;
            }
        }

        private static Vector3 ProjectOntoPlane(Vector3 axis, Vector3 normal)
        {
            Vector3 projected = axis - normal * Vector3.Dot(axis, normal);
            float lenSq = projected.LengthSquared();
            return lenSq > 1e-8f ? projected / MathF.Sqrt(lenSq) : axis;
        }

        private static void RotateAxesAroundNormal(Vector3 normal, float degrees,
                                                    ref Vector3 u, ref Vector3 v)
        {
            float rad = degrees * (MathF.PI / 180f);
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);

            Vector3 newU = cos * u - sin * v;
            Vector3 newV = sin * u + cos * v;

            u = Vector3.Normalize(newU);
            v = Vector3.Normalize(newV);
        }

        private static Vector2 GetTextureSize(Face face)
        {
            if (GlobalMapData.LoadedMaterials == null) return new Vector2(512f, 512f);
            if (face.MaterialName == null) return new Vector2(512f, 512f);
            if (!GlobalMapData.MaterialNameToIndex.TryGetValue(face.MaterialName, out int idx))
                return new Vector2(512f, 512f);

            var tex = GlobalMapData.LoadedMaterials[idx].Texture;
            if (tex == null) return new Vector2(512f, 512f);
            return new Vector2(tex.Width, tex.Height);
        }
        private static float GetTexelsPerUnit(Face face)
        {
            if (GlobalMapData.LoadedMaterials == null) return 512f;
            if (face.MaterialName == null) return 512f;
            if (!GlobalMapData.MaterialNameToIndex.TryGetValue(face.MaterialName, out int idx))
                return 512f;

            return GlobalMapData.LoadedMaterials[idx].EffectiveTexelsPerUnit;
        }
        private static Vector2 ProjectLegacy(Vector3 pos, Vector3 u, Vector3 v, float scale)
        {
            return new Vector2(Vector3.Dot(pos, u), Vector3.Dot(pos, v)) / scale;
        }
    }

    public static class TextureSeamSolver
    {
        private static float GetTexelsPerUnit(Face face)
        {
            if (GlobalMapData.LoadedMaterials == null) return 512f;
            if (face.MaterialName == null) return 512f;
            if (!GlobalMapData.MaterialNameToIndex.TryGetValue(face.MaterialName, out int idx))
                return 512f;

            return GlobalMapData.LoadedMaterials[idx].EffectiveTexelsPerUnit;
        }
        public static void Solve(Brush srcBrush, int srcFaceIdx, ref Brush dstBrush, int dstFaceIdx)
        {
            var srcFace = srcBrush.Faces[srcFaceIdx];
            ref var dstFace = ref dstBrush.Faces[dstFaceIdx];

            dstFace.UvProjectionMode = srcFace.UvProjectionMode;
            dstFace.TScaleX = srcFace.TScaleX;
            dstFace.TScaleY = srcFace.TScaleY;
            dstFace.UvRotation = srcFace.UvRotation;
            dstFace.LuxelScale = srcFace.LuxelScale;
            dstFace.MaterialName = srcFace.MaterialName;
            dstFace.Surface = srcFace.Surface;

            UvCalculator.GetUVAxes(srcFace, out Vector3 srcU, out Vector3 srcV);

            Vector3 srcNormal = Vector3.Normalize(srcFace.Normal);
            Vector3 dstNormal = Vector3.Normalize(dstFace.Normal);

            Vector3 rotatedU = RotateVectorToPlane(srcU, srcNormal, dstNormal);
            Vector3 rotatedV = RotateVectorToPlane(srcV, srcNormal, dstNormal);

            if (Vector3.Dot(rotatedV, srcV) < 0f)
            {
                rotatedV = -rotatedV;
            }

            UvCalculator.SetUVAxes(ref dstFace, rotatedU, rotatedV);

            dstFace.TScaleX = srcFace.TScaleX;
            dstFace.TScaleY = srcFace.TScaleY;

            Vector3 anchor = FindAnchorPoint(srcBrush, srcFaceIdx, dstBrush, dstFaceIdx);

            Vector2 texSize = GetTextureSize(srcFace);
            float texelsPerUnit = GetTexelsPerUnit(srcFace);
            float normX = texSize.X / texelsPerUnit;
            float normY = texSize.Y / texelsPerUnit;

            float srcFinalU = Vector3.Dot(anchor, srcU) / (srcFace.TScaleX * normX)
                            + srcFace.TOffX / texSize.X;
            float srcFinalV = Vector3.Dot(anchor, srcV) / (srcFace.TScaleY * normY)
                            + srcFace.TOffY / texSize.Y;

            UvCalculator.GetUVAxes(dstFace, out Vector3 finalDstU, out Vector3 finalDstV);

            float dstRawU = Vector3.Dot(anchor, finalDstU) / (dstFace.TScaleX * normX);
            float dstRawV = Vector3.Dot(anchor, finalDstV) / (dstFace.TScaleY * normY);

            float rawOffX = (srcFinalU - dstRawU) * texSize.X;
            float rawOffY = (srcFinalV - dstRawV) * texSize.Y;

            dstFace.TOffX = ((rawOffX % texSize.X) + texSize.X) % texSize.X;
            dstFace.TOffY = ((rawOffY % texSize.Y) + texSize.Y) % texSize.Y;
        }

        private static Vector3 RotateVectorToPlane(
            Vector3 vec, Vector3 srcNormal, Vector3 dstNormal)
        {
            Vector3 projected = vec - dstNormal * Vector3.Dot(vec, dstNormal);
            if (projected.LengthSquared() > 1e-6f)
            {
                return Vector3.Normalize(projected);
            }

            Vector3 seamLine = Vector3.Cross(srcNormal, dstNormal);
            float seamLen = seamLine.Length();

            if (seamLen > 1e-6f)
            {
                seamLine /= seamLen;
                float cosA = Math.Clamp(Vector3.Dot(srcNormal, dstNormal), -1f, 1f);
                float angle = MathF.Acos(cosA);
                var rot = Matrix.CreateFromAxisAngle(seamLine, angle);
                return Vector3.Normalize(Vector3.TransformNormal(vec, rot));
            }

            return Vector3.Normalize(vec);
        }

        private static Vector3 FindAnchorPoint(
            Brush srcBrush, int srcFaceIdx,
            Brush dstBrush, int dstFaceIdx)
        {
            var srcFace = srcBrush.Faces[srcFaceIdx];
            var dstFace = dstBrush.Faces[dstFaceIdx];

            var srcVerts = UniqueWorldVerts(srcBrush, srcFace);
            var dstVerts = UniqueWorldVerts(dstBrush, dstFace);

            const float eps = 0.01f;
            var shared = new List<Vector3>();

            foreach (var sv in srcVerts)
            {
                foreach (var dv in dstVerts)
                {
                    if (Vector3.DistanceSquared(sv, dv) < eps * eps)
                    {
                        shared.Add((sv + dv) * 0.5f);
                    }
                }
            }

            if (shared.Count >= 2)
            {
                Vector3 mid = Vector3.Zero;
                foreach (var v in shared) mid += v;
                return mid / shared.Count;
            }

            if (shared.Count == 1)
            {
                return shared[0];
            }

            float bestDist = float.MaxValue;
            Vector3 bestSrc = srcVerts[0], bestDst = dstVerts[0];

            foreach (var sv in srcVerts)
            {
                foreach (var dv in dstVerts)
                {
                    float d = Vector3.DistanceSquared(sv, dv);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestSrc = sv;
                        bestDst = dv;
                    }
                }
            }

            return (bestSrc + bestDst) * 0.5f;
        }

        private static List<Vector3> UniqueWorldVerts(Brush brush, Face face)
        {
            var seen = new List<Vector3>();

            foreach (int vi in face.Indices)
            {
                Vector3 wp = brush.Vertices[vi] + brush.Position;
                bool found = false;

                foreach (var s in seen)
                {
                    if (Vector3.DistanceSquared(s, wp) < 1e-6f)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found) seen.Add(wp);
            }

            return seen;
        }

        private static Vector2 GetTextureSize(Face face)
        {
            if (GlobalMapData.LoadedMaterials == null) return new Vector2(512f, 512f);
            if (face.MaterialName == null) return new Vector2(512f, 512f);
            if (!GlobalMapData.MaterialNameToIndex.TryGetValue(face.MaterialName, out int idx))
                return new Vector2(512f, 512f);
            var tex = GlobalMapData.LoadedMaterials[idx].Texture;
            if (tex == null) return new Vector2(512f, 512f);
            return new Vector2(tex.Width, tex.Height);
        }
    }
}
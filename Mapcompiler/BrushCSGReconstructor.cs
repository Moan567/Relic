using MapCompiler.Compilation;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler;

public static class BrushCSGReconstructor
{
    /// <summary>
    /// Compute barycentric coordinates of point p with respect to triangle (a, b, c)
    /// </summary>
    static Vector3 ComputeBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 v0 = b - a;
        Vector3 v1 = c - a;
        Vector3 v2 = p - a;

        float d00 = Vector3.Dot(v0, v0);
        float d01 = Vector3.Dot(v0, v1);
        float d11 = Vector3.Dot(v1, v1);
        float d20 = Vector3.Dot(v2, v0);
        float d21 = Vector3.Dot(v2, v1);

        float denom = d00 * d11 - d01 * d01;
        if (Math.Abs(denom) < 0.0001f)
        {
            // Degenerate triangle, return equal weights
            return new Vector3(0.333f, 0.333f, 0.334f);
        }

        float v = (d11 * d20 - d01 * d21) / denom;
        float w = (d00 * d21 - d01 * d20) / denom;
        float u = 1.0f - v - w;

        return new Vector3(u, v, w);
    }

    /// <summary>
    /// Reconstruct UV coordinates for a vertex by finding its barycentric position
    /// on the original face and interpolating the original UVs
    /// </summary>
    static (Vector2 uv, Vector2 lmpUv) ComputeUVsForVertex(Vector3 worldPos, Face originalFace, Brush originalBrush)
    {
        Vector3 localPos = worldPos - originalBrush.Position;

        var origIndices = originalFace.Indices;
        var origVerts = originalBrush.Vertices;
        var origUVs = originalBrush.UVs;
        var origLMPUVs = originalBrush.LightmapUVs;

        float bestDist = float.MaxValue;
        Vector2 bestUV = Vector2.Zero;
        Vector2 bestLMPUV = Vector2.Zero;

        for (int i = 0; i < origIndices.Length; i += 3)
        {
            Vector3 v0 = origVerts[origIndices[i]];
            Vector3 v1 = origVerts[origIndices[i + 1]];
            Vector3 v2 = origVerts[origIndices[i + 2]];

            Vector2 uv0 = origUVs[origIndices[i]];
            Vector2 uv1 = origUVs[origIndices[i + 1]];
            Vector2 uv2 = origUVs[origIndices[i + 2]];

            Vector2 lmp0 = origLMPUVs[origIndices[i]];
            Vector2 lmp1 = origLMPUVs[origIndices[i + 1]];
            Vector2 lmp2 = origLMPUVs[origIndices[i + 2]];

            Vector3 bary = ComputeBarycentric(localPos, v0, v1, v2);

            if (bary.X >= -0.001f && bary.Y >= -0.001f && bary.Z >= -0.001f)
            {
                return (uv0 * bary.X + uv1 * bary.Y + uv2 * bary.Z,
                        lmp0 * bary.X + lmp1 * bary.Y + lmp2 * bary.Z);
            }

            Vector3 clampedBary = new Vector3(
                Math.Max(0, bary.X),
                Math.Max(0, bary.Y),
                Math.Max(0, bary.Z)
            );
            float sum = clampedBary.X + clampedBary.Y + clampedBary.Z;
            if (sum > 0.001f)
                clampedBary /= sum;

            Vector3 clampedPos = v0 * clampedBary.X + v1 * clampedBary.Y + v2 * clampedBary.Z;
            float dist = Vector3.Distance(localPos, clampedPos);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestUV = uv0 * clampedBary.X + uv1 * clampedBary.Y + uv2 * clampedBary.Z;
                bestLMPUV = lmp0 * clampedBary.X + lmp1 * clampedBary.Y + lmp2 * clampedBary.Z;
            }
        }

        return (bestUV, bestLMPUV);
    }

    // Helper class to store face data before triangulation
    private class PendingFace
    {
        public List<Vector3> Vertices { get; set; } = new List<Vector3>();
        public Face OriginalFace { get; set; }
        public int BrushIndex { get; set; }
        public int LeafIndex { get; set; }
    }

    /// <summary>
    /// Represents an edge in world space
    /// </summary>
    private class WorldEdge
    {
        public Vector3 Start;
        public Vector3 End;
        public int BrushIdx;
        public int FaceIdx;
        public int EdgeIdx; // Index in the vertex list where this edge starts

        public float Length => Vector3.Distance(Start, End);
        public Vector3 Direction => Vector3.Normalize(End - Start);
    }

    /// <summary>
    /// Check if point lies on line segment, return parametric position if true
    /// </summary>
    private static bool GetPointOnSegmentParam(Vector3 point, Vector3 segStart, Vector3 segEnd,
        out float t, float tolerance = 0.1f)
    {
        t = 0;

        Vector3 segVec = segEnd - segStart;
        float segLengthSq = segVec.LengthSquared();

        if (segLengthSq < 0.0001f)
            return false;

        // Calculate parametric position
        Vector3 pointVec = point - segStart;
        t = Vector3.Dot(pointVec, segVec) / segLengthSq;

        // Check if t is within segment bounds (with small tolerance for endpoints)
        const float endpointTolerance = 0.001f;
        if (t < -endpointTolerance || t > 1.0f + endpointTolerance)
            return false;

        // Calculate closest point on segment
        Vector3 closestPoint = segStart + segVec * t;
        float distSq = (point - closestPoint).LengthSquared();

        return distSq < tolerance * tolerance;
    }

    /// <summary>
    /// Merge vertices that are very close together
    /// </summary>
    private static Vector3 SnapVertex(Vector3 vertex, List<Vector3> existingVertices, float tolerance = 0.1f)
    {
        float toleranceSq = tolerance * tolerance;

        foreach (var existing in existingVertices)
        {
            if ((vertex - existing).LengthSquared() < toleranceSq)
                return existing;
        }

        return vertex;
    }

    /// <summary>
    /// Fix T-junctions by collecting all unique vertices along all edges
    /// </summary>
    private static void FixTJunctions(List<PendingFace>[] brushPendingFaces, Brush[] brushes)
    {
        Console.WriteLine("Fixing T-junctions...");

        const float TOLERANCE = 0.01f;
        const float ENDPOINT_THRESHOLD = 0.001f;
        const float CellSize = 4f;
        const float InvCellSize = 1f / CellSize;

        var allEdges = new List<WorldEdge>();

        for (int brushIdx = 0; brushIdx < brushPendingFaces.Length; brushIdx++)
        {
            var brush = brushes[brushIdx];
            var pendingFaces = brushPendingFaces[brushIdx];

            for (int faceIdx = 0; faceIdx < pendingFaces.Count; faceIdx++)
            {
                var face = pendingFaces[faceIdx];
                var verts = face.Vertices;

                for (int edgeIdx = 0; edgeIdx < verts.Count; edgeIdx++)
                {
                    Vector3 start = verts[edgeIdx] + brush.Position;
                    Vector3 end = verts[(edgeIdx + 1) % verts.Count] + brush.Position;

                    allEdges.Add(new WorldEdge
                    {
                        Start = start,
                        End = end,
                        BrushIdx = brushIdx,
                        FaceIdx = faceIdx,
                        EdgeIdx = edgeIdx
                    });
                }
            }
        }

        Console.WriteLine($"Collected {allEdges.Count} edges from all faces");

        (int, int, int) CellOf(Vector3 p) => (
            (int)MathF.Floor(p.X * InvCellSize),
            (int)MathF.Floor(p.Y * InvCellSize),
            (int)MathF.Floor(p.Z * InvCellSize));

        var edgeGrid = new Dictionary<(int, int, int), List<int>>();

        for (int i = 0; i < allEdges.Count; i++)
        {
            var edge = allEdges[i];
            Vector3 emin = Vector3.Min(edge.Start, edge.End) - new Vector3(TOLERANCE);
            Vector3 emax = Vector3.Max(edge.Start, edge.End) + new Vector3(TOLERANCE);

            var cmin = CellOf(emin);
            var cmax = CellOf(emax);

            for (int cx = cmin.Item1; cx <= cmax.Item1; cx++)
            {
                for (int cy = cmin.Item2; cy <= cmax.Item2; cy++)
                {
                    for (int cz = cmin.Item3; cz <= cmax.Item3; cz++)
                    {
                        var key = (cx, cy, cz);
                        if (!edgeGrid.TryGetValue(key, out var list))
                            edgeGrid[key] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }
        }

        var edgeVertexInsertions = new Dictionary<(int brush, int face, int edge), List<(Vector3 pos, float t)>>();
        var candidateSeen = new HashSet<int>();

        for (int i = 0; i < allEdges.Count; i++)
        {
            var edge = allEdges[i];
            var key = (edge.BrushIdx, edge.FaceIdx, edge.EdgeIdx);

            if (!edgeVertexInsertions.TryGetValue(key, out var insertList))
                edgeVertexInsertions[key] = insertList = new List<(Vector3, float)>();

            Vector3 emin = Vector3.Min(edge.Start, edge.End) - new Vector3(TOLERANCE);
            Vector3 emax = Vector3.Max(edge.Start, edge.End) + new Vector3(TOLERANCE);
            var cmin = CellOf(emin);
            var cmax = CellOf(emax);

            candidateSeen.Clear();

            for (int cx = cmin.Item1; cx <= cmax.Item1; cx++)
            {
                for (int cy = cmin.Item2; cy <= cmax.Item2; cy++)
                {
                    for (int cz = cmin.Item3; cz <= cmax.Item3; cz++)
                    {
                        if (!edgeGrid.TryGetValue((cx, cy, cz), out var bucket)) continue;

                        foreach (int j in bucket)
                        {
                            if (j == i) continue;
                            if (!candidateSeen.Add(j)) continue;

                            var otherEdge = allEdges[j];

                            foreach (var vertex in new[] { otherEdge.Start, otherEdge.End })
                            {
                                if (GetPointOnSegmentParam(vertex, edge.Start, edge.End, out float t, TOLERANCE))
                                {
                                    if (t > ENDPOINT_THRESHOLD && t < 1.0f - ENDPOINT_THRESHOLD)
                                    {
                                        Vector3 projectedVertex = edge.Start + (edge.End - edge.Start) * t;

                                        bool isDuplicate = insertList.Any(v =>
                                            Vector3.Distance(v.pos, projectedVertex) < TOLERANCE);

                                        if (!isDuplicate)
                                            insertList.Add((projectedVertex, t));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        int totalVerticesAdded = 0;

        foreach (var kvp in edgeVertexInsertions.OrderByDescending(x => x.Key.edge))
        {
            var (brushIdx, faceIdx, edgeIdx) = kvp.Key;
            var verticesToInsert = kvp.Value;

            if (verticesToInsert.Count == 0)
                continue;

            verticesToInsert = verticesToInsert.OrderByDescending(v => v.t).ToList();

            var face = brushPendingFaces[brushIdx][faceIdx];
            var brush = brushes[brushIdx];

            foreach (var (worldPos, t) in verticesToInsert)
            {
                Vector3 localPos = worldPos - brush.Position;
                face.Vertices.Insert(edgeIdx + 1, localPos);
                totalVerticesAdded++;
            }
        }

        Console.WriteLine($"T-junction fixing complete. Added {totalVerticesAdded} vertices.");

        Console.WriteLine("Cleaning up duplicate vertices...");
        int duplicatesRemoved = 0;

        for (int brushIdx = 0; brushIdx < brushPendingFaces.Length; brushIdx++)
        {
            var brush = brushes[brushIdx];
            var pendingFaces = brushPendingFaces[brushIdx];

            foreach (var face in pendingFaces)
            {
                var cleanedVerts = new List<Vector3>();

                foreach (var vert in face.Vertices)
                {
                    if (cleanedVerts.Count > 0)
                    {
                        Vector3 prev = cleanedVerts[cleanedVerts.Count - 1];
                        if ((vert - prev).LengthSquared() < TOLERANCE * TOLERANCE)
                        {
                            duplicatesRemoved++;
                            continue;
                        }
                    }

                    cleanedVerts.Add(vert);
                }

                if (cleanedVerts.Count > 2)
                {
                    Vector3 first = cleanedVerts[0];
                    Vector3 last = cleanedVerts[cleanedVerts.Count - 1];
                    if ((first - last).LengthSquared() < TOLERANCE * TOLERANCE)
                    {
                        cleanedVerts.RemoveAt(cleanedVerts.Count - 1);
                        duplicatesRemoved++;
                    }
                }

                face.Vertices = cleanedVerts;
            }
        }

        Console.WriteLine($"Removed {duplicatesRemoved} duplicate vertices.");
    }

    /// <summary>
    /// Check if a triangle is an "ear" - a triangle with no other vertices inside it
    /// </summary>
    private static bool IsEar(List<Vector3> polygon, int i0, int i1, int i2, Vector3 normal)
    {
        Vector3 v0 = polygon[i0];
        Vector3 v1 = polygon[i1];
        Vector3 v2 = polygon[i2];

        // Check if triangle is oriented correctly
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 triNormal = Vector3.Cross(edge1, edge2);

        if (Vector3.Dot(triNormal, normal) <= 0)
            return false; // Wrong orientation or degenerate

        // Check if any other vertex is inside this triangle
        for (int i = 0; i < polygon.Count; i++)
        {
            if (i == i0 || i == i1 || i == i2)
                continue;

            if (IsPointInTriangle(polygon[i], v0, v1, v2, normal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Check if a point is inside a triangle (projected onto triangle's plane)
    /// </summary>
    private static bool IsPointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        // Use barycentric coordinates
        Vector3 v0 = b - a;
        Vector3 v1 = c - a;
        Vector3 v2 = p - a;

        float d00 = Vector3.Dot(v0, v0);
        float d01 = Vector3.Dot(v0, v1);
        float d11 = Vector3.Dot(v1, v1);
        float d20 = Vector3.Dot(v2, v0);
        float d21 = Vector3.Dot(v2, v1);

        float denom = d00 * d11 - d01 * d01;
        if (Math.Abs(denom) < 0.0001f)
            return false;

        float v = (d11 * d20 - d01 * d21) / denom;
        float w = (d00 * d21 - d01 * d20) / denom;
        float u = 1.0f - v - w;

        // Point is inside if all barycentric coordinates are positive
        const float epsilon = -0.0001f; // Small negative tolerance for numerical stability
        return (u >= epsilon && v >= epsilon && w >= epsilon);
    }

    /// <summary>
    /// Triangulate a polygon using ear clipping algorithm
    /// Returns list of triangle indices (in groups of 3)
    /// </summary>
    private static List<int> TriangulatePolygon(Vector3[] vertices, Vector3 normal)
    {
        var indices = new List<int>();

        if (vertices.Length < 3)
            return indices;

        if (vertices.Length == 3)
        {
            // Simple triangle
            indices.Add(0);
            indices.Add(1);
            indices.Add(2);
            return indices;
        }

        // Create a list of remaining vertices
        var remaining = new List<int>();
        for (int i = 0; i < vertices.Length; i++)
            remaining.Add(i);

        var vertexList = vertices.ToList();

        int maxIterations = vertices.Length * vertices.Length; // Prevent infinite loops
        int iterations = 0;

        // Ear clipping algorithm
        while (remaining.Count > 3 && iterations < maxIterations)
        {
            iterations++;
            bool earFound = false;

            for (int i = 0; i < remaining.Count; i++)
            {
                int i0 = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int i1 = remaining[i];
                int i2 = remaining[(i + 1) % remaining.Count];

                if (IsEar(vertexList, i0, i1, i2, normal))
                {
                    // Found an ear, add triangle
                    indices.Add(i0);
                    indices.Add(i1);
                    indices.Add(i2);

                    // Remove the ear tip
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
            {
                // Couldn't find an ear, fallback to fan triangulation
                //Console.WriteLine($"Warning: Ear clipping failed for polygon with {remaining.Count} vertices, using fan triangulation");
                break;
            }
        }

        // Add the final triangle
        if (remaining.Count == 3)
        {
            indices.Add(remaining[0]);
            indices.Add(remaining[1]);
            indices.Add(remaining[2]);
        }
        else if (remaining.Count > 3)
        {
            // Fallback: fan triangulation for remaining vertices
            for (int i = 1; i < remaining.Count - 1; i++)
            {
                Vector3 v0 = vertexList[remaining[0]];
                Vector3 v1 = vertexList[remaining[i]];
                Vector3 v2 = vertexList[remaining[i + 1]];

                Vector3 edge1 = v1 - v0;
                Vector3 edge2 = v2 - v0;
                Vector3 triNormal = Vector3.Cross(edge1, edge2);

                if (Vector3.Dot(triNormal, normal) < 0)
                {
                    indices.Add(remaining[i + 1]);
                    indices.Add(remaining[i]);
                    indices.Add(remaining[0]);
                }
                else
                {
                    indices.Add(remaining[0]);
                    indices.Add(remaining[i]);
                    indices.Add(remaining[i + 1]);
                }
            }
        }

        return indices;
    }

    private static void WeldSeamVertices(List<PendingFace>[] brushPendingFaces, Brush[] brushes, float tolerance = 1/512f)
    {
        Console.WriteLine("Welding seam vertices...");

        var refs = new List<(int brushIdx, int faceIdx, int vertIdx, Vector3 worldPos)>();

        for (int brushIdx = 0; brushIdx < brushPendingFaces.Length; brushIdx++)
        {
            var brush = brushes[brushIdx];
            var faces = brushPendingFaces[brushIdx];
            for (int faceIdx = 0; faceIdx < faces.Count; faceIdx++)
            {
                var verts = faces[faceIdx].Vertices;
                for (int vertIdx = 0; vertIdx < verts.Count; vertIdx++)
                    refs.Add((brushIdx, faceIdx, vertIdx, verts[vertIdx] + brush.Position));
            }
        }

        float cell = tolerance;
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) KeyFor(Vector3 p) =>
            ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));

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

        float tolSq = tolerance * tolerance;

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

        var clusterSum = new Dictionary<int, Vector3>();
        var clusterCount = new Dictionary<int, int>();
        for (int i = 0; i < refs.Count; i++)
        {
            int root = Find(i);
            clusterSum.TryGetValue(root, out var sum);
            clusterSum[root] = sum + refs[i].worldPos;
            clusterCount[root] = clusterCount.GetValueOrDefault(root) + 1;
        }

        int welded = 0;
        for (int i = 0; i < refs.Count; i++)
        {
            int root = Find(i);
            if (clusterCount[root] <= 1) continue; // nothing to weld, leave as-is

            Vector3 canonical = clusterSum[root] / clusterCount[root];
            var (brushIdx, faceIdx, vertIdx, _) = refs[i];
            var brush = brushes[brushIdx];

            brushPendingFaces[brushIdx][faceIdx].Vertices[vertIdx] = canonical - brush.Position;
            welded++;
        }

        Console.WriteLine($"Welded {welded} of {refs.Count} vertex references into shared seam positions.");
    }

    /// <summary>
    /// Rebuild all brushes from portal geometry
    /// </summary>
    public static void RebuildBrushesFromPortals(
        ref Brush[] brushes,
        out List<WorkingLeafPoly> leafPolygons,
        out Dictionary<int, int[]> pendingToFinalFace,
        out Dictionary<int, int[]> pendingToBaseVertex,
        Portal[] portals)
    {
        Console.WriteLine("\n--- Rebuilding Brushes from Portal Geometry ---");

        var progressBar = new ProgressBar();
        int progress = 0;

        var brushToPortals = new Dictionary<int, List<(Portal portal, int originalFaceIndex, int portalID, int leafID)>>();

        for (int i = 0; i < brushes.Length; i++)
        {
            brushToPortals[i] = new List<(Portal, int, int, int)>();
        }

        int id = 0;
        var usedPortals = new HashSet<(int, int)>();

        foreach (var portal in portals)
        {
            var frontNode = BSPRoot.Nodes[portal.LeafFront];
            var backNode = BSPRoot.Nodes[portal.LeafBack];

            int openLeaf = -1;
            if (!frontNode.solid && backNode.solid) openLeaf = portal.LeafFront;
            else if (!backNode.solid && frontNode.solid) openLeaf = portal.LeafBack;

            if (openLeaf == -1) continue;

            foreach (var (brushIdx, faceIdx) in portal.brushFaces)
            {
                if (brushIdx < brushes.Length)
                {
                    brushToPortals[brushIdx].Add((portal, faceIdx, id, openLeaf));
                }
            }
            id++;
        }

        var brushPendingFaces = new List<PendingFace>[brushes.Length];
        var workingPolys = new List<WorkingLeafPoly>();

        for (int brushIdx = 0; brushIdx < brushes.Length; brushIdx++)
        {
            brushPendingFaces[brushIdx] = new List<PendingFace>();

            progress++;
            progressBar.Report(progress / (float)(brushes.Length * 2));

            var brush = brushes[brushIdx];

            if (brush.IsClip || brush.IsTrigger || brush.IsLightNodeVolume || brush.IsSkybox || brush.IsClip)
                continue;

            var portalList = brushToPortals[brushIdx];
            if (portalList.Count == 0)
                continue;

            var originalFaces = brush.Faces.ToArray();
            var originalVertices = brush.Vertices.ToArray();

            foreach (var (portal, originalFaceIdx, portalIdx, leafIdx) in portalList)
            {
                if (originalFaceIdx >= originalFaces.Length)
                    continue;

                if (usedPortals.Contains((portalIdx, brushIdx)))
                    continue;

                usedPortals.Add((portalIdx, brushIdx));

                var originalFace = originalFaces[originalFaceIdx];

                var faceVerts = originalFace.Indices.Select(i => originalVertices[i]).ToArray();

                if (!originalFace.Plane.HasValue)
                {
                    originalFace.Plane = new Plane(originalVertices[0], originalFace.Normal);
                }

                if (MathF.Abs(Portalizer.SignedWindingArea(portal.Vertices, portal.Plane)) >
                    MathF.Abs(Portalizer.SignedWindingArea(faceVerts, originalFace.Plane.Value)) + 32)
                    continue;

                var portalVerts = portal.Vertices;
                foreach (var bf in originalFaces)
                {
                    Vector3 pointOnFace = originalVertices[bf.Indices[0]] + brush.Position;
                    Plane facePlane = new Plane(bf.Normal, -Vector3.Dot(bf.Normal, pointOnFace));

                    float maxDist = 0f;
                    foreach (var v in portalVerts)
                        maxDist = MathF.Max(maxDist, MathF.Abs(facePlane.DotCoordinate(v)));
                    if (maxDist < 0.05f) continue;

                    portalVerts = Portalizer.ClipWinding(portalVerts, Portalizer.FlipPlane(facePlane), true);
                    if (portalVerts == null) break;
                }

                if (portalVerts == null || portalVerts.Length < 3)
                    continue;

                var localVertices = portalVerts
                    .Select(v => v - brush.Position)
                    .ToList();

                if (Vector3.Dot(portal.Plane.Normal, originalFace.Normal) < 0)
                    localVertices.Reverse();

                var pendingFace = new PendingFace
                {
                    Vertices = localVertices,
                    OriginalFace = originalFace,
                    BrushIndex = brushIdx,
                    LeafIndex = leafIdx
                };

                brushPendingFaces[brushIdx].Add(pendingFace);
            }
        }

        leafPolygons = workingPolys;

        progressBar.Dispose();

        // PHASE 2: Fix T-junctions across all brushes
        FixTJunctions(brushPendingFaces, brushes);

        WeldSeamVertices(brushPendingFaces, brushes);

        // PHASE 3: Triangulate and apply to brushes.
        Console.WriteLine("Triangulating and applying geometry...");
        progressBar = new ProgressBar();

        pendingToFinalFace = new Dictionary<int, int[]>();
        pendingToBaseVertex = new Dictionary<int, int[]>();

        for (int brushIdx = 0; brushIdx < brushes.Length; brushIdx++)
        {
            progress++;
            progressBar.Report(progress / (float)(brushes.Length * 2));

            var brush = brushes[brushIdx];
            var pendingFaces = brushPendingFaces[brushIdx];

            var finalFaceIndex = new int[pendingFaces.Count];
            var baseVertexIndex = new int[pendingFaces.Count];
            Array.Fill(finalFaceIndex, -1);
            Array.Fill(baseVertexIndex, -1);
            pendingToFinalFace[brushIdx] = finalFaceIndex;
            pendingToBaseVertex[brushIdx] = baseVertexIndex;

            if (pendingFaces.Count == 0)
                continue;

            var originalFaces = brush.Faces.ToArray();
            var originalVertices = brush.Vertices.ToArray();
            var originalUVs = brush.UVs.ToArray();
            var originalLMPUVs = brush.LightmapUVs.ToArray();

            var newVertices = new List<Vector3>();
            var newUVs = new List<Vector2>();
            var newLightmapUVs = new List<Vector2>();
            var newFaces = new List<Face>();

            var tempBrush = new Brush
            {
                Position = brush.Position,
                Vertices = originalVertices,
                UVs = originalUVs,
                LightmapUVs = originalLMPUVs,
                Faces = originalFaces
            };

            for (int pfIdx = 0; pfIdx < pendingFaces.Count; pfIdx++)
            {
                var pendingFace = pendingFaces[pfIdx];
                var localVertices = pendingFace.Vertices.ToArray();
                var originalFace = pendingFace.OriginalFace;

                if (localVertices.Length < 3)
                    continue;

                var newFace = new Face
                {
                    Surface = originalFace.Surface,
                    Normal = originalFace.Normal,
                    Tangent = originalFace.Tangent,
                    Binormal = originalFace.Binormal,
                    Basis1 = originalFace.Basis1,
                    Basis2 = originalFace.Basis2,
                    Basis3 = originalFace.Basis3,
                    TScaleX = originalFace.TScaleX,
                    TScaleY = originalFace.TScaleY,
                    TOffX = originalFace.TOffX,
                    TOffY = originalFace.TOffY,
                    LuxelScale = originalFace.LuxelScale,
                    MaterialName = originalFace.MaterialName,
                    Drawn = originalFace.Drawn,
                    toolFace = originalFace.toolFace,
                    smoothGroup = originalFace.smoothGroup,
                };

                int vertIDX = newVertices.Count;

                for (int i = 0; i < localVertices.Length; i++)
                {
                    newVertices.Add(localVertices[i]);

                    Vector3 worldPos = localVertices[i] + brush.Position;
                    var (uv, LMPuv) = ComputeUVsForVertex(worldPos, originalFace, tempBrush);
                    newUVs.Add(uv);
                    newLightmapUVs.Add(LMPuv);
                }

                var triangleIndices = TriangulatePolygon(localVertices, originalFace.Normal);
                var finalIndices = triangleIndices.Select(i => i + vertIDX).ToArray();

                newFace.Indices = finalIndices;

                finalFaceIndex[pfIdx] = newFaces.Count;
                baseVertexIndex[pfIdx] = vertIDX;
                newFaces.Add(newFace);

                if (!newFace.Drawn) continue;

                int pendingIdx = pfIdx;

                var workingPoly = new WorkingLeafPoly
                {
                    LeafIndex = pendingFace.LeafIndex,
                    MaterialName = originalFace.MaterialName,
                    Normal = originalFace.Normal,
                    Tangent = originalFace.Tangent,
                    Binormal = originalFace.Binormal,
                    B1 = originalFace.Basis1,
                    B2 = originalFace.Basis2,
                    B3 = originalFace.Basis3,
                    BrushIndex = brushIdx,
                    PendingFaceIndex = pendingIdx,
                };

                for (int i = 0; i < localVertices.Length; i++)
                {
                    Vector3 worldPos = localVertices[i] + brush.Position;
                    workingPoly.Vertices.Add(worldPos);
                    //workingPoly.UVs.Add(ComputeUVForVertex(worldPos, originalFace, brush));
                    // LightmapUVs filled in later by ComputeLeafPolygonLightmapUVs
                }

                workingPoly.Indices.AddRange(TriangulatePolygon(localVertices.ToArray(), originalFace.Normal));
                workingPolys.Add(workingPoly);
            }

            if (newFaces.Count > 0)
            {
                brush.Vertices = newVertices.ToArray();
                brush.UVs = newUVs.ToArray();
                brush.LightmapUVs = newLightmapUVs.ToArray();
                brush.Faces = newFaces.ToArray();

                brushes[brushIdx] = brush;
            }
        }

        progressBar.Dispose();

        int totalFacesAfter = brushes.Sum(b => b.Faces.Length);
        Console.WriteLine($"Brush reconstruction complete. Total faces after CSG: {totalFacesAfter}");
    }

    /// <summary>
    /// Fills in leaf-polygon lightmap UVs. I could probably do this smarter but it doesnt seem to cause
    /// any issues, so it stays.
    /// </summary>
    public static void ComputeLeafPolygonLightmapUVs(
        List<WorkingLeafPoly> leafPolygons,
        Brush[] brushes,
        Dictionary<int, int[]> pendingToBaseVertex)
    {
        foreach (var wp in leafPolygons)
        {
            var brush = brushes[wp.BrushIndex];
            var baseIndices = pendingToBaseVertex[wp.BrushIndex];

            int vBase = wp.PendingFaceIndex < baseIndices.Length ? baseIndices[wp.PendingFaceIndex] : -1;
            if (vBase < 0 || vBase + wp.Vertices.Count > brush.LightmapUVs.Length)
            {
                Console.WriteLine($"Warning: leaf poly on brush {wp.BrushIndex} lost its vertex mapping, lightmap UVs left blank.");
                continue;
            }

            wp.LightmapUVs.Clear();
            for (int i = 0; i < wp.Vertices.Count; i++)
            {
                wp.UVs.Add(brush.UVs[vBase + i]);
                wp.LightmapUVs.Add(brush.LightmapUVs[vBase + i]);
            }
        }
    }
    public static void ApplySmoothedNormalsToLeafPolys(
        List<WorkingLeafPoly> leafPolys,
        Brush[] brushes,
        Dictionary<int, int[]> pendingToFinalFace,
        Dictionary<(int brush, int face, int vertex), SmoothedVertexData> smoothedNormals)
    {
        foreach (var wp in leafPolys)
        {
            var finalFaces = pendingToFinalFace[wp.BrushIndex];
            int finalFaceIdx = wp.PendingFaceIndex < finalFaces.Length ? finalFaces[wp.PendingFaceIndex] : -1;

            if (finalFaceIdx < 0 || finalFaceIdx >= brushes[wp.BrushIndex].Faces.Length)
            {
                for (int i = 0; i < wp.Vertices.Count; i++)
                {
                    wp.VertexNormals.Add(wp.Normal);
                    wp.VertexTangents.Add(wp.Tangent);
                    wp.VertexBinormals.Add(wp.Binormal);
                }
                continue;
            }

            var brush = brushes[wp.BrushIndex];
            var face = brush.Faces[finalFaceIdx];

            Vector3 flatNormal = face.Normal;
            Vector3 flatTangent = wp.Tangent;
            Vector3 flatBinormal = wp.Binormal;

            float handedness = MathF.Sign(Vector3.Dot(Vector3.Cross(flatNormal, flatTangent), flatBinormal));
            if (handedness == 0f) handedness = 1f;

            foreach (var worldPos in wp.Vertices)
            {
                var vd = SmoothGroups.SampleAt(brush, wp.BrushIndex, finalFaceIdx, worldPos, smoothedNormals);

                wp.VertexNormals.Add(vd.Normal);
                wp.VertexTangents.Add(vd.Tangent);
                wp.VertexBinormals.Add(vd.Binormal);
            }
        }
    }
}
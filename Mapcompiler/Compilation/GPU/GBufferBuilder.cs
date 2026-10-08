using MapCompiler.Compilation.GPU.Resources;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;
internal static class GBufferBuilder
{
    private const int RowChunkSize = 32;

    public static GBufferData Build(
        Brush[] brushes,
        int lightmapResolution,
        List<(Vector2 min, Vector2 max)>[] faceVBounds,
        Dictionary<(int brush, int face, int vertex), SmoothedVertexData> smoothNormals)
    {
        int total = lightmapResolution * lightmapResolution;
        var gbuffer = new GBufferData
        {
            PositionAndValid = new Vector4[total],
            Normal = new Vector3[total],
            Basis1 = new Vector3[total],
            Basis2 = new Vector3[total],
            Basis3 = new Vector3[total],
            SourceBrush = new int[total],
            EntityGroup = new int[total]
        };

        float uvCellSize = MathF.Max(8f / lightmapResolution, 0.001f);

        var faceGridCache = new System.Collections.Concurrent.ConcurrentDictionary<(int brush, int face), GeometryUtils.FaceUvGrid>();
        var faceLoopIndexCache = new System.Collections.Concurrent.ConcurrentDictionary<(int brush, int face), int[]>();
        var originalEdgeCache = new System.Collections.Concurrent.ConcurrentDictionary<(int brush, int face), (Vector2 a, Vector2 b)[]>();
        var faceInsetCache = new System.Collections.Concurrent.ConcurrentDictionary<(int brush, int face), (Vector2 a, Vector2 b)[]>();

        float inwardPush = 0.5f / lightmapResolution;
        var texelIsInterior = new byte[total];

        var interiorTasks = new List<(int brush, int face, int yStart, int yEnd)>();
        for (int i = 0; i < brushes.Length; i++)
        {
            for (int f = 0; f < brushes[i].Faces.Length; f++)
            {
                if (brushes[i].Faces[f].toolFace) continue;

                var (vmin, vmax) = faceVBounds[i][f];
                int yMin = (int)MathF.Floor(vmin.Y * lightmapResolution);
                int yMax = (int)MathF.Ceiling(vmax.Y * lightmapResolution);

                for (int y = yMin; y < yMax; y += RowChunkSize)
                {
                    interiorTasks.Add((i, f, y, Math.Min(y + RowChunkSize, yMax)));
                }
            }
        }

        using (var progress = CompilerConsole.StartProgress("G-buffer resolve (interior)"))
        {
            progress.Report(0);
            int done = 0;

            Parallel.ForEach(interiorTasks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, task =>
            {
                var (i, f, yStart, yEnd) = task;

                var faceGrid = faceGridCache.GetOrAdd((i, f), k => new GeometryUtils.FaceUvGrid(brushes[k.brush].LightmapUVs, brushes[k.brush].Faces[k.face].Indices, uvCellSize));
                var loopIndices = faceLoopIndexCache.GetOrAdd((i, f), k => GeometryUtils.GetFaceUvBoundaryLoop(brushes[k.brush], k.face));
                var originalEdges = originalEdgeCache.GetOrAdd((i, f), k =>
                {
                    var idx = faceLoopIndexCache[k];
                    var loopUvs = new Vector2[idx.Length];
                    for (int li = 0; li < idx.Length; li++) loopUvs[li] = brushes[k.brush].LightmapUVs[idx[li]];
                    return GeometryUtils.PolygonToEdges(loopUvs);
                });

                var faceLoop = SmoothGroups.BuildFaceLoop(brushes[i], f);
                var (vmin, vmax) = faceVBounds[i][f];

                int xMin = (int)MathF.Floor(vmin.X * lightmapResolution);
                int xMax = (int)MathF.Ceiling(vmax.X * lightmapResolution);

                for (int x = xMin; x < xMax; x++)
                {
                    for (int y = yStart; y < yEnd; y++)
                    {
                        if (x < 0 || x >= lightmapResolution || y < 0 || y >= lightmapResolution) continue;

                        var texPoint = new Vector2((x + 0.5f) / lightmapResolution, (y + 0.5f) / lightmapResolution);
                        if (!GeometryUtils.PointInPolygonEdges(texPoint, originalEdges)) continue;

                        Vector3 worldFlat = GeometryUtils.LightmapUvTo3D(texPoint, brushes[i], f, faceGrid, out _);
                        var vd = SmoothGroups.SampleAt(brushes[i], i, f, worldFlat, smoothNormals, faceLoop);

                        Vector3 world = worldFlat + vd.Normal * 0.001f;
                        Vector3 escaped = GeometryUtils.EscapeSolidForResolve(world, vd.Normal, vd.Tangent, vd.Binormal);

                        int idx = y * lightmapResolution + x;

                        gbuffer.PositionAndValid[idx] = new Vector4(escaped, 1f);
                        gbuffer.Normal[idx] = vd.Normal;
                        gbuffer.Basis1[idx] = vd.Basis1;
                        gbuffer.Basis2[idx] = vd.Basis2;
                        gbuffer.Basis3[idx] = vd.Basis3;
                        gbuffer.SourceBrush[idx] = i;
                        gbuffer.EntityGroup[idx] = TriangleOccluder.GetBrushEntityGroup(i);
                        texelIsInterior[idx] = 1;
                    }
                }

                int cur = Interlocked.Increment(ref done);
                if (cur % 25 == 0 || cur == interiorTasks.Count) progress.Report((float)cur / interiorTasks.Count);
            });
        }

        var paddingTasks = new List<(int brush, int face, int yStart, int yEnd)>();
        for (int i = 0; i < brushes.Length; i++)
        {
            for (int f = 0; f < brushes[i].Faces.Length; f++)
            {
                if (brushes[i].Faces[f].toolFace) continue;

                var (vmin, vmax) = faceVBounds[i][f];
                int yMin = (int)MathF.Floor(vmin.Y * lightmapResolution) - 3;
                int yMax = (int)MathF.Ceiling(vmax.Y * lightmapResolution) + 3;

                for (int y = yMin; y < yMax; y += RowChunkSize)
                {
                    paddingTasks.Add((i, f, y, Math.Min(y + RowChunkSize, yMax)));
                }
            }
        }

        using (var progress = CompilerConsole.StartProgress("G-buffer resolve (padding)"))
        {
            progress.Report(0);
            int done = 0;

            Parallel.ForEach(paddingTasks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, task =>
            {
                var (i, f, yStart, yEnd) = task;

                var faceGrid = faceGridCache[(i, f)];
                var insetEdges = faceInsetCache.GetOrAdd((i, f), k =>
                {
                    var idx = faceLoopIndexCache[k];
                    var loopUvs = new Vector2[idx.Length];
                    for (int li = 0; li < idx.Length; li++) loopUvs[li] = brushes[k.brush].LightmapUVs[idx[li]];
                    return GeometryUtils.PolygonToEdges(GeometryUtils.BuildInsetPolygon(loopUvs, inwardPush));
                });

                var faceLoop = SmoothGroups.BuildFaceLoop(brushes[i], f);
                var (vmin, vmax) = faceVBounds[i][f];

                int xMin = (int)MathF.Floor(vmin.X * lightmapResolution) - 3;
                int xMax = (int)MathF.Ceiling(vmax.X * lightmapResolution) + 3;

                for (int x = xMin; x < xMax; x++)
                {
                    for (int y = yStart; y < yEnd; y++)
                    {
                        if (x < 0 || x >= lightmapResolution || y < 0 || y >= lightmapResolution) continue;

                        int idx = y * lightmapResolution + x;
                        if (texelIsInterior[idx] != 0) continue;

                        var texPoint = new Vector2((x + 0.5f) / lightmapResolution, (y + 0.5f) / lightmapResolution);

                        var clampedUv = GeometryUtils.ClampUvToFacePolygon(texPoint, insetEdges);
                        Vector3 worldFlat = GeometryUtils.LightmapUvTo3D(clampedUv, brushes[i], f, faceGrid, out _);
                        var vd = SmoothGroups.SampleAt(brushes[i], i, f, worldFlat, smoothNormals, faceLoop);

                        Vector3 world = worldFlat + vd.Normal * 0.001f;
                        Vector3 escaped = GeometryUtils.EscapeSolidForResolve(world, vd.Normal, vd.Tangent, vd.Binormal);

                        gbuffer.PositionAndValid[idx] = new Vector4(escaped, 1f);
                        gbuffer.Normal[idx] = vd.Normal;
                        gbuffer.Basis1[idx] = vd.Basis1;
                        gbuffer.Basis2[idx] = vd.Basis2;
                        gbuffer.Basis3[idx] = vd.Basis3;
                        gbuffer.SourceBrush[idx] = i;
                        gbuffer.EntityGroup[idx] = TriangleOccluder.GetBrushEntityGroup(i);
                    }
                }

                int cur = Interlocked.Increment(ref done);
                if (cur % 25 == 0 || cur == paddingTasks.Count) progress.Report((float)cur / paddingTasks.Count);
            });
        }

        return gbuffer;
    }

    // Terrains are a weird beast, so a separate function is a bit cleaner
    public static void AddTerrain(GBufferData gbuffer, Terrain[] terrains, int lightmapResolution)
    {
        var tasks = new List<(int t, int triStart)>();
        for (int t = 0; t < terrains.Length; t++)
        {
            for (int tri = 0; tri < terrains[t].Triangles.Length; tri += 3)
            {
                tasks.Add((t, tri));
            }
        }

        int done = 0, total = tasks.Count;
        using var progress = CompilerConsole.StartProgress("G-buffer resolve (terrain)");
        progress.Report(0);

        Parallel.ForEach(tasks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, task =>
        {
            var (t, triStart) = task;

            int i0 = terrains[t].Triangles[triStart];
            int i1 = terrains[t].Triangles[triStart + 1];
            int i2 = terrains[t].Triangles[triStart + 2];

            Vector2 uv0 = terrains[t].lightmapUvs[i0];
            Vector2 uv1 = terrains[t].lightmapUvs[i1];
            Vector2 uv2 = terrains[t].lightmapUvs[i2];

            Vector3 p0 = terrains[t].Vertices[i0].Position;
            Vector3 p1 = terrains[t].Vertices[i1].Position;
            Vector3 p2 = terrains[t].Vertices[i2].Position;

            Vector3 n0 = terrains[t].Vertices[i0].Normal;
            Vector3 n1 = terrains[t].Vertices[i1].Normal;
            Vector3 n2 = terrains[t].Vertices[i2].Normal;

            Vector4 tv0 = terrains[t].Vertices[i0].Tangent.ToVector4();
            Vector4 tv1 = terrains[t].Vertices[i1].Tangent.ToVector4();
            Vector4 tv2 = terrains[t].Vertices[i2].Tangent.ToVector4();

            float area2D = GeometryUtils.TriArea2D(uv0, uv1, uv2);
            if (MathF.Abs(area2D) < 1e-10f) return;

            Vector2 uvMin = Vector2.Min(Vector2.Min(uv0, uv1), uv2);
            Vector2 uvMax = Vector2.Max(Vector2.Max(uv0, uv1), uv2);

            int xMin = (int)MathF.Floor(uvMin.X * lightmapResolution) - 1;
            int xMax = (int)MathF.Ceiling(uvMax.X * lightmapResolution) + 1;
            int yMin = (int)MathF.Floor(uvMin.Y * lightmapResolution) - 1;
            int yMax = (int)MathF.Ceiling(uvMax.Y * lightmapResolution) + 1;

            for (int x = xMin; x <= xMax; x++)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    if (x < 0 || x >= lightmapResolution || y < 0 || y >= lightmapResolution) continue;

                    var sp = new Vector2((float)x / lightmapResolution, (float)y / lightmapResolution);

                    float b0 = GeometryUtils.TriArea2D(uv1, uv2, sp) / area2D;
                    float b1 = GeometryUtils.TriArea2D(uv2, uv0, sp) / area2D;
                    float b2 = GeometryUtils.TriArea2D(uv0, uv1, sp) / area2D;

                    float penalty = MathF.Max(0f, -b0) + MathF.Max(0f, -b1) + MathF.Max(0f, -b2);
                    const float maxExtrapolatePenalty = 0.05f;
                    if (penalty > maxExtrapolatePenalty) continue;

                    float cb0 = MathF.Max(0f, b0);
                    float cb1 = MathF.Max(0f, b1);
                    float cb2 = MathF.Max(0f, b2);
                    float bsum = cb0 + cb1 + cb2;
                    if (bsum < 1e-8f) continue;
                    cb0 /= bsum; cb1 /= bsum; cb2 /= bsum;

                    Vector3 worldPos = cb0 * p0 + cb1 * p1 + cb2 * p2;
                    Vector3 normal = Vector3.Normalize(cb0 * n0 + cb1 * n1 + cb2 * n2);

                    Vector4 tangent4 = cb0 * tv0 + cb1 * tv1 + cb2 * tv2;
                    Vector3 tRaw = Vector3.Normalize(new Vector3(tangent4.X, tangent4.Y, tangent4.Z));
                    float hand = tangent4.W >= 0f ? 1f : -1f;

                    Vector3 tan = Vector3.Normalize(tRaw - normal * Vector3.Dot(normal, tRaw));
                    Vector3 bv = Vector3.Cross(normal, tan) * hand;

                    Vector3 basis1 = Vector3.Normalize(MapCompileOrchestrator.B1.X * tan + MapCompileOrchestrator.B1.Y * bv + MapCompileOrchestrator.B1.Z * normal);
                    Vector3 basis2 = Vector3.Normalize(MapCompileOrchestrator.B2.X * tan + MapCompileOrchestrator.B2.Y * bv + MapCompileOrchestrator.B2.Z * normal);
                    Vector3 basis3 = Vector3.Normalize(MapCompileOrchestrator.B3.X * tan + MapCompileOrchestrator.B3.Y * bv + MapCompileOrchestrator.B3.Z * normal);

                    int idx = y * lightmapResolution + x;

                    gbuffer.PositionAndValid[idx] = new Vector4(worldPos + normal * 0.001f, 1f);
                    gbuffer.Normal[idx] = normal;
                    gbuffer.Basis1[idx] = basis1;
                    gbuffer.Basis2[idx] = basis2;
                    gbuffer.Basis3[idx] = basis3;
                    gbuffer.SourceBrush[idx] = -1;
                    gbuffer.EntityGroup[idx] = -1;
                }
            }

            int cur = Interlocked.Increment(ref done);
            if (cur % 50 == 0 || cur == total) progress.Report((float)cur / total);
        });
    }
}
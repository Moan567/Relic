using MapCompiler.Compilation.GPU;
using MapCompiler.Compilation.GPU.Resources;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MapCompiler
{
    public struct Patch
    {
        public Vector3 normal, center, c0, c1, c2, c3;
        public Vector2 startUV, endUV;
        public float dr, dg, db;          // direct-light color (used as seed for bounces)
        public float r, g, b;             // current accumulated bounce color
        public float texr, texg, texb;    // albedo of the surface material
        public float area;
        public float luxels;
        public float sky; 
        public float ao0, ao1, ao2, ao3, ao4;
        public int id1, id2;
        public uint samples;
        public bool isEdge;
        public bool isTerrain;
        public (int index, float weight)[] transfer;
        public ((int node, int child) key, float weight)[] lightNodeTransfer;
        public Patch[] neighbors;
    }

    public struct TransferCandidate
    {
        public int Src, Dst;
        public float Geom;
    }
    public struct LightNodeCandidate
    {
        public int PatchIdx, ChildIdx;
        public float Contrib;
    }


    /// <summary>
    /// Compact spatial hash grid for fast neighbor queries over patch centers.
    /// </summary>
    public sealed class PatchSpatialGrid
    {
        private readonly float cellSize, invCellSize;
        private readonly Dictionary<(int x, int y, int z), int[]> cells;
        private readonly Vector3[] centers;

        public PatchSpatialGrid(Vector3[] centers, float cellSize)
        {
            this.cellSize    = cellSize;
            invCellSize = 1f / cellSize;
            this.centers     = centers;

            var temp = new ConcurrentDictionary<(int, int, int), ConcurrentBag<int>>();
            Parallel.For(0, centers.Length, i =>
                temp.GetOrAdd(WorldToCell(centers[i]), _ => new ConcurrentBag<int>()).Add(i));

            cells = new Dictionary<(int, int, int), int[]>(temp.Count);
            foreach (var kvp in temp)
                cells[kvp.Key] = kvp.Value.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (int x, int y, int z) WorldToCell(Vector3 pos) => (
            (int)MathF.Floor(pos.X * invCellSize),
            (int)MathF.Floor(pos.Y * invCellSize),
            (int)MathF.Floor(pos.Z * invCellSize));

        public int[] QueryNeighbors(int p, float maxDist) =>
            QueryNeighborsInternal(centers[p], maxDist, p);

        public int[] QueryNeighbors(Vector3 p, float maxDist) =>
            QueryNeighborsInternal(p, maxDist, -1);
        public void QueryNeighborsNonAlloc(Vector3 center, float maxDist, List<int> results, int exclude = -1)
        {
            results.Clear();
            var baseKey = WorldToCell(center);
            int range = (int)MathF.Ceiling(maxDist * invCellSize);
            float maxDistSq = maxDist * maxDist;

            for (int x = baseKey.x - range; x <= baseKey.x + range; x++)
            {
                for (int y = baseKey.y - range; y <= baseKey.y + range; y++)
                {
                    for (int z = baseKey.z - range; z <= baseKey.z + range; z++)
                    {
                        if (!cells.TryGetValue((x, y, z), out var bucket)) continue;
                        foreach (int idx in bucket)
                        {
                            if (idx == exclude) continue;
                            var d = centers[idx] - center;
                            if (d.X * d.X + d.Y * d.Y + d.Z * d.Z <= maxDistSq)
                                results.Add(idx);
                        }
                    }
                }
            }
        }
        private int[] QueryNeighborsInternal(Vector3 center, float maxDist, int exclude)
        {
            var result     = new List<int>();
            var baseKey    = WorldToCell(center);
            int range      = (int)MathF.Ceiling(maxDist * invCellSize);
            float maxDistSq = maxDist * maxDist;

            for (int x = baseKey.x - range; x <= baseKey.x + range; x++)
            for (int y = baseKey.y - range; y <= baseKey.y + range; y++)
            for (int z = baseKey.z - range; z <= baseKey.z + range; z++)
            {
                if (!cells.TryGetValue((x, y, z), out var bucket)) continue;
                foreach (int idx in bucket)
                {
                    if (idx == exclude) continue;
                    var d = centers[idx] - center;
                    if (d.X * d.X + d.Y * d.Y + d.Z * d.Z <= maxDistSq)
                        result.Add(idx);
                }
            }

            return result.ToArray();
        }
    }

    /// <summary>
    /// Builds the patch array from brush geometry, computes view-factor transfer
    /// functions between patches, and runs the progressive-refinement bounce pass.
    /// </summary>
    public static class PatchSystem
    {
        private const float MaxTransferDistance = 16f;

        public static Patch[] BuildPatches(
            Brush[] brushes,
            Terrain[] terrains,
            int lightmapResolution,
            int lightmapUnitSize,
            Color[] matColors)
        {
            const int PatchSplits  = 64;
            const float PatchScale = 2;
            var patches = new List<Patch>();

            var faceInsetCache = new Dictionary<(int brush, int face), (Vector2 a, Vector2 b)[]>();
            float inwardPush = 0.25f / lightmapResolution;

            (Vector2 a, Vector2 b)[] GetInsetBoundary(int brushIdx, int faceIdx)
            {
                var key = (brushIdx, faceIdx);
                if (!faceInsetCache.TryGetValue(key, out var inset))
                {
                    var loopIndices = GeometryUtils.GetFaceUvBoundaryLoop(brushes[brushIdx], faceIdx);
                    var loopUvs = new Vector2[loopIndices.Length];
                    for (int li = 0; li < loopIndices.Length; li++)
                        loopUvs[li] = brushes[brushIdx].LightmapUVs[loopIndices[li]];

                    inset = GeometryUtils.PolygonToEdges(GeometryUtils.BuildInsetPolygon(loopUvs, inwardPush));
                    faceInsetCache[key] = inset;
                }
                return inset;
            }

            for (int i = 0; i < brushes.Length; i++)
            {
                if (brushes[i].IsClip || brushes[i].IsLightNodeVolume
                    || brushes[i].IsSkybox || brushes[i].IsTrigger) continue;

                for (int j = 0; j < brushes[i].Faces.Length; j++)
                {
                    if (!brushes[i].Faces[j].Drawn) continue;

                    // Determine the UV extent of this face in lightmap space
                    Vector2 vmin = new Vector2(float.MaxValue), vmax = new Vector2(float.MinValue);
                    foreach (int v in brushes[i].Faces[j].Indices)
                    {
                        float x = brushes[i].LightmapUVs[v].X;
                        float y = brushes[i].LightmapUVs[v].Y;
                        vmin = new Vector2(MathF.Min(vmin.X, x), MathF.Min(vmin.Y, y));
                        vmax = new Vector2(MathF.Max(vmax.X, x), MathF.Max(vmax.Y, y));
                    }

                    var face = brushes[i].Faces[j];

                    (int X, int Y) startLuxel = ((int)MathF.Floor(vmin.X * lightmapResolution), (int)MathF.Floor(vmin.Y * lightmapResolution));
                    (int X, int Y) endLuxel   = ((int)MathF.Ceiling(vmax.X * lightmapResolution), (int)MathF.Ceiling(vmax.Y * lightmapResolution));

                    // Recursively halve the face rectangle until patches are small enough
                    var rects = new List<Rectangle> {
                        new Rectangle(startLuxel.X, startLuxel.Y,
                                      endLuxel.X - startLuxel.X,
                                      endLuxel.Y - startLuxel.Y)
                    };

                    for (int s = 0; s < PatchSplits; s++)
                    {
                        var next = new List<Rectangle>(rects.Count * 2);
                        foreach (var r in rects)
                        {
                            var half = (r.Size.ToVector2() / 2f).ToPoint();
                            if (half.X > half.Y && half.X > lightmapUnitSize * PatchScale)
                            {
                                next.Add(new Rectangle(r.Location.X, r.Location.Y, half.X, r.Size.Y));
                                next.Add(new Rectangle(r.Location.X + half.X, r.Location.Y, half.X, r.Size.Y));
                            }
                            else if (half.Y > lightmapUnitSize * PatchScale)
                            {
                                next.Add(new Rectangle(r.Location.X, r.Location.Y, r.Size.X, half.Y));
                                next.Add(new Rectangle(r.Location.X, r.Location.Y + half.Y, r.Size.X, half.Y));
                            }
                            else
                            {
                                next.Add(r);
                            }
                        }
                        rects = next;
                    }

                    foreach (var rect in rects)
                    {
                        var patch = new Patch
                        {
                            id1    = i,
                            id2     = j,
                            normal   = brushes[i].Faces[j].Normal,
                            startUV  = rect.Location.ToVector2() / lightmapResolution,
                            endUV    = (rect.Location + rect.Size).ToVector2() / lightmapResolution,
                            samples  = 0,
                            texr     = matColors[face.Surface].R / 255f,
                            texg     = matColors[face.Surface].G / 255f,
                            texb     = matColors[face.Surface].B / 255f,
                        };

                        patch.luxels = rect.Size.X * rect.Size.Y;
                        //patch.area   = patch.luxels / lightmapResolution;
                        patch.area   = 1f;
                        patch.isEdge = rect.Location.X == startLuxel.X || rect.Location.Y == startLuxel.Y
                                    || (rect.Location.X + rect.Width)  == endLuxel.X
                                    || (rect.Location.Y + rect.Height) == endLuxel.Y;

                        var insetBoundary = GetInsetBoundary(i, j);

                        patch.c0 = Clamp3D(GeometryUtils.ClampUvToFacePolygon(patch.startUV, insetBoundary), brushes[i], j);
                        patch.c1 = Clamp3D(GeometryUtils.ClampUvToFacePolygon(patch.endUV, insetBoundary), brushes[i], j);
                        patch.c2 = Clamp3D(GeometryUtils.ClampUvToFacePolygon(new Vector2(patch.startUV.X, patch.endUV.Y), insetBoundary), brushes[i], j);
                        patch.c3 = Clamp3D(GeometryUtils.ClampUvToFacePolygon(new Vector2(patch.endUV.X, patch.startUV.Y), insetBoundary), brushes[i], j);

                        //Vector2 mid = GeometryUtils.ClampUvToFacePolygon((patch.startUV + patch.endUV) / 2f, insetBoundary);
                        //patch.center = Clamp3D(mid, brushes[i], j);

                        patch.center = (patch.c0 + patch.c1 + patch.c2 + patch.c3) / 4f;

                        if (!float.IsFinite(patch.center.X) || !float.IsFinite(patch.center.Y) || !float.IsFinite(patch.center.Z))
                        {
                            CompilerConsole.Warn($"Degenerate patch skipped.");
                            continue;
                        }

                        patches.Add(patch);
                    }
                }
            }
            if(terrains != null)
            {
                for (int i = 0; i < terrains.Length; i++)
                {
                    // Determine the UV extent of this face in lightmap space
                    Vector2 vmin = new Vector2(float.MaxValue), vmax = new Vector2(float.MinValue);
                    foreach (int v in terrains[i].Triangles)
                    {
                        float x = terrains[i].lightmapUvs[v].X;
                        float y = terrains[i].lightmapUvs[v].Y;
                        vmin = new Vector2(MathF.Min(vmin.X, x), MathF.Min(vmin.Y, y));
                        vmax = new Vector2(MathF.Max(vmax.X, x), MathF.Max(vmax.Y, y));
                    }

                    (int X, int Y) startLuxel = ((int)MathF.Floor(vmin.X * lightmapResolution), (int)MathF.Floor(vmin.Y * lightmapResolution));
                    (int X, int Y) endLuxel = ((int)MathF.Ceiling(vmax.X * lightmapResolution), (int)MathF.Ceiling(vmax.Y * lightmapResolution));

                    // Recursively halve the face rectangle until patches are small enough
                    var rects = new List<Rectangle> {
                        new Rectangle(startLuxel.X, startLuxel.Y,
                                      endLuxel.X - startLuxel.X,
                                      endLuxel.Y - startLuxel.Y)
                    };

                    for (int s = 0; s < PatchSplits; s++)
                    {
                        var next = new List<Rectangle>(rects.Count * 2);
                        foreach (var r in rects)
                        {
                            var half = (r.Size.ToVector2() / 2f).ToPoint();
                            if (half.X > half.Y && half.X > lightmapUnitSize * PatchScale)
                            {
                                next.Add(new Rectangle(r.Location.X, r.Location.Y, half.X, r.Size.Y));
                                next.Add(new Rectangle(r.Location.X + half.X, r.Location.Y, half.X, r.Size.Y));
                            }
                            else if (half.Y > lightmapUnitSize * PatchScale)
                            {
                                next.Add(new Rectangle(r.Location.X, r.Location.Y, r.Size.X, half.Y));
                                next.Add(new Rectangle(r.Location.X, r.Location.Y + half.Y, r.Size.X, half.Y));
                            }
                            else
                            {
                                next.Add(r);
                            }
                        }
                        rects = next;
                    }

                    foreach (var rect in rects)
                    {
                        var patch = new Patch
                        {
                            id1 = i,
                            isTerrain = true,
                            startUV = rect.Location.ToVector2() / lightmapResolution,
                            endUV = (rect.Location + rect.Size).ToVector2() / lightmapResolution,
                            samples = 0,
                            texr = matColors[terrains[i].Surface].R / 255f,
                            texg = matColors[terrains[i].Surface].G / 255f,
                            texb = matColors[terrains[i].Surface].B / 255f,
                        };

                        patch.luxels = rect.Size.X * rect.Size.Y;
                        //patch.area = patch.luxels / lightmapResolution;
                        patch.area = 1f;
                        patch.isEdge = rect.Location.X == startLuxel.X || rect.Location.Y == startLuxel.Y
                                    || (rect.Location.X + rect.Width) == endLuxel.X
                                    || (rect.Location.Y + rect.Height) == endLuxel.Y;

                        // Resolve patch corners and center into world space
                        Vector2 mid = (patch.startUV + patch.endUV) / 2f;

                        Vector3 rawNormal = Clamp3D(mid, terrains[i].Vertices.Select(i => i.Normal).ToArray(), terrains[i].lightmapUvs, terrains[i].Triangles.Select(i => (int)i).ToArray());
                        patch.normal = rawNormal.LengthSquared() > 1e-8f ? Vector3.Normalize(rawNormal) : terrains[i].Vertices[terrains[i].Triangles[0]].Normal;

                        patch.center = Clamp3D(mid, terrains[i].Vertices.Select(i=>i.Position).ToArray(), terrains[i].lightmapUvs, terrains[i].Triangles.Select(i=>(int)i).ToArray());

                        if (!float.IsFinite(patch.center.X) || !float.IsFinite(patch.center.Y) || !float.IsFinite(patch.center.Z))
                        {
                            CompilerConsole.Warn($"Degenerate patch skipped.");
                            continue;
                        }

                        patch.c0 = Clamp3D(patch.startUV, terrains[i].Vertices.Select(i => i.Position).ToArray(), terrains[i].lightmapUvs, terrains[i].Triangles.Select(i => (int)i).ToArray());
                        patch.c1 = Clamp3D(patch.endUV, terrains[i].Vertices.Select(i => i.Position).ToArray(), terrains[i].lightmapUvs, terrains[i].Triangles.Select(i => (int)i).ToArray());
                        patch.c2 = Clamp3D(new Vector2(patch.startUV.X, patch.endUV.Y), terrains[i].Vertices.Select(i => i.Position).ToArray(), terrains[i].lightmapUvs, terrains[i].Triangles.Select(i => (int)i).ToArray());
                        patch.c3 = Clamp3D(new Vector2(patch.endUV.X, patch.startUV.Y), terrains[i].Vertices.Select(i => i.Position).ToArray(), terrains[i].lightmapUvs, terrains[i].Triangles.Select(i => (int)i).ToArray());

                        patches.Add(patch);
                    }
                }
            }

            return patches.ToArray();
        }

        /// <summary>
        /// Samples the direct-light lightmap arrays and stores the averaged
        /// value per patch as the seed for the bounce pass.
        /// </summary>
        public static void SeedPatchesFromLightmap(
            ref Patch[] patches,
            LightmapColor[] b1, LightmapColor[] b2, LightmapColor[] b3,
            int lightmapResolution,
            List<(Vector2 min, Vector2 max)>[] faceVBounds)
        {
            for (int i = 0; i < patches.Length; i++)
            {
                int xStart = ((int)MathF.Floor(patches[i].startUV.X * lightmapResolution));
                int xEnd = ((int)MathF.Ceiling(patches[i].endUV.X * lightmapResolution));
                int yStart = ((int)MathF.Floor(patches[i].startUV.Y * lightmapResolution));
                int yEnd = ((int)MathF.Ceiling(patches[i].endUV.Y * lightmapResolution));

                for (int x = xStart; x < xEnd; x++)
                {
                    for (int y = yStart; y < yEnd; y++)
                    {
                        if (x < 0 || x >= lightmapResolution) continue;
                        if (y < 0 || y >= lightmapResolution) continue;

                        int idx = y * lightmapResolution + x;
                        patches[i].r += (b1[idx].R + b2[idx].R + b3[idx].R) / 3f;
                        patches[i].g += (b1[idx].G + b2[idx].G + b3[idx].G) / 3f;
                        patches[i].b += (b1[idx].B + b2[idx].B + b3[idx].B) / 3f;
                        patches[i].samples += 1;
                    }
                }

                if (patches[i].samples > 0)
                {
                    patches[i].r /= patches[i].samples * 3;
                    patches[i].g /= patches[i].samples * 3;
                    patches[i].b /= patches[i].samples * 3;
                }

                patches[i].dr = patches[i].r;
                patches[i].dg = patches[i].g;
                patches[i].db = patches[i].b;
            }
        }

        /// <summary>
        /// Computes the view-factor transfer matrices between patches using a
        /// BSP visibility test to discard occluded patch pairs.
        /// Also computes transfers to light-node children and terrain vertices.
        /// </summary>
        public static void ComputeTransferFunctions(
            ref Patch[] _patches,
            Brush[] brushes,
            List<LightNodeBundle> lightNodes,
            PatchSpatialGrid grid,
            Vector3[] patchCenters,
            VisLeaf[] visLeaves)
        {
            Patch[] patches = _patches;

            var nodeToVisLeaf = BuildNodeToVisLeaf(visLeaves);
            var pvsSets = BuildPvsSets(visLeaves);

            var patchLeaf = new int[patches.Length];
            Parallel.For(0, patches.Length, p => patchLeaf[p] = GetVisLeaf(patches[p].center, patches[p].normal * 0.02f, nodeToVisLeaf));

            var flatChildPos = new List<Vector3>();
            var flatChildKey = new List<(int node, int child)>();
            for (int node = 0; node < lightNodes.Count; node++)
            {
                for (int n = 0; n < lightNodes[node].Children.Length; n++)
                {
                    flatChildPos.Add(lightNodes[node].Children[n].Pos);
                    flatChildKey.Add((node, n));
                }
            }

            var childPos = flatChildPos.ToArray();
            var childKey = flatChildKey.ToArray();
            var childLeaf = new int[childPos.Length];
            Parallel.For(0, childPos.Length, i => childLeaf[i] = GetVisLeaf(childPos[i], Vector3.Zero, nodeToVisLeaf));

            var lightNodeGrid = childPos.Length > 0 ? new PatchSpatialGrid(childPos, MaxTransferDistance) : null;

            int chunkSize = 128;
            int numChunks = (int)MathF.Ceiling(patches.Length / (float)chunkSize);
            int doneChunks = 0;

            using var progress = CompilerConsole.StartProgress("Transfer functions");
            progress.Report(0);

            Parallel.For(0, numChunks, c =>
            {
                var neighborBuf = new List<int>(64);
                var childBuf = new List<int>(32);

                for (int p = c * chunkSize; p < c * chunkSize + chunkSize; p++)
                {
                    if (p >= patches.Length) continue;

                    Vector3 center = patches[p].center;
                    int leafP = patchLeaf[p];

                    var transfer = new List<(int, float)>();
                    var nodeTransfer = new List<((int, int), float)>();
                    float total = 0f, nodeTotal = 0f;

                    const float falloffBias = 4;

                    grid.QueryNeighborsNonAlloc(center, MaxTransferDistance, neighborBuf, p);
                    foreach (int t in neighborBuf)
                    {
                        if (patches[t].id1 == patches[p].id1 && patches[t].isTerrain == patches[p].isTerrain) continue;
                        if (!LeavesCanSee(leafP, patchLeaf[t], pvsSets)) continue;

                        Vector3 faceCenter = patches[t].center;
                        float r = Vector3.Distance(faceCenter, center) + float.Epsilon;
                        Vector3 s = (faceCenter - center) / r;

                        float cos_i = MathF.Max(0f, Vector3.Dot(patches[p].normal, s));
                        float cos_j = MathF.Max(0f, Vector3.Dot(patches[t].normal, -s));

                        if (!(cos_i > 0f) || !(cos_j > 0f)) continue;

                        float geom = 1 * cos_i * cos_j / (MathF.PI * (r * r + falloffBias));

                        bool occluded = TestPatchOcclusion(patches[p], patches[t], center, faceCenter, r, patches[p].isTerrain ? -1 : patches[p].id1);
                        if (occluded) continue;

                        transfer.Add((t, geom));
                        total += geom;
                    }

                    if (lightNodeGrid != null)
                    {
                        lightNodeGrid.QueryNeighborsNonAlloc(center, MaxTransferDistance, childBuf);
                        foreach (int ci in childBuf)
                        {
                            if (!LeavesCanSee(leafP, childLeaf[ci], pvsSets)) continue;

                            Vector3 pos = childPos[ci];
                            float dist = Vector3.Distance(center, pos);
                            float contrib = 1f / ((dist + 1f) * (dist + 1f));

                            Vector3 origin = center + patches[p].normal * 0.01f;
                            var ray = new Ray(origin, Vector3.Normalize(pos - origin));
                            var hit = BSPRoot.TraceRay(ray, dist);
                            if (hit.Hit && Vector3.Distance(hit.Point, center) < dist) continue;

                            nodeTransfer.Add((childKey[ci], contrib));
                            nodeTotal += contrib;
                        }
                    }

                    float skyVisibility = LightCalculator.CastCheckAmbient(
                        center + patches[p].normal * 0.01f,
                        patches[p].normal);

                    float enclosure = 1f - skyVisibility;
                    patches[p].sky = skyVisibility;

                    float normalizeTarget = Math.Clamp(enclosure, 0f, 1f);

                    Normalize(transfer, total, normalizeTarget);
                    Normalize(nodeTransfer, nodeTotal);

                    patches[p].transfer = transfer.ToArray();
                    patches[p].lightNodeTransfer = nodeTransfer.ToArray();
                }

                Interlocked.Increment(ref doneChunks);
                progress.Report(doneChunks / (float)numChunks);
            });

            _patches = patches;
        }

        public static void RunBouncePass(ref Patch[] patches, int maxIters = 10)
        {
            var B      = new Vector3[patches.Length];
            var unshot = new Vector3[patches.Length];

            for (int i = 0; i < patches.Length; i++)
            {
                unshot[i] = new Vector3(patches[i].dr, patches[i].dg, patches[i].db);
            }

            for (int iter = 0; iter < maxIters; iter++)
            {
                for (int i = 0; i < unshot.Length; i++)
                {
                    if (unshot[i].LengthSquared() <= float.Epsilon * float.Epsilon) continue;

                    Vector3 Q  = unshot[i];
                    unshot[i]  = Vector3.Zero;

                    foreach (var (j, F) in patches[i].transfer)
                    {
                        var rho = new Vector3(patches[j].texr, patches[j].texg, patches[j].texb) * 0.8f + Vector3.One * 0.2f;

                        Vector3 arriving  = Q * F;
                        Vector3 reflected = new Vector3(arriving.X * rho.X, arriving.Y * rho.Y, arriving.Z * rho.Z);

                        B[j]      += reflected;
                        unshot[j] += reflected;
                    }
                }
            }

            for (int i = 0; i < patches.Length; i++)
            {
                patches[i].r = MathF.Max(B[i].X, 0f);
                patches[i].g = MathF.Max(B[i].Y, 0f);
                patches[i].b = MathF.Max(B[i].Z, 0f);
            }
        }
        public static int[] BuildTexelHomePatch(Patch[] patches, int lightmapResolution)
        {
            int totalLuxels = lightmapResolution * lightmapResolution;

            var texelPatch = new int[totalLuxels];
            Array.Fill(texelPatch, -1);

            for (int pid = 0; pid < patches.Length; pid++)
            {
                int xS = (int)MathF.Max(0, MathF.Floor(patches[pid].startUV.X * lightmapResolution) - 3);
                int xE = (int)MathF.Min(lightmapResolution - 1, MathF.Ceiling(patches[pid].endUV.X * lightmapResolution) + 3);
                int yS = (int)MathF.Max(0, MathF.Floor(patches[pid].startUV.Y * lightmapResolution) - 3);
                int yE = (int)MathF.Min(lightmapResolution - 1, MathF.Ceiling(patches[pid].endUV.Y * lightmapResolution) + 3);

                for (int y = yS; y <= yE; y++)
                {
                    for (int x = xS; x <= xE; x++)
                    {
                        int idx = y * lightmapResolution + x;
                        if (texelPatch[idx] == -1) texelPatch[idx] = pid;
                    }
                }
            }

            return texelPatch;
        }
        public static (int[] offsets, int[] indices) BuildSortedGridBuckets(
            Patch[] patches, Vector3 gridOrigin, float cellSize, int dimsX, int dimsY, int dimsZ)
        {
            int cellCount = dimsX * dimsY * dimsZ;
            var perCell = new List<int>[cellCount];

            for (int p = 0; p < patches.Length; p++)
            {
                var cell = Vector3.Floor((patches[p].center - gridOrigin) / cellSize);
                int cx = (int)cell.X, cy = (int)cell.Y, cz = (int)cell.Z;
                if (cx < 0 || cx >= dimsX || cy < 0 || cy >= dimsY || cz < 0 || cz >= dimsZ) continue;

                int cellIdx = (cz * dimsY + cy) * dimsX + cx;
                (perCell[cellIdx] ??= new List<int>()).Add(p);
            }

            var offsets = new int[cellCount + 1];
            for (int c = 0; c < cellCount; c++)
            {
                offsets[c + 1] = offsets[c] + (perCell[c]?.Count ?? 0);
            }

            var flat = new int[offsets[cellCount]];

            Parallel.For(0, cellCount, c =>
            {
                var list = perCell[c];
                if (list == null || list.Count == 0) return;

                Vector3 cellCenter = gridOrigin + new Vector3(
                    c % dimsX,
                    (c / dimsX) % dimsY,
                    c / (dimsX * dimsY)) * cellSize + Vector3.One * (cellSize * 0.5f);

                list.Sort((a, b) =>
                    Vector3.DistanceSquared(patches[a].center, cellCenter)
                        .CompareTo(Vector3.DistanceSquared(patches[b].center, cellCenter)));

                list.CopyTo(flat, offsets[c]);
            });

            return (offsets, flat);
        }
        public static (int[] offsetsFlat, int count) BuildSortedCellOffsets(int searchRadius)
        {
            var offsets = new List<(int x, int y, int z, float distSq)>();

            for (int dz = -searchRadius; dz <= searchRadius; dz++)
            {
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    for (int dx = -searchRadius; dx <= searchRadius; dx++)
                    {
                        float distSq = dx * dx + dy * dy + dz * dz;
                        offsets.Add((dx, dy, dz, distSq));
                    }
                }
            }

            offsets.Sort((a, b) => a.distSq.CompareTo(b.distSq));

            var flat = new int[offsets.Count * 3];
            for (int i = 0; i < offsets.Count; i++)
            {
                flat[i * 3] = offsets[i].x;
                flat[i * 3 + 1] = offsets[i].y;
                flat[i * 3 + 2] = offsets[i].z;
            }

            return (flat, offsets.Count);
        }
        private static float QuadArea(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3)
        {
            float tri1 = Vector3.Cross(c3 - c0, c1 - c0).Length() * 0.5f;
            float tri2 = Vector3.Cross(c1 - c0, c2 - c0).Length() * 0.5f;
            return tri1 + tri2;
        }
        private static Vector3 Clamp3D(Vector2 uv, Brush brush, int face)
        {
            Vector3 p = GeometryUtils.LightmapUvTo3D(uv, brush, face, out _);
            return GeometryUtils.ClampToFace(p, brush, face);
        }
        private static Vector3 Clamp3D(Vector2 uv, Vector3[] vert, Vector2[] lmpUV, int[] tris)
        {
            Vector3 p = GeometryUtils.LightmapUvTo3D(uv, vert, lmpUV, tris, out _);
            //return GeometryUtils.ClampToFace(p, brush, face);
            return p;
        }
        private static bool TestPatchOcclusion(
            in Patch src, in Patch dst,
            Vector3 srcCenter, Vector3 dstCenter,
            float dist, int selfBrush)
        {
            Span<Vector3> testPoints = stackalloc Vector3[]
            {
                src.center, src.c0, src.c1, src.c2, src.c3
            };

            foreach (var tp in testPoints)
            {
                Vector3 origin = (tp + srcCenter) / 2f + dst.normal * 0.015f;
                var ray = new Ray(origin, Vector3.Normalize(dstCenter - origin));
                var hit = BSPRoot.TraceRay(ray, dist, default, selfBrush);

                if (!hit.Hit || Vector3.Distance(hit.Point, origin) >= dist)
                    return false;
            }

            return true;
        }
        private static void Normalize<T>(List<(T, float)> list, float total, float target = 1f)
        {
            // NaN safe
            if (!(total > 0f)) return;

            float scale = target / total;
            for (int i = 0; i < list.Count; i++)
            {
                list[i] = (list[i].Item1, list[i].Item2 * scale);
            }
        }

        public static Patch[] CloneMutableState(Patch[] source)
        {
            var clone = new Patch[source.Length];
            Array.Copy(source, clone, source.Length);
            for (int i = 0; i < clone.Length; i++)
            {
                clone[i].dr = clone[i].dg = clone[i].db = 0f;
                clone[i].r = clone[i].g = clone[i].b = 0f;
                clone[i].samples = 0;
            }
            return clone;
        }

        private static Dictionary<int, int> BuildNodeToVisLeaf(VisLeaf[] leaves)
        {
            var map = new Dictionary<int, int>(leaves.Length);
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i].BspLeafID >= 0)
                    map[leaves[i].BspLeafID] = i;
            return map;
        }

        private static HashSet<uint>[] BuildPvsSets(VisLeaf[] leaves)
        {
            var sets = new HashSet<uint>[leaves.Length];
            for (int i = 0; i < leaves.Length; i++)
                sets[i] = new HashSet<uint>(leaves[i].PVS);
            return sets;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetVisLeaf(Vector3 pos, Vector3 nudge, Dictionary<int, int> nodeToVisLeaf)
        {
            uint node = BSPRoot.Traverse(pos + nudge);
            return nodeToVisLeaf.TryGetValue((int)node, out int leaf) ? leaf : -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool LeavesCanSee(int leafA, int leafB, HashSet<uint>[] pvsSets)
        {
            if (leafA < 0 || leafB < 0 || leafA == leafB) return true;
            return pvsSets[leafA].Contains((uint)leafB);
        }
    }
}

using Chisel.Models.Data;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using SimdVec = System.Numerics.Vector4;

namespace MapCompiler.Compilation;

/// <summary>
/// A bounding volume hierarchy over non-BSP triangle soup (terrain, detail brushes, and
/// detail models) used for shadow/occlusion ray tests against decorative geometry that
/// never makes it into the BSP tree.
/// </summary>
internal class TriangleOccluder
{
    private struct TriGroup
    {
        public Vector3[] positions;   // flat triangle soup: [i*3+0..2] = one triangle
        public int sourceIndex;       // terrain index or brush index
        public bool isTerrain;
        public int entityGroup;       // -1 = universal occluder, otherwise restricted to this entity index
    }
    private struct BVHNode
    {
        public BoundingBox bounds;
        public int left;      // index into 'nodes' of the left child, or -1 if this is a leaf
        public int right;     // index into 'nodes' of the right child, or -1 if this is a leaf
        public int triStart;  // first triangle index (into the trace-time arrays), leaf only
        public int triCount;  // number of triangles in this leaf, leaf only
    }
    private struct WideBVHNode
    {
        public SimdVec minX, minY, minZ, maxX, maxY, maxZ;
        public int child0, child1, child2, child3;
        public int triStart0, triStart1, triStart2, triStart3;
        public int triCount0, triCount1, triCount2, triCount3;
    }

    private const int MaxTrisPerLeaf = 8;

    private static BVHNode[] nodes = Array.Empty<BVHNode>();
    private static int rootIndex = -1;

    private static WideBVHNode[] wideNodes = Array.Empty<WideBVHNode>();
    private static int wideRootIndex = -1;

    // Trace-time triangle soup, reordered so every leaf's triangles are contiguous.
    private static Vector3[] triPositions = Array.Empty<Vector3>(); // 3 entries per triangle
    private static int[] triSourceIndex = Array.Empty<int>();
    private static bool[] triIsTerrain = Array.Empty<bool>();
    private static int[] triEntityGroup = Array.Empty<int>();
    private static readonly Dictionary<int, int> brushEntityGroups = new();
    public static void Build(Terrain[] terrains, Brush[] brushes, EntityReference[] entityReferences, int[] brushOwner)
    {
        brushEntityGroups.Clear();

        var groups = new ConcurrentBag<TriGroup>();

        if (terrains != null)
        {
            Parallel.For(0, terrains.Length, t =>
            {
                var terrain = terrains[t];
                var positions = new Vector3[terrain.Triangles.Length];
                for (int i = 0; i < terrain.Triangles.Length; i++)
                    positions[i] = terrain.Vertices[terrain.Triangles[i]].Position;

                groups.Add(new TriGroup { positions = positions, sourceIndex = t, isTerrain = true, entityGroup = -1 });
            });
        }

        for (int b = 0; b < brushes.Length; b++)
        {
            bool isEntityOwned = EntityOwnership.IsPartOfEntity(brushOwner, b);
            if (!brushes[b].IsDetail && !isEntityOwned) continue;

            var brush = brushes[b];
            var triList = new List<Vector3>();

            foreach (var face in brush.Faces)
            {
                if (!face.Drawn) continue;
                for (int i = 0; i < face.Indices.Length; i += 3)
                {
                    Vector3 v0 = brush.Vertices[face.Indices[i]] + brush.Position;
                    Vector3 v1 = brush.Vertices[face.Indices[i + 1]] + brush.Position;
                    Vector3 v2 = brush.Vertices[face.Indices[i + 2]] + brush.Position;
                    triList.Add(v0); triList.Add(v1); triList.Add(v2);
                }
            }

            if (triList.Count == 0) continue;

            int group = brushes[b].IsDetail ? -1 : brushOwner[b];
            brushEntityGroups[b] = group;

            groups.Add(new TriGroup { positions = triList.ToArray(), sourceIndex = b, isTerrain = false, entityGroup = group });
        }

        Parallel.For(0, entityReferences.Length, e =>
        {
            var entity = entityReferences[e];
            if (entity.EntityName != "DetailModel") return;
            if (entity.Properties == null || entity.Properties.Length <= 0) return;

            var filepath = Path.Combine(Program.WorkingDir, Path.ChangeExtension(entity.Properties[0].Value, "ccmdl"));
            if (!File.Exists(filepath) || Path.GetExtension(filepath) != ".ccmdl") return;

            var mat = Matrix.CreateFromYawPitchRoll(
                          MathHelper.ToRadians(entity.SpawnRotation.X),
                          MathHelper.ToRadians(entity.SpawnRotation.Y),
                          MathHelper.ToRadians(entity.SpawnRotation.Z)) *
                      Matrix.CreateWorld(entity.Position, Vector3.Forward, Vector3.Up);

            var model = CCMDLHandler.LoadCCMDL(File.ReadAllBytes(filepath));
            foreach (var bg in model.bodyGroups)
            {
                var verts = new List<Vector3>();
                foreach (var i in bg.meshData.Indices)
                    verts.Add(Vector3.Transform(bg.meshData.Vertices[i].Position, mat));

                if (verts.Count == 0) continue;

                groups.Add(new TriGroup { positions = verts.ToArray(), sourceIndex = -10, isTerrain = false, entityGroup = -1 });
            }
        });

        BuildFromGroups(groups.ToArray());
    }
    private static void BuildFromGroups(TriGroup[] groups)
    {
        int totalTris = 0;
        for (int g = 0; g < groups.Length; g++)
            totalTris += groups[g].positions.Length / 3;

        if (totalTris == 0)
        {
            nodes = Array.Empty<BVHNode>();
            rootIndex = -1;
            triPositions = Array.Empty<Vector3>();
            triSourceIndex = Array.Empty<int>();
            triIsTerrain = Array.Empty<bool>();
            triEntityGroup = Array.Empty<int>();
            wideNodes = Array.Empty<WideBVHNode>(); 
            wideRootIndex = -1;
            return;
        }

        var srcPositions = new Vector3[totalTris * 3];
        var srcSourceIndex = new int[totalTris];
        var srcIsTerrain = new bool[totalTris];
        var srcEntityGroup = new int[totalTris];

        int cursor = 0;
        for (int g = 0; g < groups.Length; g++)
        {
            var grp = groups[g];
            int triCount = grp.positions.Length / 3;
            Array.Copy(grp.positions, 0, srcPositions, cursor * 3, grp.positions.Length);
            for (int i = 0; i < triCount; i++)
            {
                srcSourceIndex[cursor + i] = grp.sourceIndex;
                srcIsTerrain[cursor + i] = grp.isTerrain;
                srcEntityGroup[cursor + i] = grp.entityGroup;
            }
            cursor += triCount;
        }

        var triOrder = new int[totalTris];
        for (int i = 0; i < totalTris; i++) triOrder[i] = i;

        var builtNodes = new List<BVHNode>(totalTris / MaxTrisPerLeaf * 2 + 1);
        rootIndex = BuildNode(triOrder, srcPositions, builtNodes, 0, totalTris);
        nodes = builtNodes.ToArray();

        var orderedPositions = new Vector3[totalTris * 3];
        var orderedSourceIndex = new int[totalTris];
        var orderedIsTerrain = new bool[totalTris];
        var orderedEntityGroup = new int[totalTris];
        for (int i = 0; i < totalTris; i++)
        {
            int ti = triOrder[i];
            orderedPositions[i * 3] = srcPositions[ti * 3];
            orderedPositions[i * 3 + 1] = srcPositions[ti * 3 + 1];
            orderedPositions[i * 3 + 2] = srcPositions[ti * 3 + 2];
            orderedSourceIndex[i] = srcSourceIndex[ti];
            orderedIsTerrain[i] = srcIsTerrain[ti];
            orderedEntityGroup[i] = srcEntityGroup[ti];
        }

        triPositions = orderedPositions;
        triSourceIndex = orderedSourceIndex;
        triIsTerrain = orderedIsTerrain;
        triEntityGroup = orderedEntityGroup;

        if (rootIndex >= 0 && nodes[rootIndex].left != -1)
        {
            var wideList = new List<WideBVHNode>(nodes.Length / 2 + 1);
            wideRootIndex = BuildWideNode(rootIndex, nodes, wideList);
            wideNodes = wideList.ToArray();
        }
        else if (rootIndex >= 0)
        {
            var singleLeaf = new WideBVHNode
            {
                minX = new SimdVec(nodes[rootIndex].bounds.Min.X, float.MaxValue, float.MaxValue, float.MaxValue),
                minY = new SimdVec(nodes[rootIndex].bounds.Min.Y, float.MaxValue, float.MaxValue, float.MaxValue),
                minZ = new SimdVec(nodes[rootIndex].bounds.Min.Z, float.MaxValue, float.MaxValue, float.MaxValue),
                maxX = new SimdVec(nodes[rootIndex].bounds.Max.X, float.MinValue, float.MinValue, float.MinValue),
                maxY = new SimdVec(nodes[rootIndex].bounds.Max.Y, float.MinValue, float.MinValue, float.MinValue),
                maxZ = new SimdVec(nodes[rootIndex].bounds.Max.Z, float.MinValue, float.MinValue, float.MinValue),
                child0 = -1,
                child1 = -1,
                child2 = -1,
                child3 = -1,
                triStart0 = nodes[rootIndex].triStart,
                triCount0 = nodes[rootIndex].triCount,
            };
            wideNodes = new[] { singleLeaf };
            wideRootIndex = 0;
        }
        else
        {
            wideNodes = Array.Empty<WideBVHNode>();
            wideRootIndex = -1;
        }
    }

    /// <summary>
    /// Recursively partitions triOrder[start, start+count) via a median split of the largest
    /// centroid-extent axis, building a balanced BVH. Returns the index of the node created
    /// for this range within 'builtNodes'.
    /// </summary>
    private static int BuildNode(int[] triOrder, Vector3[] positions, List<BVHNode> builtNodes, int start, int count)
    {
        Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
        Vector3 cmin = new(float.MaxValue), cmax = new(float.MinValue);

        for (int i = start; i < start + count; i++)
        {
            int ti = triOrder[i];
            Vector3 v0 = positions[ti * 3], v1 = positions[ti * 3 + 1], v2 = positions[ti * 3 + 2];
            bmin = Vector3.Min(bmin, Vector3.Min(v0, Vector3.Min(v1, v2)));
            bmax = Vector3.Max(bmax, Vector3.Max(v0, Vector3.Max(v1, v2)));
            Vector3 c = (v0 + v1 + v2) / 3f;
            cmin = Vector3.Min(cmin, c);
            cmax = Vector3.Max(cmax, c);
        }

        var node = new BVHNode { bounds = new BoundingBox(bmin, bmax) };

        if (count <= MaxTrisPerLeaf)
        {
            node.left = -1;
            node.right = -1;
            node.triStart = start;
            node.triCount = count;
            builtNodes.Add(node);
            return builtNodes.Count - 1;
        }

        Vector3 extent = cmax - cmin;
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        float mid = axis == 0 ? (cmin.X + cmax.X) * 0.5f
                  : axis == 1 ? (cmin.Y + cmax.Y) * 0.5f
                  : (cmin.Z + cmax.Z) * 0.5f;

        int lo = start, hi = start + count - 1;
        while (lo <= hi)
        {
            int ti = triOrder[lo];
            Vector3 c = (positions[ti * 3] + positions[ti * 3 + 1] + positions[ti * 3 + 2]) / 3f;
            float val = axis == 0 ? c.X : axis == 1 ? c.Y : c.Z;
            if (val <= mid) lo++;
            else
            {
                (triOrder[lo], triOrder[hi]) = (triOrder[hi], triOrder[lo]);
                hi--;
            }
        }

        int leftCount = lo - start;
        int rightCount = count - leftCount;
        if (leftCount == 0 || rightCount == 0)
        {
            // Degenerate split (all centroids landed on one side)
            leftCount = count / 2;
            rightCount = count - leftCount;
        }

        // Reserve this node's slot now so children (added by the recursive calls below) end
        // up at higher indices; left/right get patched in once both subtrees are built.
        int nodeIndex = builtNodes.Count;
        builtNodes.Add(node);

        int leftIdx = BuildNode(triOrder, positions, builtNodes, start, leftCount);
        int rightIdx = BuildNode(triOrder, positions, builtNodes, start + leftCount, rightCount);

        node = builtNodes[nodeIndex];
        node.left = leftIdx;
        node.right = rightIdx;
        builtNodes[nodeIndex] = node;

        return nodeIndex;
    }
    private static int BuildWideNode(int binIdx, BVHNode[] binNodes, List<WideBVHNode> outNodes)
    {
        var slots = new List<int>(4) { binNodes[binIdx].left, binNodes[binIdx].right };

        bool expanded = true;
        while (slots.Count < 4 && expanded)
        {
            expanded = false;
            for (int i = 0; i < slots.Count; i++)
            {
                if (binNodes[slots[i]].left == -1) continue;

                int idx = slots[i];
                slots[i] = binNodes[idx].left;
                slots.Insert(i + 1, binNodes[idx].right);
                expanded = true;
                break;
            }
        }

        var minXs = new float[4]; var minYs = new float[4]; var minZs = new float[4];
        var maxXs = new float[4]; var maxYs = new float[4]; var maxZs = new float[4];
        var children = new int[4] { -1, -1, -1, -1 };
        var triStarts = new int[4]; var triCounts = new int[4];

        for (int i = 0; i < 4; i++)
        {
            if (i >= slots.Count)
            {
                minXs[i] = minYs[i] = minZs[i] = float.MaxValue;
                maxXs[i] = maxYs[i] = maxZs[i] = float.MinValue;
                continue;
            }

            int childBin = slots[i];
            ref readonly var cn = ref binNodes[childBin];

            minXs[i] = cn.bounds.Min.X; minYs[i] = cn.bounds.Min.Y; minZs[i] = cn.bounds.Min.Z;
            maxXs[i] = cn.bounds.Max.X; maxYs[i] = cn.bounds.Max.Y; maxZs[i] = cn.bounds.Max.Z;

            if (cn.left == -1)
            {
                triStarts[i] = cn.triStart;
                triCounts[i] = cn.triCount;
            }
            else
            {
                children[i] = -2;
            }
        }

        var node = new WideBVHNode
        {
            minX = new SimdVec(minXs[0], minXs[1], minXs[2], minXs[3]),
            minY = new SimdVec(minYs[0], minYs[1], minYs[2], minYs[3]),
            minZ = new SimdVec(minZs[0], minZs[1], minZs[2], minZs[3]),
            maxX = new SimdVec(maxXs[0], maxXs[1], maxXs[2], maxXs[3]),
            maxY = new SimdVec(maxYs[0], maxYs[1], maxYs[2], maxYs[3]),
            maxZ = new SimdVec(maxZs[0], maxZs[1], maxZs[2], maxZs[3]),
            child0 = children[0],
            child1 = children[1],
            child2 = children[2],
            child3 = children[3],
            triStart0 = triStarts[0],
            triStart1 = triStarts[1],
            triStart2 = triStarts[2],
            triStart3 = triStarts[3],
            triCount0 = triCounts[0],
            triCount1 = triCounts[1],
            triCount2 = triCounts[2],
            triCount3 = triCounts[3],
        };

        int nodeSlot = outNodes.Count;
        outNodes.Add(node);

        for (int i = 0; i < 4; i++)
        {
            if (i >= slots.Count || children[i] != -2) continue;

            int childWideIdx = BuildWideNode(slots[i], binNodes, outNodes);
            node = outNodes[nodeSlot];
            switch (i)
            {
                case 0: node.child0 = childWideIdx; break;
                case 1: node.child1 = childWideIdx; break;
                case 2: node.child2 = childWideIdx; break;
                case 3: node.child3 = childWideIdx; break;
            }
            outNodes[nodeSlot] = node;
        }

        return nodeSlot;
    }
    public static int GetBrushEntityGroup(int brushIndex)
    => brushEntityGroups.TryGetValue(brushIndex, out int group) ? group : -1;


    public static bool TraceRay(Ray ray, float maxDist, int excludeTerrain = -1, int excludeBrush = -1, int selfEntityGroup = -1)
    {
        return TraceRay(ray, maxDist, out _, excludeTerrain, excludeBrush, selfEntityGroup);
    }
    public static bool TraceRay(Ray ray, float maxDist, out Vector3 hitPos, int excludeTerrain = -1, int excludeBrush = -1, int selfEntityGroup = -1)
    {
        hitPos = default;

        if (wideRootIndex < 0) return false;

        Vector3 dir = Vector3.Normalize(ray.Direction);
        Vector3 origin = ray.Position;
        Vector3 invDir = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);

        float bestT = maxDist;
        bool found = false;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = wideRootIndex;

        SimdVec ox = new SimdVec(origin.X), oy = new SimdVec(origin.Y), oz = new SimdVec(origin.Z);
        SimdVec idx = new SimdVec(invDir.X), idy = new SimdVec(invDir.Y), idz = new SimdVec(invDir.Z);

        while (sp > 0)
        {
            int ni = stack[--sp];
            ref readonly var node = ref wideNodes[ni];

            SimdVec bestVec = new SimdVec(bestT);

            SimdVec tminX = SimdVec.Min((node.minX - ox) * idx, (node.maxX - ox) * idx);
            SimdVec tmaxX = SimdVec.Max((node.minX - ox) * idx, (node.maxX - ox) * idx);
            SimdVec tminY = SimdVec.Min((node.minY - oy) * idy, (node.maxY - oy) * idy);
            SimdVec tmaxY = SimdVec.Max((node.minY - oy) * idy, (node.maxY - oy) * idy);
            SimdVec tminZ = SimdVec.Min((node.minZ - oz) * idz, (node.maxZ - oz) * idz);
            SimdVec tmaxZ = SimdVec.Max((node.minZ - oz) * idz, (node.maxZ - oz) * idz);

            SimdVec tmin = SimdVec.Max(SimdVec.Max(tminX, tminY), SimdVec.Max(tminZ, SimdVec.Zero));
            SimdVec tmax = SimdVec.Min(SimdVec.Min(tmaxX, tmaxY), SimdVec.Min(tmaxZ, bestVec));

            if (node.child0 != -1 && tmin.X <= tmax.X) stack[sp++] = node.child0;
            else if (node.triCount0 > 0 && tmin.X <= tmax.X)
                TestLeafTris(origin, dir, node.triStart0, node.triCount0, excludeTerrain, excludeBrush, selfEntityGroup, ref bestT, ref found, ref hitPos);

            if (node.child1 != -1 && tmin.Y <= tmax.Y) stack[sp++] = node.child1;
            else if (node.triCount1 > 0 && tmin.Y <= tmax.Y)
                TestLeafTris(origin, dir, node.triStart1, node.triCount1, excludeTerrain, excludeBrush, selfEntityGroup, ref bestT, ref found, ref hitPos);

            if (node.child2 != -1 && tmin.Z <= tmax.Z) stack[sp++] = node.child2;
            else if (node.triCount2 > 0 && tmin.Z <= tmax.Z)
                TestLeafTris(origin, dir, node.triStart2, node.triCount2, excludeTerrain, excludeBrush, selfEntityGroup, ref bestT, ref found, ref hitPos);

            if (node.child3 != -1 && tmin.W <= tmax.W) stack[sp++] = node.child3;
            else if (node.triCount3 > 0 && tmin.W <= tmax.W)
                TestLeafTris(origin, dir, node.triStart3, node.triCount3, excludeTerrain, excludeBrush, selfEntityGroup, ref bestT, ref found, ref hitPos);
        }

        return found;
    }
    private static void TestLeafTris(Vector3 origin, Vector3 dir, int triStart, int triCount, int excludeTerrain, int excludeBrush, int selfEntityGroup, ref float bestT, ref bool found, ref Vector3 hitPos)
    {
        var ray = new Ray(origin, dir);
        int end = triStart + triCount;
        for (int i = triStart; i < end; i++)
        {
            bool isTerrain = triIsTerrain[i];
            int srcIdx = triSourceIndex[i];
            if (isTerrain && srcIdx == excludeTerrain) continue;
            if (!isTerrain && srcIdx == excludeBrush) continue;

            int triGroup = triEntityGroup[i];
            if (triGroup >= 0 && triGroup != selfEntityGroup) continue;

            if (IntersectTriangle(ray, triPositions[i * 3], triPositions[i * 3 + 1], triPositions[i * 3 + 2], out float t)
                && t > 1e-4f && t < bestT)
            {
                bestT = t;
                found = true;
            }
        }

        if (found) hitPos = origin + dir * bestT;
    }
    public static void TraceRayPacket4(
    ReadOnlySpan<Vector3> origins,
    ReadOnlySpan<Vector3> directions,
    ReadOnlySpan<float> maxDists,
    Span<bool> hitFound,
    Span<Vector3> hitPos,
    int excludeTerrain = -1, int excludeBrush = -1, int selfEntityGroup = -1)
    {
        hitFound[0] = hitFound[1] = hitFound[2] = hitFound[3] = false;

        if (rootIndex < 0) return;

        Span<float> bestT = stackalloc float[4] { maxDists[0], maxDists[1], maxDists[2], maxDists[3] };
        Span<Vector3> dirNorm = stackalloc Vector3[4];
        for (int k = 0; k < 4; k++) dirNorm[k] = Vector3.Normalize(directions[k]);

        SimdVec ox = new SimdVec(origins[0].X, origins[1].X, origins[2].X, origins[3].X);
        SimdVec oy = new SimdVec(origins[0].Y, origins[1].Y, origins[2].Y, origins[3].Y);
        SimdVec oz = new SimdVec(origins[0].Z, origins[1].Z, origins[2].Z, origins[3].Z);

        SimdVec idx = new SimdVec(1f / dirNorm[0].X, 1f / dirNorm[1].X, 1f / dirNorm[2].X, 1f / dirNorm[3].X);
        SimdVec idy = new SimdVec(1f / dirNorm[0].Y, 1f / dirNorm[1].Y, 1f / dirNorm[2].Y, 1f / dirNorm[3].Y);
        SimdVec idz = new SimdVec(1f / dirNorm[0].Z, 1f / dirNorm[1].Z, 1f / dirNorm[2].Z, 1f / dirNorm[3].Z);

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = rootIndex;

        while (sp > 0)
        {
            int ni = stack[--sp];
            ref readonly var node = ref nodes[ni];

            SimdVec bmin_x = new SimdVec(node.bounds.Min.X);
            SimdVec bmax_x = new SimdVec(node.bounds.Max.X);
            SimdVec bmin_y = new SimdVec(node.bounds.Min.Y);
            SimdVec bmax_y = new SimdVec(node.bounds.Max.Y);
            SimdVec bmin_z = new SimdVec(node.bounds.Min.Z);
            SimdVec bmax_z = new SimdVec(node.bounds.Max.Z);

            SimdVec bestVec = new SimdVec(bestT[0], bestT[1], bestT[2], bestT[3]);

            SimdVec t1x = (bmin_x - ox) * idx, t2x = (bmax_x - ox) * idx;
            SimdVec t1y = (bmin_y - oy) * idy, t2y = (bmax_y - oy) * idy;
            SimdVec t1z = (bmin_z - oz) * idz, t2z = (bmax_z - oz) * idz;

            SimdVec tminV = SimdVec.Max(SimdVec.Max(SimdVec.Min(t1x, t2x), SimdVec.Min(t1y, t2y)), SimdVec.Max(SimdVec.Min(t1z, t2z), SimdVec.Zero));
            SimdVec tmaxV = SimdVec.Min(SimdVec.Min(SimdVec.Max(t1x, t2x), SimdVec.Max(t1y, t2y)), SimdVec.Min(SimdVec.Max(t1z, t2z), bestVec));

            bool anyActive =
                tminV.X <= tmaxV.X || tminV.Y <= tmaxV.Y ||
                tminV.Z <= tmaxV.Z || tminV.W <= tmaxV.W;

            if (!anyActive) continue;

            if (node.left == -1)
            {
                int end = node.triStart + node.triCount;
                for (int k = 0; k < 4; k++)
                {
                    if (tminV[k] > tmaxV[k]) continue;

                    var ray = new Ray(origins[k], dirNorm[k]);

                    for (int i = node.triStart; i < end; i++)
                    {
                        bool isTerrain = triIsTerrain[i];
                        int srcIdx = triSourceIndex[i];
                        if (isTerrain && srcIdx == excludeTerrain) continue;
                        if (!isTerrain && srcIdx == excludeBrush) continue;

                        int triGroup = triEntityGroup[i];
                        if (triGroup >= 0 && triGroup != selfEntityGroup) continue;

                        if (IntersectTriangle(ray, triPositions[i * 3], triPositions[i * 3 + 1], triPositions[i * 3 + 2], out float t)
                            && t > 1e-4f && t < bestT[k])
                        {
                            bestT[k] = t;
                            hitFound[k] = true;
                            hitPos[k] = origins[k] + dirNorm[k] * t;
                        }
                    }
                }
            }
            else
            {
                stack[sp++] = node.left;
                stack[sp++] = node.right;
            }
        }
    }

    /// <summary>
    /// Traces a ray through the occluder BVH. Returns true if a triangle was hit within
    /// maxDist, and outputs the world-space position of the closest such hit via hitPos.
    /// hitPos is only meaningful when this returns true.
    /// </summary>
    public static bool TraceRayScalar(Ray ray, float maxDist, int excludeTerrain = -1, int excludeBrush = -1, int selfEntityGroup = -1)
    {
        return TraceRayScalar(ray, maxDist, out _, excludeTerrain, excludeBrush, selfEntityGroup);
    }
    public static bool TraceRayScalar(Ray ray, float maxDist, out Vector3 hitPos, int excludeTerrain = -1, int excludeBrush = -1, int selfEntityGroup = -1)
    {
        hitPos = default;

        if (rootIndex < 0) return false;

        ray = new Ray(ray.Position, Vector3.Normalize(ray.Direction));

        float bestT = maxDist;
        bool found = false;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = rootIndex;

        while (sp > 0)
        {
            int ni = stack[--sp];
            ref readonly var node = ref nodes[ni];

            float? aabbHit = ray.Intersects(node.bounds);
            if (!aabbHit.HasValue || aabbHit.Value >= bestT) continue;

            if (node.left == -1)
            {
                int end = node.triStart + node.triCount;
                for (int i = node.triStart; i < end; i++)
                {
                    bool isTerrain = triIsTerrain[i];
                    int srcIdx = triSourceIndex[i];
                    if (isTerrain && srcIdx == excludeTerrain) continue;
                    if (!isTerrain && srcIdx == excludeBrush) continue;

                    int triGroup = triEntityGroup[i];
                    if (triGroup >= 0 && triGroup != selfEntityGroup) continue;

                    if (IntersectTriangle(ray, triPositions[i * 3], triPositions[i * 3 + 1], triPositions[i * 3 + 2], out float t)
                        && t > 1e-4f && t < bestT)
                    {
                        bestT = t;
                        found = true;
                    }
                }
            }
            else
            {
                stack[sp++] = node.left;
                stack[sp++] = node.right;
            }
        }

        if (found) hitPos = ray.Position + ray.Direction * bestT;
        return found;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IntersectTriangle(Ray ray,
        Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        t = 0f;
        Vector3 e1 = v1 - v0;
        Vector3 e2 = v2 - v0;
        Vector3 h = Vector3.Cross(ray.Direction, e2);
        float a = Vector3.Dot(e1, h);
        if (MathF.Abs(a) < 1e-8f) return false;

        float inv = 1f / a;
        Vector3 s = ray.Position - v0;
        float u = inv * Vector3.Dot(s, h);
        if (u < 0f || u > 1f) return false;

        Vector3 q = Vector3.Cross(s, e1);
        float v = inv * Vector3.Dot(ray.Direction, q);
        if (v < 0f || u + v > 1f) return false;

        t = inv * Vector3.Dot(e2, q);
        return t > 0f;
    }
}
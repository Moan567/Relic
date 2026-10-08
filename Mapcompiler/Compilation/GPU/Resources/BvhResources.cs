using Microsoft.Xna.Framework;
using Rockwall;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU.Resources;
public struct BvhTriangle
{
    public Vector3 V0, V1, V2;
    public Vector2 Uv0, Uv1, Uv2;
    public Vector3 Albedo;
    public int SourceBrush;
    public int EntityGroup;
    public bool IsSkybox;
}

[StructLayout(LayoutKind.Sequential, Size = 48)]
public struct BvhNode
{
    public Vector3 BoundsMin;
    public uint LeftFirst;
    public Vector3 BoundsMax;
    public uint TriCount;
    public uint MissIndex;
}

public sealed class BvhResources : IDisposable
{
    private readonly GpuBuffer nodes;
    private readonly GpuBuffer triV0, triV1, triV2;
    private readonly GpuBuffer triUv0, triUv1, triUv2;
    private readonly GpuBuffer triAlbedo;
    private readonly GpuBuffer triSourceBrush, triEntityGroup, triIsSkybox;

    public BvhResources(GL gl, Brush[] brushes, Terrain[] terrains, Color[] matColors, List<BvhTriangle> extraTriangles = null)
    {
        var tris = BuildUnifiedTriangleList(brushes, terrains, matColors);
        if (extraTriangles != null) tris.AddRange(extraTriangles);

        var builtNodes = BuildBvh(tris, out int[] triIndices);

        int n = triIndices.Length;
        var v0 = new Vector3[n];
        var v1 = new Vector3[n];
        var v2 = new Vector3[n];
        var uv0 = new Vector2[n];
        var uv1 = new Vector2[n];
        var uv2 = new Vector2[n];
        var albedoFlat = new float[n * 3];
        var sourceBrush = new int[n];
        var entityGroup = new int[n];
        var isSkybox = new int[n];

        for (int i = 0; i < n; i++)
        {
            var tri = tris[triIndices[i]];
            v0[i] = tri.V0;
            v1[i] = tri.V1;
            v2[i] = tri.V2;
            uv0[i] = tri.Uv0;
            uv1[i] = tri.Uv1;
            uv2[i] = tri.Uv2;
            albedoFlat[i * 3] = tri.Albedo.X;
            albedoFlat[i * 3 + 1] = tri.Albedo.Y;
            albedoFlat[i * 3 + 2] = tri.Albedo.Z;
            sourceBrush[i] = tri.SourceBrush;
            entityGroup[i] = tri.EntityGroup;
            isSkybox[i] = tri.IsSkybox ? 1 : 0;
        }

        nodes = new GpuBuffer(gl);
        nodes.Upload<BvhNode>(builtNodes.ToArray());

        triV0 = new GpuBuffer(gl); triV0.Upload<Vector3>(v0);
        triV1 = new GpuBuffer(gl); triV1.Upload<Vector3>(v1);
        triV2 = new GpuBuffer(gl); triV2.Upload<Vector3>(v2);

        triUv0 = new GpuBuffer(gl); triUv0.Upload<Vector2>(uv0);
        triUv1 = new GpuBuffer(gl); triUv1.Upload<Vector2>(uv1);
        triUv2 = new GpuBuffer(gl); triUv2.Upload<Vector2>(uv2);

        triAlbedo = new GpuBuffer(gl); triAlbedo.Upload<float>(albedoFlat);

        triSourceBrush = new GpuBuffer(gl); triSourceBrush.Upload<int>(sourceBrush);
        triEntityGroup = new GpuBuffer(gl); triEntityGroup.Upload<int>(entityGroup);
        triIsSkybox = new GpuBuffer(gl); triIsSkybox.Upload<int>(isSkybox);
    }
    public void Bind()
    {
        nodes.BindBase(GpuBindings.BvhNodes);
        triV0.BindBase(GpuBindings.TriV0);
        triV1.BindBase(GpuBindings.TriV1);
        triV2.BindBase(GpuBindings.TriV2);
        triUv0.BindBase(GpuBindings.TriUv0);
        triUv1.BindBase(GpuBindings.TriUv1);
        triUv2.BindBase(GpuBindings.TriUv2);
        triAlbedo.BindBase(GpuBindings.TriAlbedo);
        triSourceBrush.BindBase(GpuBindings.TriSourceBrush);
        triEntityGroup.BindBase(GpuBindings.TriEntityGroup);
        triIsSkybox.BindBase(GpuBindings.TriIsSkybox);
    }

    private static List<BvhTriangle> BuildUnifiedTriangleList(Brush[] brushes, Terrain[] terrains, Color[] matColors)
    {
        var tris = new List<BvhTriangle>(brushes.Length * 4);

        for (int b = 0; b < brushes.Length; b++)
        {
            if (brushes[b].IsClip || brushes[b].IsLightNodeVolume || brushes[b].IsTrigger) continue;

            bool skybox = brushes[b].IsSkybox;

            int entityGroup = TriangleOccluder.GetBrushEntityGroup(b);

            for (int f = 0; f < brushes[b].Faces.Length; f++)
            {
                var face = brushes[b].Faces[f];

                Vector3 albedo = matColors[face.Surface].ToVector3() / 255f;

                for (int t = 0; t < face.Indices.Length; t += 3)
                {
                    int i0 = face.Indices[t];
                    int i1 = face.Indices[t + 1];
                    int i2 = face.Indices[t + 2];

                    Vector3 v0 = brushes[b].Vertices[i0] + brushes[b].Position;
                    Vector3 v1 = brushes[b].Vertices[i1] + brushes[b].Position;
                    Vector3 v2 = brushes[b].Vertices[i2] + brushes[b].Position;

                    tris.Add(new BvhTriangle
                    {
                        V0 = v0,
                        V1 = v1,
                        V2 = v2,
                        Uv0 = brushes[b].LightmapUVs[i0],
                        Uv1 = brushes[b].LightmapUVs[i1],
                        Uv2 = brushes[b].LightmapUVs[i2],
                        Albedo = albedo,
                        SourceBrush = b,
                        EntityGroup = entityGroup,
                        IsSkybox = skybox
                    });
                }
            }
        }

        for (int i = 0; i < terrains.Length; i++)
        {
            Vector3 albedo = matColors[terrains[i].Surface].ToVector3() / 255f;

            for (int t = 0; t < terrains[i].Triangles.Length; t += 3)
            {
                int i0 = terrains[i].Triangles[t];
                int i1 = terrains[i].Triangles[t + 1];
                int i2 = terrains[i].Triangles[t + 2];

                tris.Add(new BvhTriangle
                {
                    V0 = terrains[i].Vertices[i0].Position,
                    V1 = terrains[i].Vertices[i1].Position,
                    V2 = terrains[i].Vertices[i2].Position,
                    Uv0 = terrains[i].lightmapUvs[i0],
                    Uv1 = terrains[i].lightmapUvs[i1],
                    Uv2 = terrains[i].lightmapUvs[i2],
                    Albedo = albedo,
                    SourceBrush = -1,
                    EntityGroup = -1,
                    IsSkybox = false
                });
            }
        }

        return tris;
    }

    private static List<BvhNode> BuildBvh(List<BvhTriangle> tris, out int[] triIndices)
    {
        int n = tris.Count;
        var indices = new int[n];
        for (int i = 0; i < n; i++) indices[i] = i;

        var centroids = new Vector3[n];
        var triBoundsMin = new Vector3[n];
        var triBoundsMax = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var tri = tris[i];
            triBoundsMin[i] = Vector3.Min(tri.V0, Vector3.Min(tri.V1, tri.V2));
            triBoundsMax[i] = Vector3.Max(tri.V0, Vector3.Max(tri.V1, tri.V2));
            centroids[i] = (tri.V0 + tri.V1 + tri.V2) / 3f;
        }

        var nodes = new List<BvhNode>(n * 2);
        nodes.Add(new BvhNode());

        void UpdateBounds(int nodeIdx)
        {
            var node = nodes[nodeIdx];
            node.BoundsMin = new Vector3(float.MaxValue);
            node.BoundsMax = new Vector3(float.MinValue);

            for (uint i = 0; i < node.TriCount; i++)
            {
                int triIdx = indices[node.LeftFirst + i];
                node.BoundsMin = Vector3.Min(node.BoundsMin, triBoundsMin[triIdx]);
                node.BoundsMax = Vector3.Max(node.BoundsMax, triBoundsMax[triIdx]);
            }

            nodes[nodeIdx] = node;
        }

        float SurfaceArea(Vector3 min, Vector3 max)
        {
            Vector3 e = max - min;
            if (e.X < 0f || e.Y < 0f || e.Z < 0f) return 0f;
            return 2f * (e.X * e.Y + e.Y * e.Z + e.Z * e.X);
        }

        const int BinCount = 16;

        bool FindBestSplit(int nodeIdx, out int bestAxis, out float bestSplitPos, out float bestCost)
        {
            var node = nodes[nodeIdx];
            bestAxis = -1;
            bestSplitPos = 0f;
            bestCost = float.MaxValue;

            Vector3 centroidMin = new Vector3(float.MaxValue);
            Vector3 centroidMax = new Vector3(float.MinValue);
            for (uint i = 0; i < node.TriCount; i++)
            {
                int triIdx = indices[node.LeftFirst + i];
                centroidMin = Vector3.Min(centroidMin, centroids[triIdx]);
                centroidMax = Vector3.Max(centroidMax, centroids[triIdx]);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                float axisMin = centroidMin.GetElement(axis);
                float axisMax = centroidMax.GetElement(axis);
                if (axisMax - axisMin < 1e-6f) continue;

                var binCount = new int[BinCount];
                var binMin = new Vector3[BinCount];
                var binMax = new Vector3[BinCount];
                for (int b = 0; b < BinCount; b++)
                {
                    binMin[b] = new Vector3(float.MaxValue);
                    binMax[b] = new Vector3(float.MinValue);
                }

                float scale = BinCount / (axisMax - axisMin);

                for (uint i = 0; i < node.TriCount; i++)
                {
                    int triIdx = indices[node.LeftFirst + i];
                    int bin = Math.Clamp((int)((centroids[triIdx].GetElement(axis) - axisMin) * scale), 0, BinCount - 1);

                    binCount[bin]++;
                    binMin[bin] = Vector3.Min(binMin[bin], triBoundsMin[triIdx]);
                    binMax[bin] = Vector3.Max(binMax[bin], triBoundsMax[triIdx]);
                }

                var leftCount = new int[BinCount - 1];
                var leftArea = new float[BinCount - 1];
                var rightCount = new int[BinCount - 1];
                var rightArea = new float[BinCount - 1];

                Vector3 lMin = new Vector3(float.MaxValue), lMax = new Vector3(float.MinValue);
                int lSum = 0;
                for (int b = 0; b < BinCount - 1; b++)
                {
                    lSum += binCount[b];
                    lMin = Vector3.Min(lMin, binMin[b]);
                    lMax = Vector3.Max(lMax, binMax[b]);
                    leftCount[b] = lSum;
                    leftArea[b] = SurfaceArea(lMin, lMax);
                }

                Vector3 rMin = new Vector3(float.MaxValue), rMax = new Vector3(float.MinValue);
                int rSum = 0;
                for (int b = BinCount - 1; b >= 1; b--)
                {
                    rSum += binCount[b];
                    rMin = Vector3.Min(rMin, binMin[b]);
                    rMax = Vector3.Max(rMax, binMax[b]);
                    rightCount[b - 1] = rSum;
                    rightArea[b - 1] = SurfaceArea(rMin, rMax);
                }

                for (int b = 0; b < BinCount - 1; b++)
                {
                    if (leftCount[b] == 0 || rightCount[b] == 0) continue;

                    float cost = leftCount[b] * leftArea[b] + rightCount[b] * rightArea[b];
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestSplitPos = axisMin + (b + 1) / scale;
                    }
                }
            }

            return bestAxis != -1;
        }

        void Subdivide(int nodeIdx, uint escapeIndex)
        {
            var node = nodes[nodeIdx];
            node.MissIndex = escapeIndex;
            nodes[nodeIdx] = node;

            if (node.TriCount <= 2)
            {
                return;
            }

            float parentCost = node.TriCount * SurfaceArea(node.BoundsMin, node.BoundsMax);

            if (!FindBestSplit(nodeIdx, out int axis, out float splitPos, out float splitCost) || splitCost >= parentCost)
            {
                return;
            }

            int i = (int)node.LeftFirst;
            int j = i + (int)node.TriCount - 1;
            while (i <= j)
            {
                if (centroids[indices[i]].GetElement(axis) < splitPos)
                {
                    i++;
                }
                else
                {
                    (indices[i], indices[j]) = (indices[j], indices[i]);
                    j--;
                }
            }

            int leftCount = i - (int)node.LeftFirst;
            if (leftCount == 0 || leftCount == (int)node.TriCount)
            {
                return;
            }

            int leftIdx = nodes.Count;
            nodes.Add(new BvhNode { LeftFirst = node.LeftFirst, TriCount = (uint)leftCount });
            nodes.Add(new BvhNode { LeftFirst = (uint)i, TriCount = node.TriCount - (uint)leftCount });
            uint rightIdx = (uint)(leftIdx + 1);

            node.LeftFirst = (uint)leftIdx;
            node.TriCount = 0;
            nodes[nodeIdx] = node;

            UpdateBounds(leftIdx);
            UpdateBounds((int)rightIdx);

            Subdivide(leftIdx, rightIdx);
            Subdivide((int)rightIdx, escapeIndex);
        }

        nodes[0] = new BvhNode { LeftFirst = 0, TriCount = (uint)n };
        UpdateBounds(0);
        Subdivide(0, uint.MaxValue);

        triIndices = indices;
        return nodes;
    }

    public void Dispose()
    {
        nodes.Dispose();
        triV0.Dispose(); triV1.Dispose(); triV2.Dispose();
        triUv0.Dispose(); triUv1.Dispose(); triUv2.Dispose();
        triAlbedo.Dispose();
        triSourceBrush.Dispose(); triEntityGroup.Dispose(); triIsSkybox.Dispose();
    }
}
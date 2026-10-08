using Microsoft.Xna.Framework;
using Silk.NET.OpenGL;
using System;
using System.Runtime.InteropServices;

namespace MapCompiler.Compilation.GPU.Resources;

[StructLayout(LayoutKind.Sequential)]
public struct GridCellGpu
{
    public Vector3 Color; public float Count;
    public Vector3 Normal; public float Pad0;
    public Vector3 Position; public float Pad1;
}

public sealed class PatchBucketGridResources : IDisposable
{
    private readonly GpuBuffer bucketOffsets;
    private readonly GpuBuffer bucketIndices;

    public Vector3 GridOrigin { get; }
    public float CellSize { get; }
    public int DimsX { get; }
    public int DimsY { get; }
    public int DimsZ { get; }
    public int CellCount => DimsX * DimsY * DimsZ;

    public PatchBucketGridResources(GL gl, Patch[] patches, Vector3 worldMin, Vector3 worldMax, float cellSize)
    {
        GridOrigin = worldMin - Vector3.One * cellSize;
        CellSize = cellSize;

        Vector3 extent = (worldMax - worldMin) + Vector3.One * cellSize * 2f;
        DimsX = Math.Max(1, (int)MathF.Ceiling(extent.X / cellSize));
        DimsY = Math.Max(1, (int)MathF.Ceiling(extent.Y / cellSize));
        DimsZ = Math.Max(1, (int)MathF.Ceiling(extent.Z / cellSize));

        var (offsets, indices) = PatchSystem.BuildSortedGridBuckets(patches, GridOrigin, cellSize, DimsX, DimsY, DimsZ);

        bucketOffsets = new GpuBuffer(gl);
        bucketOffsets.Upload<int>(offsets);

        bucketIndices = new GpuBuffer(gl);
        bucketIndices.Upload<int>(indices);
    }

    public void Bind()
    {
        bucketOffsets.BindBase(GpuBindings.PatchBucketOffsets);
        bucketIndices.BindBase(GpuBindings.PatchBucketIndices);
    }

    public void Dispose()
    {
        bucketOffsets.Dispose();
        bucketIndices.Dispose();
    }
}
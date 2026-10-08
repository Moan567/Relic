using System;

namespace MapCompiler.Compilation.GPU.Resources;
public sealed class PropVertexResources : IDisposable
{
    public GpuBuffer Positions { get; }
    public GpuBuffer Normals { get; }
    public int Count { get; }

    public PropVertexResources(GpuBuffer positions, GpuBuffer normals, int count)
    {
        Positions = positions;
        Normals = normals;
        Count = count;
    }

    public void Bind()
    {
        Positions.BindBase(GpuBindings.PropVertexPositions);
        Normals.BindBase(GpuBindings.PropVertexNormals);
    }

    public void Dispose()
    {
        Positions.Dispose();
        Normals.Dispose();
    }
}
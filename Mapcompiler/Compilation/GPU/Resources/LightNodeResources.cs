using Microsoft.Xna.Framework;
using Rockwall;
using Silk.NET.OpenAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU.Resources;
public sealed class LightNodePositions : IDisposable
{
    public GpuBuffer Buffer { get; }
    public int Count { get; }

    public LightNodePositions(GpuBuffer buffer, int count)
    {
        Buffer = buffer;
        Count = count;
    }

    public void Dispose() => Buffer.Dispose();
}
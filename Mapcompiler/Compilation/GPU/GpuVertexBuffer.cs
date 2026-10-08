using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;

public sealed class GpuVertexBuffer : IDisposable
{
    private readonly GL gl;
    public uint Handle { get; private set; }
    public int VertexCount { get; private set; }

    public GpuVertexBuffer(GL gl)
    {
        this.gl = gl;
        Handle = gl.GenBuffer();
    }

    public unsafe void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        VertexCount = data.Length;
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, Handle);
        fixed (byte* ptr = bytes)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)bytes.Length, ptr, BufferUsageARB.StaticDraw);
        }

        var error = gl.GetError();
        if (error != GLEnum.NoError)
        {
            throw new InvalidOperationException($"Vertex buffer upload failed ({bytes.Length:N0} bytes): {error}");
        }
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            gl.DeleteBuffer(Handle);
            Handle = 0;
        }
    }
}
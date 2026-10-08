using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;

public readonly struct VertexAttribute
{
    public readonly uint Location;
    public readonly int ComponentCount;
    public readonly VertexAttribPointerType Type;
    public readonly uint Stride;
    public readonly uint Offset;
    public readonly bool IsInteger;

    public VertexAttribute(uint location, int componentCount, VertexAttribPointerType type, uint stride, uint offset, bool isInteger = false)
    {
        Location = location; ComponentCount = componentCount; Type = type; Stride = stride; Offset = offset; IsInteger = isInteger;
    }
}

public sealed class GpuVertexArray : IDisposable
{
    private readonly GL gl;
    public uint Handle { get; private set; }

    public GpuVertexArray(GL gl, GpuVertexBuffer vertexBuffer, params VertexAttribute[] attributes)
    {
        this.gl = gl;
        Handle = gl.GenVertexArray();

        gl.BindVertexArray(Handle);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer.Handle);

        foreach (var attr in attributes)
        {
            gl.EnableVertexAttribArray(attr.Location);
            unsafe
            {
                if (attr.IsInteger)
                    gl.VertexAttribIPointer(attr.Location, attr.ComponentCount, VertexAttribIType.Int, attr.Stride, (void*)attr.Offset);
                else
                    gl.VertexAttribPointer(attr.Location, attr.ComponentCount, attr.Type, false, attr.Stride, (void*)attr.Offset);
            }
        }

        gl.BindVertexArray(0);
    }

    public void Bind()
    {
        gl.BindVertexArray(Handle);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            gl.DeleteVertexArray(Handle);
            Handle = 0;
        }
    }
}
using Silk.NET.OpenGL;
using System;

public sealed class GpuTexture : IDisposable
{
    private readonly GL gl;
    public uint Handle { get; private set; }
    public GLEnum Format { get; }
    public unsafe GpuTexture(GL gl, int width, int height, GLEnum internalFormat, ReadOnlySpan<byte> data = default, PixelFormat pixelFormat = PixelFormat.Rgba, PixelType pixelType = PixelType.Float)
    {
        this.gl = gl;
        Format = internalFormat;
        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        fixed (byte* ptr = data)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)internalFormat, (uint)width, (uint)height, 0, pixelFormat, pixelType, ptr);
        }
    }

    public void BindImage(uint unit, bool readOnly)
    {
        gl.BindImageTexture(unit, Handle, 0, false, 0, readOnly ? GLEnum.ReadOnly : GLEnum.WriteOnly, Format);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            gl.DeleteTexture(Handle);
            Handle = 0;
        }
    }
}
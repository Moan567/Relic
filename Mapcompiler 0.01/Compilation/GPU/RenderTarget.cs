using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;
public readonly struct AttachmentDesc
{
    public readonly GLEnum InternalFormat;
    public readonly PixelFormat PixelFormat;
    public readonly PixelType PixelType;
    private AttachmentDesc(GLEnum i, PixelFormat f, PixelType t) { InternalFormat = i; PixelFormat = f; PixelType = t; }
    public static AttachmentDesc Float(GLEnum internalFormat) => new(internalFormat, PixelFormat.Rgba, PixelType.Float);
    public static AttachmentDesc Int() => new(GLEnum.R32i, PixelFormat.RedInteger, PixelType.Int);
}
public sealed class RenderTarget : IDisposable
{
    private readonly GL gl;
    private readonly List<GpuTexture> colorAttachments = new();
    public uint FboHandle { get; private set; }
    public int Width { get; }
    public int Height { get; }

    public RenderTarget(GL gl, int width, int height, params AttachmentDesc[] attachments)
    {
        this.gl = gl; Width = width; Height = height;
        FboHandle = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, FboHandle);

        var drawBuffers = new List<GLEnum>();
        for (int i = 0; i < attachments.Length; i++)
        {
            var texture = new GpuTexture(gl, width, height, attachments[i].InternalFormat, default, attachments[i].PixelFormat, attachments[i].PixelType);
            colorAttachments.Add(texture);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, TextureTarget.Texture2D, texture.Handle, 0);
            drawBuffers.Add(GLEnum.ColorAttachment0 + i);
        }

        unsafe { fixed (GLEnum* p = drawBuffers.ToArray()) gl.DrawBuffers((uint)drawBuffers.Count, (DrawBufferMode*)p); }

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete) throw new InvalidOperationException($"RenderTarget incomplete: {status}");
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public GpuTexture GetAttachment(int index) => colorAttachments[index];

    public void Bind()
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, FboHandle);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void Clear()
    {
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);
    }

    public static void Unbind(GL gl)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public GpuTexture[] DetachAttachments()
    {
        var result = colorAttachments.ToArray();
        colorAttachments.Clear();
        return result;
    }

    public void Dispose()
    {
        foreach (var tex in colorAttachments)
        {
            tex.Dispose();
        }
        if (FboHandle != 0)
        {
            gl.DeleteFramebuffer(FboHandle);
            FboHandle = 0;
        }
    }
}
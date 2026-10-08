using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System;

namespace MapCompiler.Compilation.GPU;
public sealed class GpuContext : IDisposable
{
    public GL GL { get; private set; }
    private IWindow window;

    private GpuContext(GL gl, IWindow window)
    {
        GL = gl;
        this.window = window;
    }

    public static GpuContext TryCreate()
    {
        try
        {
            var options = WindowOptions.Default;
            options.IsVisible = false;
            options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(4, 3));

            var window = Window.Create(options);
            window.Initialize();
            var gl = GL.GetApi(window);

            return new GpuContext(gl, window);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        GL = null;
        window?.Dispose();
        window = null;
    }
}
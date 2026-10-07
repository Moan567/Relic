using System;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Mathematics;

namespace Rolonin.Renderer
{
    internal static class Program
    {
        public static void Main()
        {
            var gws = GameWindowSettings.Default;
            var nws = new NativeWindowSettings()
            {
                Size = new Vector2i(1280, 720),
                Title = "Rolonin Renderer",
            };

            using var win = new RendererWindow(gws, nws);
            win.Run();
        }
    }
}

// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using Microsoft.Xna.Framework.Graphics;
using System;

namespace Microsoft.Xna.Framework
{
    public partial class GraphicsDeviceManager
    {
        private readonly struct GLContextVersion
        {
            public readonly int Major;
            public readonly int Minor;
            public readonly int Profile;

            public GLContextVersion(int major, int minor, int profile)
            {
                Major = major;
                Minor = minor;
                Profile = profile;
            }
        }

        private static GLContextVersion DetectGLVersion()
        {
            var versions = new[]
            {
                new GLContextVersion(3, 2, 0x0001), // Core
                new GLContextVersion(3, 0, 0x0001), // Core
                new GLContextVersion(2, 1, 0x0002), // Compatibility
                new GLContextVersion(2, 0, 0x0002), // Compatibility
            };


            foreach (var version in versions)
            {
                Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextMajorVersion, version.Major);
                Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextMinorVersion, version.Minor);
                Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextProfileMAsl, version.Profile);

                Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextFlags, 0);

                var window = Sdl.Window.Create("", 0, 0, 1, 1, Sdl.Window.State.OpenGL | Sdl.Window.State.Hidden);

                if (window == IntPtr.Zero)
                    continue;

                var context = Sdl.GL.CreateContext(window);

                if (context != IntPtr.Zero)
                {
                    Sdl.GL.DeleteContext(context);
                    Sdl.Window.Destroy(window);

                    return version;
                }

                Sdl.Window.Destroy(window);
            }

            throw new PlatformNotSupportedException("OpenGL 2.0 or newer is required.");
        }

        partial void PlatformInitialize(PresentationParameters presentationParameters)
        {
            var backBufferFormat = _game.graphicsDeviceManager.PreferredBackBufferFormat;
            var surfaceFormat = backBufferFormat.GetColorFormat();
            var depthStencilFormat = _game.graphicsDeviceManager.PreferredDepthStencilFormat;

            // TODO Need to get this data from the Presentation Parameters
            Sdl.GL.SetAttribute(Sdl.GL.Attribute.RedSize, surfaceFormat.R);
            Sdl.GL.SetAttribute(Sdl.GL.Attribute.GreenSize, surfaceFormat.G);
            Sdl.GL.SetAttribute(Sdl.GL.Attribute.BlueSize, surfaceFormat.B);
            Sdl.GL.SetAttribute(Sdl.GL.Attribute.AlphaSize, surfaceFormat.A);

            if (backBufferFormat == SurfaceFormat.ColorSRgb || backBufferFormat == SurfaceFormat.Bgr32SRgb || backBufferFormat == SurfaceFormat.Bgra32SRgb)
            {
                Sdl.GL.SetAttribute(Sdl.GL.Attribute.FramebufferSRGBCapable, 1);
            }

            switch (depthStencilFormat)
            {
                case DepthFormat.None:
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.DepthSize, 0);
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.StencilSize, 0);
                    break;
                case DepthFormat.Depth16:
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.DepthSize, 16);
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.StencilSize, 0);
                    break;
                case DepthFormat.Depth24:
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.DepthSize, 24);
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.StencilSize, 0);
                    break;
                case DepthFormat.Depth24Stencil8:
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.DepthSize, 24);
                    Sdl.GL.SetAttribute(Sdl.GL.Attribute.StencilSize, 8);
                    break;
            }

            Sdl.GL.SetAttribute(Sdl.GL.Attribute.DoubleBuffer, 1);

            var glVersion = DetectGLVersion();

            Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextMajorVersion, glVersion.Major);
            Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextMinorVersion, glVersion.Minor);
            Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextProfileMAsl, glVersion.Profile);

            Sdl.GL.SetAttribute(Sdl.GL.Attribute.ContextFlags, 0);

            if (presentationParameters.MultiSampleCount > 0)
            {
                Sdl.GL.SetAttribute(Sdl.GL.Attribute.MultiSampleBuffers, 1);
                Sdl.GL.SetAttribute(Sdl.GL.Attribute.MultiSampleSamples, presentationParameters.MultiSampleCount);
            }

            ((SdlGameWindow)SdlGameWindow.Instance).CreateWindow();
        }
    }
}

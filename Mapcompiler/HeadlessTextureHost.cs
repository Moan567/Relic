using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Silk.NET.Windowing;
using System.Collections.Generic;
namespace MapCompiler;
public class HeadlessTextureHost : Game
{
    readonly GraphicsDeviceManager graphics;
    readonly string[] textureNames;
    public Dictionary<string, System.Drawing.Bitmap> Results = new();
    public Dictionary<string, (int w, int h)> Sizes = new();

    public HeadlessTextureHost(string contentRoot, string[] textureNames)
    {
        this.textureNames = textureNames;
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 64,
            PreferredBackBufferHeight = 64,
            SynchronizeWithVerticalRetrace = false
        };
        Content.RootDirectory = contentRoot;
        IsFixedTimeStep = false;
    }

    protected override void Initialize()
    {
        base.Initialize();
        Window.Position = new Point(-32000, -32000); // off-screen; DesktopGL honors this
    }

    protected override void LoadContent()
    {
        using var sb = new SpriteBatch(GraphicsDevice);

        foreach (var name in textureNames)
        {
            var tex = Content.Load<Texture2D>(name);

            using var rt = new RenderTarget2D(GraphicsDevice, tex.Width, tex.Height, false, SurfaceFormat.Color, DepthFormat.None);
            GraphicsDevice.SetRenderTarget(rt);
            GraphicsDevice.Clear(Color.Transparent);
            sb.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
            sb.Draw(tex, Vector2.Zero, Color.White);
            sb.End();
            GraphicsDevice.SetRenderTarget(null);

            var data = new Color[tex.Width * tex.Height];
            rt.GetData(data);

            var bmp = new System.Drawing.Bitmap(tex.Width, tex.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new System.Drawing.Rectangle(0, 0, tex.Width, tex.Height);
            var locked = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);

            var bytes = new byte[data.Length * 4];
            for (int i = 0; i < data.Length; i++)
            {
                var c = data[i];
                int o = i * 4;
                bytes[o + 0] = c.B;
                bytes[o + 1] = c.G;
                bytes[o + 2] = c.R;
                bytes[o + 3] = c.A;
            }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, locked.Scan0, bytes.Length);
            bmp.UnlockBits(locked);

            Results[name] = bmp;
        }

        Exit();
    }
}
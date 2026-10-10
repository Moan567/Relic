using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common;
public interface IEditorScene
{
    void Attach(EditorHost host);
    void Detach();
    void Resize(int width, int height);
    void Update(GameTime gameTime);
    void Draw(GameTime gameTime);
}
public sealed class EditorHost : Game
{
    public static EditorHost Instance => Current;

    readonly Stopwatch stopwatch = new();
    readonly GameTime gameTime = new();

    public static EditorHost Current { get; private set; } = null!;

    public EditorHost()
    {
        Current = this;
        Services.AddService(GDLoader.Create(this));
        Content.RootDirectory = "Content";

        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
    }
    public void RenderScene(IEditorScene scene, RenderTarget2D target, int width, int height)
    {
        gameTime.ElapsedGameTime = stopwatch.Elapsed;
        gameTime.TotalGameTime += gameTime.ElapsedGameTime;
        stopwatch.Restart();

        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Viewport = new Viewport(0, 0, width, height);

        float delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
        MouseManager.Update(delta);
        KeyboardManager.Update();

        scene.Update(gameTime);
        scene.Draw(gameTime);

        GraphicsDevice.SetRenderTarget(null);
    }
}
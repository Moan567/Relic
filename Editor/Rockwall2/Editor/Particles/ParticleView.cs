using Avalonia.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Particles;
public class ParticleView : IEditorScene
{
    public static ParticleView Instance { get; private set; } = null!;
    EditorHost host;

    private PlaneGrid grid = null!;
    private SpriteBatch spriteBatch = null!;

    public ParticleSpawner? ActiveSpawner;
    public int SelectedSubsystem;

    public bool IsPaused { get; set; }
    int targetWidth, targetHeight;
    private Orbit3DCamera orbitCamera = new Orbit3DCamera();

    /// <summary>Raised after Restart() or OpenParticle() sets a new spawner.</summary>
    public event Action<ParticleSpawner?>? ActiveSpawnerChanged;

    /// <summary>Raised every Update tick with the system's current elapsed time.</summary>
    public event Action<float>? PlaybackTimeChanged;

    /// <summary>Raised whenever IsPaused changes.</summary>
    public event Action<bool>? PausedChanged;

    public ParticleView()
    {
        Instance = this;
    }

    public void Restart()
    {
        if (ActiveSpawner == null) return;

        // Sync behavior array from live subsystems so in-editor edits survive.
        ActiveSpawner.system.behavior.subsystemBehaviors =
            ActiveSpawner.system.particleSubsystems.Select(s => s.behavior).ToArray();

        var behavior = ActiveSpawner.system.behavior;
        ParticleManager.Clear();
        int id = ParticleManager.SpawnParticleSystem(Vector3.Zero, behavior);
        ActiveSpawner = ParticleManager.GetSpawner(id);
        ActiveSpawner!.system.isLooping = true;
        IsPaused = false;

        ActiveSpawnerChanged?.Invoke(ActiveSpawner);
    }

    public void Attach(EditorHost host)
    {
        this.host = host;
        spriteBatch = new SpriteBatch(host.GraphicsDevice);
        grid = new PlaneGrid(host.GraphicsDevice, 0.5f, 25, new Plane(Vector3.Up,0));
        orbitCamera.posOffset = Vector3.Zero;
    }

    public void Detach()
    {
    }

    public void Draw(GameTime gameTime)
    {
        if (!MainWindow.Instance.particleEditor.IsVisible) return;

        host.GraphicsDevice.Clear(new Color(15, 15, 15));

        host.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        host.GraphicsDevice.BlendState = BlendState.NonPremultiplied;
        host.GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;

        host.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

        grid.Draw(orbitCamera.viewMatrix, orbitCamera.projectionMatrix, Vector3.Zero, 0.5f, true);

        if (ActiveSpawner == null) return;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        ParticleManager.RenderSystems(orbitCamera.position, orbitCamera.worldMatrix, orbitCamera.viewMatrix, orbitCamera.projectionMatrix,dt);
        //MainWindow.Instance.particleEditor.Refresh();
    }

    public void Resize(int width, int height)
    {
        targetWidth = width;
        targetHeight = height;
        orbitCamera.AspectRatio = targetWidth / (float)targetHeight;
    }

    public Vector2? GlobalToLocal(Vector2 global)
    {
        var rect = MainWindow.Instance.particleEditor.gameControl.Bounds;

        var local = global - new Vector2((float)rect.X, (float)rect.Y);
        if (local.X < 0 || local.Y < 0 || local.X > rect.Width || local.Y > rect.Height) return null;
        return local;
    }
    public void Update(GameTime gameTime)
    {
        if (!MainWindow.Instance.particleEditor.IsVisible) return;

        var mousePos = MouseManager.GetXnaPositionRelativeTo(
            MainWindow.Instance.particleEditor.gameControl).ToVector2();

        orbitCamera.blockOrbit = !MainWindow.Instance.particleEditor.IsPointerOver || !GlobalToLocal(mousePos).HasValue;

        orbitCamera.Update(gameTime);

        if (ActiveSpawner == null) return;

        //if (KeyboardManager.IsDown(Key.LeftCtrl) && KeyboardManager.IsPressed(Key.S))
        //    FileHandler.SaveParticle(null!, null!);

        if (!IsPaused)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            ParticleManager.UpdateSystems(dt);
            PlaybackTimeChanged?.Invoke(ActiveSpawner.system.life);
        }
    }
}

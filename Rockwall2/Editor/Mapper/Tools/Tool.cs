using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Mapper.ViewportManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Tools;

public abstract class Tool
{
    protected SpriteBatch spriteBatch => MapperView.Instance.SpriteBatch;
    protected BasicEffect basicEffect => MapperView.Instance.BasicEffect;
    protected GraphicsDevice graphicsDevice => EditorHost.Instance.GraphicsDevice;

    protected EditorViewport ActiveViewport => ViewportManager.Active;
    protected EditorViewport RenderingViewport => ViewportManager.Rendering;
    protected bool IsUsedIn2D => ViewportManager.Active.Is2D;
    protected bool IsRenderingIn2D => ViewportManager.Rendering.Is2D;
    protected Vector2 MouseLocalF => ViewportManager.MouseLocal;
    protected Point MouseLocal => ViewportManager.MouseLocal.ToPoint();
    protected Ray SceneRay => ViewportManager.ActiveRay(graphicsDevice);

    public abstract void OnSelected();
    public abstract void OnDeselected();
    public abstract void OnUpdate(float delta);
    public abstract void OnRender(float delta);

    protected Vector3 LocalToWorld(EditorViewport vp, Vector2 local)
    {
        float gs = Transformable.GridSize;
        float wx = MathF.Round(((local.X - vp.Width * 0.5f) / vp.Zoom + vp.Pan.X) / gs) * gs;
        float wy = MathF.Round(((vp.Height * 0.5f - local.Y) / vp.Zoom + vp.Pan.Y) / gs) * gs;
        return wx * vp.RightAxis + wy * vp.UpAxis;
    }
}
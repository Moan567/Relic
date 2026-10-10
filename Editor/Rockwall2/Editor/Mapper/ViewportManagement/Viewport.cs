using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.ViewportManagement;

public enum ViewportType { Perspective, Top, Front, Side }

public class EditorViewport
{
    public readonly ViewportType Type;
    public bool IsPerspective => Type == ViewportType.Perspective;
    public bool Is2D => Type != ViewportType.Perspective;

    public string Label => Type switch
    {
        ViewportType.Top => "Top  (X / Z)",
        ViewportType.Front => "Front  (X / Y)",
        ViewportType.Side => "Side  (Z / Y)",
        _ => "3D Perspective",
    };

    // World axis that maps to screen-right, screen-up, and out-of-screen respectively
    public readonly Vector3 RightAxis;
    public readonly Vector3 UpAxis;
    public readonly Vector3 ViewAxis;

    public Vector2 Pan = Vector2.Zero;
    public float Zoom = 16f;
    public float TargetZoom = 16f;

    public Rectangle PixelRect;
    public int Width => PixelRect.Width;
    public int Height => PixelRect.Height;

    public Matrix ViewMatrix = Matrix.Identity;
    public Matrix ProjectionMatrix = Matrix.Identity;
    public Matrix WorldMatrix = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);

    public EditorViewport(ViewportType type, Vector3 right, Vector3 up, Vector3 view)
    {
        Type = type;
        RightAxis = right;
        UpAxis = up;
        ViewAxis = view;
    }

    public Ray ScreenToRay(Vector2 localPos, GraphicsDevice gd)
    {
        if (IsPerspective)
        {
            var saved = gd.Viewport;
            gd.Viewport = new Viewport(PixelRect);
            var near = gd.Viewport.Unproject(new Vector3(localPos, 0.1f), ProjectionMatrix, ViewMatrix, WorldMatrix);
            var far = gd.Viewport.Unproject(new Vector3(localPos, 1.0f), ProjectionMatrix, ViewMatrix, WorldMatrix);
            gd.Viewport = saved;
            return new Ray(near, Vector3.Normalize(far - near));
        }
        else
        {
            float wx = (localPos.X - Width * 0.5f) / Zoom + Pan.X;
            float wy = (Height * 0.5f - localPos.Y) / Zoom + Pan.Y;
            return new Ray(wx * RightAxis + wy * UpAxis + ViewAxis * 10000f, -ViewAxis);
        }
    }

    public Vector2 WorldToLocal(Vector3 world)
    {
        float sx = (Vector3.Dot(world, RightAxis) - Pan.X) * Zoom + Width * 0.5f;
        float sy = -(Vector3.Dot(world, UpAxis) - Pan.Y) * Zoom + Height * 0.5f;
        return new Vector2(sx, sy);
    }
    public Vector3 LocalToWorld(Vector2 local)
    {
        float wx = (local.X - Width * 0.5f) / Zoom + Pan.X;
        float wy = (Height * 0.5f - local.Y) / Zoom + Pan.Y;
        return wx * RightAxis + wy * UpAxis;
    }
    public Vector2 WorldToScreen(Vector3 world)
        => WorldToLocal(world) + new Vector2(PixelRect.X, PixelRect.Y);

    public bool IsOnScreen(Vector3 world, float margin = 32f)
    {
        var l = WorldToLocal(world);
        return l.X >= -margin && l.X <= Width + margin && l.Y >= -margin && l.Y <= Height + margin;
    }

    public Vector2? GlobalToLocal(Vector2 global)
    {
        var local = global - new Vector2(PixelRect.X, PixelRect.Y);
        if (local.X < 0 || local.Y < 0 || local.X > Width || local.Y > Height) return null;
        return local;
    }
    public Vector2? LocalToGlobal(Vector2 local)
    {
        if (local.X < 0 || local.Y < 0 || local.X > Width || local.Y > Height) return null;
        var global = local + new Vector2(PixelRect.X, PixelRect.Y);
        return local;
    }
}
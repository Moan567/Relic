using Avalonia;
using Microsoft.Xna.Framework;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.ViewportManagement;
// Handles dragging at the edges and keeping the mouse from escaping
public static class ViewportDragLock
{
    public static EditorViewport Locked { get; set; }

    const float EdgeMargin = 32f;
    const float PanSpeed = 800f;

    public static void Tick(EditorViewport hovered, Vector2 globalMouse, float delta)
    {
        if (Locked == null)
        {
            if (MouseManager.IsAnyDragging && hovered != null && hovered.Is2D)
            {
                Locked = hovered;
            }
            return;
        }

        if (!MouseManager.IsAnyDragging)
        {
            Locked = null;
            return;
        }

        UpdateEdgePan(Locked, globalMouse, delta);
    }

    static void UpdateEdgePan(EditorViewport vp, Vector2 mouseGlobal, float delta)
    {
        var rect = vp.PixelRect;

        float panX = EdgeFactor(mouseGlobal.X, rect.X, rect.X + rect.Width);
        float panY = EdgeFactor(mouseGlobal.Y, rect.Y, rect.Y + rect.Height);
        if (panX == 0f && panY == 0f) return;

        vp.Pan += new Vector2(panX, -panY) * PanSpeed * delta / vp.Zoom;
    }

    static float EdgeFactor(float global, float min, float max)
    {
        if (global < min + EdgeMargin)
            return -MathHelper.Clamp((min + EdgeMargin - global) / EdgeMargin, 0f, 1f);
        if (global > max - EdgeMargin)
            return MathHelper.Clamp((global - (max - EdgeMargin)) / EdgeMargin, 0f, 1f);
        return 0f;
    }
}
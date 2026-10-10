using Microsoft.Xna.Framework;
using Rockwall2.Editor.Mapper.ViewportManagement;
using System;

namespace Rockwall2.Editor.Mapper.Utils;

enum Drag2DMode { None, Drawing, MoveEdgeRight, MoveEdgeLeft, MoveEdgeUp, MoveEdgeDown, MoveAll }
public static class DragUtils
{
    public static bool IsInsideBox2D(EditorViewport vp, Vector2 mouseLocation, Vector3 boxMin, Vector3 boxMax)
    {
        float srMin = (Vector3.Dot(boxMin, vp.RightAxis) - vp.Pan.X) * vp.Zoom + vp.Width * 0.5f;
        float srMax = (Vector3.Dot(boxMax, vp.RightAxis) - vp.Pan.X) * vp.Zoom + vp.Width * 0.5f;
        float suMin = -(Vector3.Dot(boxMin, vp.UpAxis) - vp.Pan.Y) * vp.Zoom + vp.Height * 0.5f;
        float suMax = -(Vector3.Dot(boxMax, vp.UpAxis) - vp.Pan.Y) * vp.Zoom + vp.Height * 0.5f;
        return mouseLocation.X > Math.Min(srMin, srMax) && mouseLocation.X < Math.Max(srMin, srMax)
            && mouseLocation.Y > Math.Min(suMin, suMax) && mouseLocation.Y < Math.Max(suMin, suMax);
    }
}
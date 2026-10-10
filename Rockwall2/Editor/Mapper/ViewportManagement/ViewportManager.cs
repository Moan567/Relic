using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.ViewportManagement;

public static class ViewportManager
{
    public static ViewportLayout Layout { get; private set; }

    public static EditorViewport Perspective => Layout.Perspective;
    public static EditorViewport Top => Layout.Top;
    public static EditorViewport Front => Layout.Front;
    public static EditorViewport Side => Layout.Side;

    public static EditorViewport Active { get; set; }
    public static EditorViewport Rendering { get; set; }

    public static Vector2 MouseGlobal { get; set; }

    public static Vector2 MouseLocal =>
        Active?.GlobalToLocal(MouseGlobal) ?? Vector2.Zero;

    public static void Initialize()
    {
        Layout = new ViewportLayout();
        Active = Layout.Perspective;
    }

    public static void Recompute(int w, int h) => Layout.Recompute(w, h);

    public static void UpdateMatrices(GraphicsDevice gd)
    {
        Perspective.ViewMatrix = Viewport3DCamera.viewMatrix;
        Perspective.ProjectionMatrix = Viewport3DCamera.projectionMatrix;
        Build2D(Top, gd);
        Build2D(Front, gd);
        Build2D(Side, gd);
    }

    static void Build2D(EditorViewport vp, GraphicsDevice gd)
    {
        if (vp.Width <= 0 || vp.Height <= 0) return;

        Vector3 panWorld = vp.Pan.X * vp.RightAxis + vp.Pan.Y * vp.UpAxis;
        Vector3 camPos = panWorld + vp.ViewAxis * 10000f;

        Vector3 up = vp.UpAxis;
        if (Math.Abs(Vector3.Dot(Vector3.Normalize(vp.ViewAxis),
                                 Vector3.Normalize(up))) > 0.99f)
            up = vp.RightAxis;

        vp.ViewMatrix = Matrix.CreateLookAt(camPos, panWorld, up);

        float hw = vp.Width * 0.5f / vp.Zoom;
        float hh = vp.Height * 0.5f / vp.Zoom;
        vp.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
            -hw, hw, -hh, hh, -20000f, 20000f);
    }

    public static Ray ActiveRay(GraphicsDevice gd) =>
        Active.ScreenToRay(MouseLocal, gd);
}
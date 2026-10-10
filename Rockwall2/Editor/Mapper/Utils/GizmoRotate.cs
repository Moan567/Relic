using Avalonia.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper.ViewportManagement;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;

public static class GizmoRotate
{
    public static bool IsDragging { get; private set; }
    public static bool WasDragging { get; private set; }
    public static bool DraggingPivot => draggingPivot;
    public static bool WasDraggingPivot => wasDraggingPivot;

    static Vector3 gizmoPos;
    static Vector3 dragPivot;
    static Vector3? customPivot;
    static bool draggingPivot;
    static bool wasDraggingPivot;

    static Vector3 selectedAxis;
    static bool xSelected, ySelected, zSelected;

    static float floatingAngle;
    static float originAngle;
    static float previousAngle;

    const float RingRadius = 0.7f;
    const float RingThickness = 0.05f;
    const float PickTolerance = 0.06f;
    const float CornerPickPixels = 10f;
    const float PivotPickPixels = 10f;

    static float time = 0;

    static Vector3 PivotWorld => customPivot ?? gizmoPos;

    public static List<VertexPositionColor> VerticesX = new();
    public static List<int> IndicesX = new();
    public static List<VertexPositionColor> VerticesY = new();
    public static List<int> IndicesY = new();
    public static List<VertexPositionColor> VerticesZ = new();
    public static List<int> IndicesZ = new();

    public static void GenerateGeometry()
    {
        VerticesX.Clear(); IndicesX.Clear();
        VerticesY.Clear(); IndicesY.Clear();
        VerticesZ.Clear(); IndicesZ.Clear();

        BrushOperations.AddRing(VerticesX, IndicesX, Vector3.UnitX, Color.IndianRed, RingRadius, RingThickness, 32);
        BrushOperations.AddRing(VerticesY, IndicesY, Vector3.UnitY, Color.ForestGreen, RingRadius, RingThickness, 32);
        BrushOperations.AddRing(VerticesZ, IndicesZ, Vector3.UnitZ, Color.CornflowerBlue, RingRadius, RingThickness, 32);
    }

    public static void ResetPivot() => customPivot = null;
    public static void SetPivot(Vector3 p) => customPivot = p;

    public static void UpdateGeneral()
    {
        WasDragging = IsDragging;
        wasDraggingPivot = draggingPivot;
        if (Toolbelt.SelectedObjects.Count == 0) { customPivot = null; return; }

        gizmoPos = Toolbelt.SelectedObjects.Select(o => o.GetPosition()).Aggregate((s, e) => s + e) / Toolbelt.SelectedObjects.Count;
        gizmoPos = Vector3.Round(gizmoPos / Transformable.GridSize) * Transformable.GridSize;
    }

    public static void UpdateUse(bool allowAnchor)
    {
        if (ViewportManager.Active.Is2D) { Update2D(allowAnchor); return; }

        if (IsDragging) { UpdateDrag(); return; }

        var sceneRay = ViewportManager.ActiveRay(EditorHost.Instance.GraphicsDevice);
        selectedAxis = Vector3.Zero;
        float bestT = float.MaxValue;

        void TestAxis(Vector3 axis)
        {
            var plane = new Plane(axis, -Vector3.Dot(axis, PivotWorld));
            float? t = sceneRay.Intersects(plane);
            if (!t.HasValue) return;
            Vector3 hit = sceneRay.Position + sceneRay.Direction * t.Value;
            float score = MathF.Abs(Vector3.Distance(hit, PivotWorld) - RingRadius);
            if (score < PickTolerance && t.Value < bestT)
            {
                bestT = t.Value;
                selectedAxis = axis;
            }
        }
        TestAxis(Vector3.UnitX);
        TestAxis(Vector3.UnitY);
        TestAxis(Vector3.UnitZ);

        xSelected = selectedAxis == Vector3.UnitX;
        ySelected = selectedAxis == Vector3.UnitY;
        zSelected = selectedAxis == Vector3.UnitZ;

        if (MouseManager.IsDown(MouseButton.Left) && selectedAxis != Vector3.Zero)
            BeginDrag();
    }

    static void Update2D(bool allowAnchor = true)
    {
        var vp = ViewportManager.Active;

        time += 1 / 60f;

        if (draggingPivot && allowAnchor)
        {
            if (!MouseManager.IsDown(MouseButton.Left)) { draggingPivot = false; return; }
            Vector2 mouseLocal = vp.GlobalToLocal(ViewportManager.MouseGlobal) ?? Vector2.Zero;

            Vector3 planar = vp.LocalToWorld(mouseLocal);
            Vector3 snapped = Vector3.Floor(planar / Transformable.GridSize + Vector3.One * 0.5f) * Transformable.GridSize;

            float depth = Vector3.Dot(customPivot ?? gizmoPos, vp.ViewAxis);
            customPivot = snapped + vp.ViewAxis * depth;
            return;
        }

        if (IsDragging) { UpdateDrag(); return; }

        Vector2 mouse = vp.GlobalToLocal(ViewportManager.MouseGlobal) ?? Vector2.Zero;

        if (Vector2.Distance(mouse, vp.WorldToLocal(PivotWorld)) < PivotPickPixels && allowAnchor)
        {
            if (MouseManager.IsDown(MouseButton.Left)) draggingPivot = true;
            return;
        }

        selectedAxis = Vector3.Zero;
        foreach (var c in Get2DHandleCorners(vp))
        {
            if (Vector2.Distance(mouse, c) < CornerPickPixels)
            {
                selectedAxis = vp.ViewAxis;
                break;
            }
        }

        if (MouseManager.IsDown(MouseButton.Left) && selectedAxis != Vector3.Zero)
            BeginDrag();
    }

    static Vector2[] Get2DHandleCorners(EditorViewport vp)
    {
        var bounds = Toolbelt.GetSelectionBounds();
        Vector2 a = vp.WorldToLocal(bounds.Min);
        Vector2 b = vp.WorldToLocal(bounds.Max);
        Vector2 min = Vector2.Min(a, b);
        Vector2 max = Vector2.Max(a, b);

        const float pad = 12f;
        min -= Vector2.One * pad;
        max += Vector2.One * pad;
        return new[] { min, new Vector2(max.X, min.Y), new Vector2(min.X, max.Y), max };
    }

    static float MeasureAngle()
    {
        if (ViewportManager.Active.Is2D)
        {
            var vp = ViewportManager.Active;
            Vector2 mouseLocal = vp.GlobalToLocal(ViewportManager.MouseGlobal) ?? Vector2.Zero;
            Vector2 centerLocal = vp.WorldToLocal(dragPivot);
            return MathF.Atan2(-(mouseLocal.Y - centerLocal.Y), mouseLocal.X - centerLocal.X);
        }

        var sceneRay = ViewportManager.ActiveRay(EditorHost.Instance.GraphicsDevice);
        var plane = new Plane(selectedAxis, -Vector3.Dot(selectedAxis, dragPivot));
        float? t = sceneRay.Intersects(plane);
        if (!t.HasValue) return previousAngle;

        Vector3 local = (sceneRay.Position + sceneRay.Direction * t.Value) - dragPivot;
        Vector3 u = Vector3.Cross(selectedAxis, Vector3.Up);
        if (u.LengthSquared() < 0.01f) u = Vector3.Cross(selectedAxis, Vector3.Right);
        u.Normalize();
        Vector3 v = Vector3.Cross(selectedAxis, u);
        return MathF.Atan2(Vector3.Dot(local, v), Vector3.Dot(local, u));
    }

    static void UpdateDrag()
    {
        if (!MouseManager.IsDown(MouseButton.Left)) { EndDrag(); return; }

        float currentAngle = MeasureAngle();
        floatingAngle += NormalizeAngle(currentAngle - previousAngle);
        previousAngle = currentAngle;
        ApplySnappedRotation();
    }

    static float NormalizeAngle(float a)
    {
        while (a > MathHelper.Pi) a -= MathHelper.TwoPi;
        while (a < -MathHelper.Pi) a += MathHelper.TwoPi;
        return a;
    }

    static void BeginDrag()
    {
        IsDragging = true;
        dragPivot = PivotWorld;
        floatingAngle = 0;
        originAngle = 0;
        previousAngle = MeasureAngle();
    }

    static void ApplySnappedRotation()
    {
        float snapRadians = MathHelper.ToRadians(Transformable.RotationSnapDegrees);
        float snapped = MathF.Round(floatingAngle / snapRadians) * snapRadians;
        if (MathF.Abs(snapped - originAngle) < 0.0001f) return;

        float delta = snapped - originAngle;
        foreach (var obj in Toolbelt.SelectedObjects)
            obj.RotateAbout(dragPivot, selectedAxis, delta);
        originAngle = snapped;
    }

    static void EndDrag()
    {
        IsDragging = false;

        foreach (var obj in Toolbelt.SelectedObjects)
            obj.PostMove();

        Toolbelt.SelectedObjects.RemoveAll(t => t is BrushVertexMoveable);
        Toolbelt.CreateUndoStateForAllSelectedObjects();
    }

    internal static void Draw(GraphicsDevice graphicsDevice, BasicEffect basicEffect, bool anchorDraw = true)
    {
        if (Toolbelt.SelectedObjects.Count == 0) return;
        if (ViewportManager.Rendering.Is2D) { Draw2DHandles(anchorDraw); return; }

        Vector3 renderPos = IsDragging ? dragPivot : PivotWorld;

        basicEffect.VertexColorEnabled = true;
        basicEffect.World = Matrix.CreateWorld(renderPos, Vector3.Forward, Vector3.Up);
        basicEffect.Alpha = 1;
        var selectedColor = Vector3.One * 0.25f;

        DrawRing(graphicsDevice, basicEffect, VerticesX, IndicesX, xSelected ? selectedColor : Vector3.One);
        DrawRing(graphicsDevice, basicEffect, VerticesY, IndicesY, ySelected ? selectedColor : Vector3.One);
        DrawRing(graphicsDevice, basicEffect, VerticesZ, IndicesZ, zSelected ? selectedColor : Vector3.One);

        basicEffect.VertexColorEnabled = false;
    }

    static void DrawRing(GraphicsDevice gd, BasicEffect fx, List<VertexPositionColor> verts, List<int> inds, Vector3 color)
    {
        fx.DiffuseColor = color;
        foreach (var pass in fx.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, verts.ToArray(), 0, verts.Count, inds.ToArray(), 0, inds.Count / 3);
        }
    }

    static void Draw2DHandles(bool anchor)
    {
        var vp = ViewportManager.Rendering;
        var corners = Get2DHandleCorners(vp);
        var sb = MapperView.Instance.SpriteBatch;
        var ringColor = IsDragging ? Color.MonoGameOrange : Color.White;

        sb.Begin();
        foreach (var c in corners)
            sb.DrawCircle(new CircleF(new(c.X, c.Y), 5), 8, ringColor);

        if(anchor)
        {
            var pivotScreen = vp.WorldToLocal(PivotWorld);
            var pivotColor = draggingPivot ? Color.MonoGameOrange : Color.Yellow;
            RenderUtils.DrawDashedCircle(sb, pivotScreen, 6f, Color.Yellow, 1f, time);
            sb.DrawLine(pivotScreen - Vector2.UnitX * 9, pivotScreen + Vector2.UnitX * 9, pivotColor, 1f);
            sb.DrawLine(pivotScreen - Vector2.UnitY * 9, pivotScreen + Vector2.UnitY * 9, pivotColor, 1f);
        }

        sb.End();
    }
}
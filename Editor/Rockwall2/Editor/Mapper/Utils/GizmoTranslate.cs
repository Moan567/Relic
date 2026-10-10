using Avalonia.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper.ViewportManagement;
using Rockwall2.Views;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2.Editor.Mapper.Utils;

public static class GizmoTranslate
{
    public enum GizmoMode { Translate, Rotate }

    public static GizmoMode Mode = GizmoMode.Translate;
    public static Matrix WorldMatrix = Matrix.Identity;

    public static List<VertexPositionColor> VerticesX = new();
    public static List<int> IndicesX = new();
    public static List<VertexPositionColor> VerticesY = new();
    public static List<int> IndicesY = new();
    public static List<VertexPositionColor> VerticesZ = new();
    public static List<int> IndicesZ = new();

    static BoundingBox xBox;
    static BoundingBox yBox;
    static BoundingBox zBox;

    static bool xSelected;
    static bool ySelected;
    static bool zSelected;
    public static bool IsDragging { get; private set; }
    public static bool WasDragging { get; private set; }

    static Vector3 selectedAxis;
    static Vector3 gizmoPos;
    static Vector3 floatingGizmoPos;
    static Vector3 originGizmoPos;
    static Vector2 projectedAxis;
    static float pixelsPerWorldUnit;

    static PlaneGrid grid;

    static DepthStencilState depthStencilState;

    public static void GenerateGeometry()
    {
        VerticesX.Clear(); IndicesX.Clear();
        VerticesY.Clear(); IndicesY.Clear();
        VerticesZ.Clear(); IndicesZ.Clear();

        depthStencilState = new DepthStencilState
        {
            DepthBufferWriteEnable = true,
            DepthBufferFunction = CompareFunction.Always,
            DepthBufferEnable = true
        };

        if (Mode == GizmoMode.Translate)
        {
            float length = 0.6f;
            float coneLength = 0.1f;
            float coneRadius = 0.07f;
            float shaftRadius = 0.04f;

            xBox = new BoundingBox(Vector3.One * -shaftRadius * 1.5f, Vector3.One * shaftRadius * 1.5f + Vector3.UnitX * length);
            yBox = new BoundingBox(Vector3.One * -shaftRadius * 1.5f, Vector3.One * shaftRadius * 1.5f + Vector3.UnitY * length);
            zBox = new BoundingBox(Vector3.One * -shaftRadius * 1.5f, Vector3.One * shaftRadius * 1.5f + Vector3.UnitZ * length);

            BrushOperations.AddAxis(VerticesX, IndicesX, Vector3.Right, Color.IndianRed, length, coneLength, shaftRadius, coneRadius);
            BrushOperations.AddAxis(VerticesY, IndicesY, Vector3.Up, Color.ForestGreen, length, coneLength, shaftRadius, coneRadius);
            BrushOperations.AddAxis(VerticesZ, IndicesZ, Vector3.Backward, Color.CornflowerBlue, length, coneLength, shaftRadius, coneRadius);
        }
    }

    public static void Update()
    {
        WasDragging = IsDragging;

        if (Toolbelt.SelectedObjects.Count == 0) return;

        var sceneRay = ViewportManager.ActiveRay(EditorHost.Instance.GraphicsDevice);

        gizmoPos = Toolbelt.SelectedObjects.Select(o => o.GetPosition()).Aggregate((s, e) => s + e) / (Toolbelt.SelectedObjects.Count);

        sceneRay.Position -= gizmoPos;

        if (!IsDragging)
        {
            float minDist = float.MaxValue;
            float? xDist = sceneRay.Intersects(xBox);
            float? yDist = sceneRay.Intersects(yBox);
            float? zDist = sceneRay.Intersects(zBox);

            xSelected = false;
            ySelected = false;
            zSelected = false;

            if (float.Abs(Vector3.Dot(ViewportManager.Active.ViewAxis, Vector3.UnitX)) > 0.8f && ViewportManager.Active.Is2D)
                xDist = float.MaxValue;
            if (float.Abs(Vector3.Dot(ViewportManager.Active.ViewAxis, Vector3.UnitY)) > 0.8f && ViewportManager.Active.Is2D)
                yDist = float.MaxValue;
            if (float.Abs(Vector3.Dot(ViewportManager.Active.ViewAxis, Vector3.UnitZ)) > 0.8f && ViewportManager.Active.Is2D)
                zDist = float.MaxValue;

            selectedAxis = Vector3.Zero;

            if (xDist > 0.1f && xDist < minDist)
            {
                minDist = xDist.Value;
                xSelected = true;
                ySelected = false;
                zSelected = false;

                selectedAxis = Vector3.UnitX;
            }
            if (yDist > 0.1f && yDist < minDist)
            {
                minDist = yDist.Value;
                xSelected = false;
                ySelected = true;
                zSelected = false;

                selectedAxis = Vector3.UnitY;
            }
            if (zDist > 0.1f && zDist < minDist)
            {
                minDist = zDist.Value;
                xSelected = false;
                ySelected = false;
                zSelected = true;

                selectedAxis = Vector3.UnitZ;
            }

            if (MouseManager.IsDown(Avalonia.Input.MouseButton.Left) && (xSelected || ySelected || zSelected))
            {
                if (KeyboardManager.IsDown(Key.LeftShift))
                {
                    Toolbelt.DuplicateSelectedObjects();
                    if (Toolbelt.SelectedObjects.Count > 0)
                        gizmoPos = Toolbelt.SelectedObjects.Select(o => o.GetPosition()).Aggregate((s, e) => s + e) / Toolbelt.SelectedObjects.Count;
                }

                IsDragging = true;

                (projectedAxis, pixelsPerWorldUnit) = OtherMath.ProjectAxisToScreen(gizmoPos, selectedAxis);

                floatingGizmoPos = gizmoPos;
                originGizmoPos = Vector3.Floor(floatingGizmoPos / Transformable.GridSize) * Transformable.GridSize;

                MouseManager.CaptureCursor(MainWindow.Instance, new Vector2((float)MouseManager.Position.X, (float)MouseManager.Position.Y));

                Vector3 normal = new Vector3();
                if (xSelected) normal = Vector3.Up;
                if (ySelected) normal = Vector3.Forward;
                if (zSelected) normal = Vector3.Right;

                grid = new PlaneGrid(EditorHost.Instance.GraphicsDevice, Transformable.GridSize, 50, new Plane(originGizmoPos, normal));
            }
        }
        else
        {
            if (!MouseManager.IsDown(Avalonia.Input.MouseButton.Left))
            {
                IsDragging = false;
                MouseManager.ResetCursor(MainWindow.Instance);
                MouseManager.ReleaseCursor(MainWindow.Instance);

                foreach (var obj in Toolbelt.SelectedObjects)
                {
                    obj.PostMove();
                }

                // This is kind of a hack because we can no longer be sure that the vert IDs will be the same.
                Toolbelt.SelectedObjects.RemoveAll(t => t is BrushVertexMoveable);

                Toolbelt.CreateUndoStateForAllSelectedObjects();
                return;
            }

            Vector2 delta = new Vector2((float)MouseManager.Delta.X, (float)MouseManager.Delta.Y);

            float move = Vector2.Dot(projectedAxis, delta) / pixelsPerWorldUnit;

            floatingGizmoPos += selectedAxis * move;
            var snapped = Vector3.Floor(floatingGizmoPos / Transformable.GridSize) * Transformable.GridSize;
            if (snapped != originGizmoPos)
            {
                foreach (var obj in Toolbelt.SelectedObjects)
                {
                    obj.Move(snapped - originGizmoPos);
                }
                originGizmoPos = snapped;
            }
        }
    }

    internal static void Draw(GraphicsDevice graphicsDevice, BasicEffect basicEffect)
    {
        if (Toolbelt.SelectedObjects.Count == 0) return;

        if (IsDragging)
        {
            grid.Draw(ViewportManager.Rendering.ViewMatrix, ViewportManager.Rendering.ProjectionMatrix, gizmoPos);
        }

        basicEffect.VertexColorEnabled = true;

        graphicsDevice.DepthStencilState = depthStencilState;

        basicEffect.World = Matrix.CreateWorld(gizmoPos, Vector3.Forward, Vector3.Up);
        basicEffect.Alpha = 1;

        if (Mode == GizmoMode.Translate)
        {
            var selectedColor = Vector3.One * 0.25f;


            if (float.Abs(Vector3.Dot(ViewportManager.Rendering.ViewAxis, Vector3.UnitX)) < 0.8f || !ViewportManager.Rendering.Is2D)
            {
                basicEffect.DiffuseColor = xSelected ? selectedColor : Vector3.One;

                foreach (var pass in basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    graphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, VerticesX.ToArray(), 0, VerticesX.Count, IndicesX.ToArray(), 0, IndicesX.Count / 3);
                }
            }
            if (float.Abs(Vector3.Dot(ViewportManager.Rendering.ViewAxis, Vector3.UnitY)) < 0.8f || !ViewportManager.Rendering.Is2D)
            {
                basicEffect.DiffuseColor = ySelected ? selectedColor : Vector3.One;

                foreach (var pass in basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    graphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, VerticesY.ToArray(), 0, VerticesY.Count, IndicesY.ToArray(), 0, IndicesY.Count / 3);
                }
            }
            if (float.Abs(Vector3.Dot(ViewportManager.Rendering.ViewAxis, Vector3.UnitZ)) < 0.8f || !ViewportManager.Rendering.Is2D)
            {
                basicEffect.DiffuseColor = zSelected ? selectedColor : Vector3.One;

                foreach (var pass in basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    graphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, VerticesZ.ToArray(), 0, VerticesZ.Count, IndicesZ.ToArray(), 0, IndicesZ.Count / 3);
                }
            }
        }

        basicEffect.VertexColorEnabled = false;
        graphicsDevice.DepthStencilState = DepthStencilState.Default;
    }
}
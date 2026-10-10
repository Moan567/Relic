using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using Rockwall2.Editor.Mapper.Tools;
using Rockwall2.Editor.Mapper.ViewportManagement;
using Rockwall2.Editor.Mapper.Utils;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Common.Input;
using Avalonia.Input;
using Rockwall2.Views;

namespace Rockwall2.Tools
{
    public class BrushTool : Tool
    {
        static readonly Color BoxFill = new Color(255, 140, 30, 28);
        static readonly Color BoxBorder = new Color(255, 160, 60, 220);
        static readonly Color BoxBorderMove = new Color(255, 220, 100, 255);
        static readonly Color BoxEdgeHit = new Color(255, 200, 80, 180);
        static readonly Color BoxSizeLabel = new Color(255, 220, 160, 200);

        Vector3 boxMin = Vector3.Zero;
        Vector3 boxMax = Vector3.Zero;
        bool hasBox = false;

        // 2D
        Drag2DMode drag2D = Drag2DMode.None;
        EditorViewport drag2DViewport;
        Vector3 dragStartWorld;
        Vector3 boxMinAtDragStart;
        Vector3 boxMaxAtDragStart;
        BoundingBox boxMemory;

        float scaleGizmoTime;
        Vector3 boxMinAtScaleStart;
        Vector3 boxMaxAtScaleStart;

        // 3D
        PlaneGrid grid;
        Plane currentPlane;
        bool buildingBrush3D;
        bool extrudeOutward;
        Vector3 extrudeAxis = Vector3.Up;
        float extrusionDistance;
        float pixelsPerWorldUnit;
        Vector3 flatStart, flatEnd;
        Vector2 projectionAngle;
        Vector3 worldPos;

        public enum PrimitiveShape { Box, Cylinder, Sphere, Cone }
        PrimitiveShape activeShape = PrimitiveShape.Box;
        int primitiveSides = 12;

        VertexPosition[] nubs = BrushOperations
            .GenerateSphereVerticesDirect(Vector3.Zero, 0.1f, 4, 8)
            .Reverse().ToArray();
        List<VertexPosition> brushTempVerts = new();
        Vector3 PrimitiveAxis = Vector3.Up;

        Brush? previewBrush;
        bool previewDirty = true;

        void MarkPreviewDirty() => previewDirty = true;

        public void SetPrimitiveMode(PrimitiveShape shape)
        {
            activeShape = shape;
            previewDirty = true;
        }
        public void SetPrimitiveParams(int sides)
        {
            primitiveSides = sides;
            previewDirty = true;
        }

        void RebuildPreviewIfNeeded()
        {
            if (!hasBox)
            {
                previewBrush = null;
                return;
            }
            if (!previewDirty) return;
            previewDirty = false;

            var bounds = new BoundingBox(boxMin, boxMax);
            List<Plane> planes = activeShape switch
            {
                PrimitiveShape.Cylinder => PrimitiveGenerator.Cylinder(bounds, primitiveSides, PrimitiveAxis),
                PrimitiveShape.Sphere => PrimitiveGenerator.Sphere(bounds, primitiveSides / 2, primitiveSides, PrimitiveAxis),
                PrimitiveShape.Cone => PrimitiveGenerator.Cone(bounds, primitiveSides, PrimitiveAxis),
                _ => PrimitiveGenerator.Box(bounds),
            };
            previewBrush = BrushOperations.CreateBrushFromPlanes(planes, Toolbelt.ActiveTexture,
                GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture]);
        }

        public override void OnSelected()
        {
            grid ??= new PlaneGrid(EditorHost.Instance.GraphicsDevice,
                Transformable.GridSize, 2, new Plane(Vector3.Up, 0));
        }

        public override void OnDeselected()
        {
            hasBox = false;
            drag2D = Drag2DMode.None;
            buildingBrush3D = false;
            extrudeOutward = false;
            previewBrush = null;
        }

        public override void OnUpdate(float delta)
        {
            scaleGizmoTime += delta;
            if (IsUsedIn2D) Update2D();
            else Update3D();
        }

        void Update2D()
        {
            var vp = ActiveViewport;
            var mouseLocal = MouseLocalF;
            var mouseWorld = MouseLocalToWorld(vp, mouseLocal);

            bool leftDown = MouseManager.WasDown(Avalonia.Input.MouseButton.Left);
            bool leftPressed = MouseManager.IsPressed(Avalonia.Input.MouseButton.Left);
            bool leftReleased = MouseManager.IsReleased(Avalonia.Input.MouseButton.Left);

            if (KeyboardManager.IsPressed(Key.Escape))
            {
                if (drag2D != Drag2DMode.None) drag2D = Drag2DMode.None;
                else { hasBox = false; buildingBrush3D = false; }
                return;
            }

            if (GizmoScale.isDragging)
            {
                MouseManager.SetCursor(MainWindow.Instance, GizmoScale.GetActiveCursor()!.Value);
                GizmoScale.Update(vp, mouseWorld);
                return;
            }

            if (hasBox && drag2D == Drag2DMode.None)
                if (KeyboardManager.IsPressed(Key.Enter) || MouseManager.IsPressed(MouseButton.Right))
                { CommitBox(); return; }

            if (leftPressed && drag2D == Drag2DMode.None)
            {
                if (hasBox)
                {
                    int handle = HitTestScaleHandle(vp, mouseLocal);
                    if (handle != -1)
                        GizmoScale.BeginDrag(handle, vp, BoxBounds, ApplyBoxScale, BeginBoxScale, EndBoxScale);
                    else if (IsInsideBox2D(vp, mouseLocal))
                        BeginDrag(Drag2DMode.MoveAll, vp, mouseWorld);
                    else
                    {
                        hasBox = false; buildingBrush3D = false;
                        BeginDraw(vp, mouseWorld);
                    }
                }
                else
                    BeginDraw(vp, mouseWorld);
            }

            if (leftDown && drag2D != Drag2DMode.None)
                ApplyDrag(drag2DViewport, MouseLocalToWorld(drag2DViewport, MouseLocalF));

            if (leftReleased && drag2D != Drag2DMode.None)
            {
                NormaliseBox();
                if (BoxHasArea(drag2DViewport))
                {
                    EnsureMinDepth(drag2DViewport);
                    hasBox = true;
                }
                else
                    hasBox = false;
                drag2D = Drag2DMode.None;
            }
        }

        void BeginDraw(EditorViewport vp, Vector3 worldPt)
        {
            extrudeAxis = vp.ViewAxis;
            BeginDrag(Drag2DMode.Drawing, vp, worldPt);
            InitBoxFromPoint(vp, worldPt);
        }

        void BeginDrag(Drag2DMode mode, EditorViewport vp, Vector3 worldPt)
        {
            drag2D = mode;
            drag2DViewport = vp;
            dragStartWorld = worldPt;
            boxMinAtDragStart = boxMin;
            boxMaxAtDragStart = boxMax;
        }

        void Update3D()
        {
            grid.SetSpacing(Transformable.GridSize);
            var sceneRay = SceneRay;

            if (hasBox)
            {
                if (KeyboardManager.IsPressed(Key.Escape))
                {
                    hasBox = false; buildingBrush3D = false; extrudeOutward = false;
                    UpdateHoverPlane(sceneRay);
                    return;
                }
                if (!buildingBrush3D &&
                    (KeyboardManager.IsPressed(Key.Enter) || MouseManager.IsPressed(MouseButton.Right)))
                {
                    CommitBox(); return;
                }
            }

            if (!hasBox && !buildingBrush3D)
            {
                UpdateHoverPlane(sceneRay);

                if (MouseManager.IsDown(MouseButton.Left))
                {
                    buildingBrush3D = true;
                    flatStart = SnapPointToPlane(worldPos, currentPlane);
                    flatEnd = flatStart;
                    extrusionDistance = 0;
                    extrudeAxis = currentPlane.Normal;
                    PrimitiveAxis = SnapToCardinalAxis(extrudeAxis);
                }
                return;
            }

            if (buildingBrush3D && MouseManager.IsDown(MouseButton.Left))
            {
                (projectionAngle, pixelsPerWorldUnit) =
                    OtherMath.ProjectAxisToScreen(flatStart, currentPlane.Normal);

                float? dist = sceneRay.Intersects(currentPlane);
                if (dist > 0.1f)
                {
                    worldPos = Vector3.Round((sceneRay.Position + sceneRay.Direction * dist.Value)
                                   / Transformable.GridSize) * Transformable.GridSize;
                    flatEnd = SnapPointToPlane(worldPos, currentPlane, flatStart);
                }

                Sync3DToBox();
                hasBox = true;
                MouseManager.ResetCursor(MainWindow.Instance);
                return;
            }

            if (hasBox)
            {
                int scrollDir = float.Sign(MouseManager.ScrollDelta);
                if (scrollDir != 0)
                    ScrollExpandBox(scrollDir);

                buildingBrush3D = false;
                UpdateHoverPlane(sceneRay);

                if (KeyboardManager.IsPressed(Key.Enter) || MouseManager.IsPressed(MouseButton.Right))
                    CommitBox();
            }
        }
        void ScrollExpandBox(int dir)
        {
            float gs = Transformable.GridSize;

            Vector3 n = extrudeAxis;
            Vector3 absN = new Vector3(MathF.Abs(n.X), MathF.Abs(n.Y), MathF.Abs(n.Z));

            if (absN.X >= absN.Y && absN.X >= absN.Z)
            {
                if (n.X >= 0) boxMax.X = MathF.Round((boxMax.X + gs * dir) / gs) * gs;
                else boxMin.X = MathF.Round((boxMin.X - gs * dir) / gs) * gs;
            }
            else if (absN.Y >= absN.X && absN.Y >= absN.Z)
            {
                if (n.Y >= 0) boxMax.Y = MathF.Round((boxMax.Y + gs * dir) / gs) * gs;
                else boxMin.Y = MathF.Round((boxMin.Y - gs * dir) / gs) * gs;
            }
            else
            {
                if (n.Z >= 0) boxMax.Z = MathF.Round((boxMax.Z + gs * dir) / gs) * gs;
                else boxMin.Z = MathF.Round((boxMin.Z - gs * dir) / gs) * gs;
            }
            MarkPreviewDirty();
        }
        void Sync3DToBox()
        {
            float extrude = MathF.Floor(extrusionDistance / Transformable.GridSize) * Transformable.GridSize;
            var min = flatStart;
            var max = flatEnd + extrude * currentPlane.Normal;
            boxMin = new Vector3(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y), Math.Min(min.Z, max.Z));
            boxMax = new Vector3(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y), Math.Max(min.Z, max.Z));
            MarkPreviewDirty();
        }

        void UpdateHoverPlane(Ray sceneRay)
        {
            var mapHit = MapTools.RaycastMapGeometry(sceneRay, true, !IsUsedIn2D);
            if (mapHit.distance > 0f && mapHit.face != -1 && mapHit.brush != -1)
            {
                worldPos = Vector3.Round((sceneRay.Position + sceneRay.Direction * mapHit.distance)
                               / Transformable.GridSize) * Transformable.GridSize;
                var fn = MapTools.Brushes[mapHit.brush].Faces[mapHit.face].Normal;
                var an = new Vector3(MathF.Abs(fn.X), MathF.Abs(fn.Y), MathF.Abs(fn.Z));
                Vector3 sn = an.X >= an.Y && an.X >= an.Z ? new Vector3(MathF.Sign(fn.X), 0, 0)
                           : an.Y >= an.X && an.Y >= an.Z ? new Vector3(0, MathF.Sign(fn.Y), 0)
                                                           : new Vector3(0, 0, MathF.Sign(fn.Z));
                currentPlane = new Plane(worldPos, sn);
                grid.SetPlane(currentPlane);
            }
            else
            {
                currentPlane = new Plane(Vector3.Zero, Vector3.Up);
                grid.SetPlane(currentPlane);
                float? dst = sceneRay.Intersects(currentPlane);
                if (dst > 0)
                    worldPos = Vector3.Round((sceneRay.Position + sceneRay.Direction * dst.Value)
                                   / Transformable.GridSize) * Transformable.GridSize;
            }
        }

        void CommitBox()
        {
            NormaliseBox();
            float gs = Transformable.GridSize;
            if (boxMax.X - boxMin.X < gs * 0.5f ||
                boxMax.Y - boxMin.Y < gs * 0.5f ||
                boxMax.Z - boxMin.Z < gs * 0.5f) return;

            Brush created;

            var brushOrigin = boxMin;

            var bounds = new BoundingBox(boxMin - brushOrigin, boxMax - brushOrigin);

            switch (activeShape)
            {
                case PrimitiveShape.Cylinder:
                    created = BrushOperations.CreateBrushFromPlanes(
                        PrimitiveGenerator.Cylinder(bounds, primitiveSides, PrimitiveAxis),
                        Toolbelt.ActiveTexture, GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture]).Value;
                    break;
                case PrimitiveShape.Sphere:
                    created = BrushOperations.CreateBrushFromPlanes(
                        PrimitiveGenerator.Sphere(bounds, primitiveSides / 2, primitiveSides, PrimitiveAxis),
                        Toolbelt.ActiveTexture, GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture]).Value;
                    break;
                case PrimitiveShape.Cone:
                    created = BrushOperations.CreateBrushFromPlanes(
                        PrimitiveGenerator.Cone(bounds, primitiveSides, PrimitiveAxis),
                        Toolbelt.ActiveTexture, GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture]).Value;
                    break;
                default:
                    created = BrushOperations.CreateBrush(bounds.Min, bounds.Max);
                    break;
            }

            created.Position = brushOrigin;

            var oldBrushes = MapTools.Brushes;

            BrushOperations.RebuildBrush(ref created);
            MapTools.AddBrush(created);

            var brushRef = MapTools.Brushes[MapTools.Brushes.Length - 1];
            Toolbelt.UndoManager.DoOnUndo(() =>
            {
                MapTools.ActiveMap.Brushes = oldBrushes;
                MapTools.RecomputeAllBrushBounds();
            });

            hasBox = false;
            drag2D = Drag2DMode.None;
            buildingBrush3D = false;
            extrudeOutward = false;

            boxMemory = new BoundingBox(boxMin, boxMax);
        }

        public override void OnRender(float delta)
        {
            RebuildPreviewIfNeeded();
            if (IsRenderingIn2D) { Render2D(RenderingViewport); return; }
            Render3D();
        }
        BoundingBox BoxBounds() => new BoundingBox(boxMin, boxMax);

        void BeginBoxScale()
        {
            boxMinAtScaleStart = boxMin;
            boxMaxAtScaleStart = boxMax;
        }

        void ApplyBoxScale(Vector3 anchor, EditorViewport vp, float scaleRight, float scaleUp)
        {
            Vector3 newMin = GizmoScale.TransformPoint(anchor, boxMinAtScaleStart, vp, scaleRight, scaleUp);
            Vector3 newMax = GizmoScale.TransformPoint(anchor, boxMaxAtScaleStart, vp, scaleRight, scaleUp);
            boxMin = new Vector3(Math.Min(newMin.X, newMax.X), Math.Min(newMin.Y, newMax.Y), Math.Min(newMin.Z, newMax.Z));
            boxMax = new Vector3(Math.Max(newMin.X, newMax.X), Math.Max(newMin.Y, newMax.Y), Math.Max(newMin.Z, newMax.Z));
            MarkPreviewDirty();
        }

        void EndBoxScale() { }

        int HitTestScaleHandle(EditorViewport vp, Vector2 ml)
        {
            if (!hasBox) return -1;

            var handles = GizmoScale.GetHandles(vp, BoxBounds(), out _);
            float best = 10f;
            int bestIndex = -1;

            for (int i = 0; i < handles.Length; i++)
            {
                float dist = Vector2.Distance(vp.WorldToLocal(handles[i]), ml);
                if (dist < best)
                {
                    best = dist;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
        void Render2D(EditorViewport vp)
        {
            if (!hasBox && drag2D == Drag2DMode.None) return;

            float rMin = Vector3.Dot(boxMin, vp.RightAxis);
            float rMax = Vector3.Dot(boxMax, vp.RightAxis);
            float uMin = Vector3.Dot(boxMin, vp.UpAxis);
            float uMax = Vector3.Dot(boxMax, vp.UpAxis);

            float sxMin = (rMin - vp.Pan.X) * vp.Zoom + vp.Width * 0.5f;
            float sxMax = (rMax - vp.Pan.X) * vp.Zoom + vp.Width * 0.5f;
            float syMin = -(uMin - vp.Pan.Y) * vp.Zoom + vp.Height * 0.5f;
            float syMax = -(uMax - vp.Pan.Y) * vp.Zoom + vp.Height * 0.5f;

            float left = Math.Min(sxMin, sxMax);
            float right = Math.Max(sxMin, sxMax);
            float top = Math.Min(syMin, syMax);
            float bottom = Math.Max(syMin, syMax);
            float w = right - left;
            float h = bottom - top;

            var mouseLocal = vp.GlobalToLocal(ViewportManager.MouseGlobal) ?? new Vector2(-9999, -9999);

            GizmoScale.hoveredHandle = -1;
            if (hasBox && !GizmoScale.isDragging)
            {
                var scaleHandles = GizmoScale.GetHandles(vp, BoxBounds(), out _);
                for (int i = 0; i < scaleHandles.Length; i++)
                {
                    if (Vector2.Distance(vp.WorldToLocal(scaleHandles[i]), mouseLocal) < 10f)
                    {
                        GizmoScale.hoveredHandle = i;
                        break;
                    }
                }
            }

            bool hoveredInterior = GizmoScale.hoveredHandle == -1 && IsInsideBox2D(vp, mouseLocal);
            Color borderColor = hoveredInterior
                ? new Color(255, 220, 100, 255)
                : new Color(255, 160, 60, 220);

            spriteBatch.Begin(blendState: BlendState.NonPremultiplied);

            if (w > 0 && h > 0)
                spriteBatch.FillRectangle(left, top, w, h, new Color(255, 140, 30, 28));

            if (drag2D == Drag2DMode.MoveAll || hoveredInterior)
                spriteBatch.DrawRectangle(left, top, w, h, borderColor, 2f);

            if (hasBox)
                GizmoScale.Render(vp, spriteBatch, scaleGizmoTime, BoxBounds());

            if (hasBox && previewBrush.HasValue)
            {
                var wireVerts = BuildBrushWireframe(previewBrush.Value, Color.White);
                for (int i = 0; i + 1 < wireVerts.Count; i += 2)
                {
                    var p0 = WorldToScreen2D(vp, wireVerts[i].Position);
                    var p1 = WorldToScreen2D(vp, wireVerts[i + 1].Position);
                    spriteBatch.DrawLine(p0, p1, new Color(255, 255, 255, 140), 1f);
                }
            }

            spriteBatch.End();

            if (drag2D == Drag2DMode.None)
            {
                var cursor = GizmoScale.GetActiveCursor();
                if (cursor.HasValue)
                    MouseManager.SetCursor(MainWindow.Instance, cursor.Value);
                else if (hoveredInterior)
                    MouseManager.SetCursor(MainWindow.Instance, StandardCursorType.SizeAll);
            }
        }
        Vector2 WorldToScreen2D(EditorViewport vp, Vector3 world)
        {
            float sx = (Vector3.Dot(world, vp.RightAxis) - vp.Pan.X) * vp.Zoom + vp.Width * 0.5f;
            float sy = -(Vector3.Dot(world, vp.UpAxis) - vp.Pan.Y) * vp.Zoom + vp.Height * 0.5f;
            return new Vector2(sx, sy);
        }
        void Render3D()
        {
            graphicsDevice.BlendState = BlendState.AlphaBlend;
            basicEffect.Alpha = 1f;
            basicEffect.DiffuseColor = Color.MonoGameOrange.ToVector3();
            basicEffect.World = Matrix.CreateWorld(worldPos, Vector3.Forward, Vector3.Up);
            foreach (var pass in basicEffect.CurrentTechnique.Passes)
            { pass.Apply(); graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, nubs, 0, nubs.Length / 3); }
            basicEffect.World = Matrix.Identity;

            if (!hasBox)
            {
                grid.Draw(Viewport3DCamera.viewMatrix, Viewport3DCamera.projectionMatrix, worldPos);
                return;
            }

            Vector3 lo = boxMin, hi = boxMax;

            Vector3 c000 = new(lo.X, lo.Y, lo.Z), c100 = new(hi.X, lo.Y, lo.Z);
            Vector3 c010 = new(lo.X, hi.Y, lo.Z), c110 = new(hi.X, hi.Y, lo.Z);
            Vector3 c001 = new(lo.X, lo.Y, hi.Z), c101 = new(hi.X, lo.Y, hi.Z);
            Vector3 c011 = new(lo.X, hi.Y, hi.Z), c111 = new(hi.X, hi.Y, hi.Z);

            brushTempVerts.Clear();
            // Bottom
            brushTempVerts.Add(new(c000)); brushTempVerts.Add(new(c100));
            brushTempVerts.Add(new(c100)); brushTempVerts.Add(new(c110));
            brushTempVerts.Add(new(c110)); brushTempVerts.Add(new(c010));
            brushTempVerts.Add(new(c010)); brushTempVerts.Add(new(c000));
            // Top
            brushTempVerts.Add(new(c001)); brushTempVerts.Add(new(c101));
            brushTempVerts.Add(new(c101)); brushTempVerts.Add(new(c111));
            brushTempVerts.Add(new(c111)); brushTempVerts.Add(new(c011));
            brushTempVerts.Add(new(c011)); brushTempVerts.Add(new(c001));
            // Verticals
            brushTempVerts.Add(new(c000)); brushTempVerts.Add(new(c001));
            brushTempVerts.Add(new(c100)); brushTempVerts.Add(new(c101));
            brushTempVerts.Add(new(c110)); brushTempVerts.Add(new(c111));
            brushTempVerts.Add(new(c010)); brushTempVerts.Add(new(c011));

            basicEffect.DiffuseColor = BoxBorder.ToVector3();
            basicEffect.Alpha = 1f;
            basicEffect.World = Matrix.Identity;
            foreach (var pass in basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList,
                    brushTempVerts.ToArray(), 0, brushTempVerts.Count / 2);
            }

            basicEffect.Alpha = 0.2f;
            basicEffect.DiffuseColor = BoxFill.ToVector3();
            basicEffect.World = Matrix.Identity;
            var fillVerts = new VertexPosition[]
            {
                // -Z face
                new(c000), new(c110), new(c100),   new(c000), new(c010), new(c110),
                // +Z face  
                new(c001), new(c101), new(c111),   new(c001), new(c111), new(c011),
                // -X face
                new(c000), new(c001), new(c011),   new(c000), new(c011), new(c010),
                // +X face
                new(c100), new(c110), new(c111),   new(c100), new(c111), new(c101),
                // -Y face
                new(c000), new(c100), new(c101),   new(c000), new(c101), new(c001),
                // +Y face
                new(c010), new(c011), new(c111),   new(c010), new(c111), new(c110),
            };
            graphicsDevice.BlendState = BlendState.NonPremultiplied;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            foreach (var pass in basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, fillVerts, 0, fillVerts.Length / 3);
            }
            basicEffect.Alpha = 1f;

            if (previewBrush.HasValue)
            {
                var wireVerts = BuildBrushWireframe(previewBrush.Value, new Color(255, 255, 255, 140));
                if (wireVerts.Count > 1)
                {
                    basicEffect.VertexColorEnabled = true;
                    basicEffect.World = Matrix.Identity;
                    basicEffect.Alpha = 1f;
                    var arr = wireVerts.ToArray();
                    foreach (var pass in basicEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, arr, 0, arr.Length / 2);
                    }
                    basicEffect.VertexColorEnabled = false;
                }
            }

            if (hasBox) return;

            grid.DrawBoundedGrid(Viewport3DCamera.viewMatrix,
                Viewport3DCamera.projectionMatrix, boxMin, boxMax);
        }

        Vector3 MouseLocalToWorld(EditorViewport vp, Vector2 local)
        {
            float gs = Transformable.GridSize;
            float wx = MathF.Round(((local.X - vp.Width * 0.5f) / vp.Zoom + vp.Pan.X) / gs) * gs;
            float wy = MathF.Round(((vp.Height * 0.5f - local.Y) / vp.Zoom + vp.Pan.Y) / gs) * gs;
            float depth = hasBox
                ? (Vector3.Dot(boxMin, vp.ViewAxis) + Vector3.Dot(boxMax, vp.ViewAxis)) * 0.5f
                : 0f;
            return wx * vp.RightAxis + wy * vp.UpAxis + depth * vp.ViewAxis;
        }

        void InitBoxFromPoint(EditorViewport vp, Vector3 worldPt)
        {
            float depth = boxMemory.Max == boxMemory.Min ? -Transformable.GridSize : Vector3.Dot(boxMemory.Min, vp.ViewAxis);
            float height = boxMemory.Max == boxMemory.Min ? 0 : Vector3.Dot(boxMemory.Max, vp.ViewAxis);

            boxMin = worldPt + vp.ViewAxis * depth;
            boxMax = worldPt + vp.ViewAxis * height;
            SetBoxOnAxis(vp.RightAxis, Vector3.Dot(worldPt, vp.RightAxis), Vector3.Dot(worldPt, vp.RightAxis));
            SetBoxOnAxis(vp.UpAxis, Vector3.Dot(worldPt, vp.UpAxis), Vector3.Dot(worldPt, vp.UpAxis));
        }

        void EnsureMinDepth(EditorViewport vp)
        {
            float gs = Transformable.GridSize;
            float dMin = Vector3.Dot(boxMin, vp.ViewAxis);
            float dMax = Vector3.Dot(boxMax, vp.ViewAxis);
            if (MathF.Abs(dMax - dMin) < gs * 0.5f)
            {
                float centre = (dMin + dMax) * 0.5f;
                SetBoxMinOnAxis(vp.ViewAxis, centre - gs * 0.5f);
                SetBoxMaxOnAxis(vp.ViewAxis, centre + gs * 0.5f);
            }
        }

        void ApplyDrag(EditorViewport vp, Vector3 cur)
        {
            float gs = Transformable.GridSize;
            switch (drag2D)
            {
                case Drag2DMode.Drawing:
                    SetBoxOnAxis(vp.RightAxis,
                        MathF.Min(Vector3.Dot(dragStartWorld, vp.RightAxis), Vector3.Dot(cur, vp.RightAxis)),
                        MathF.Max(Vector3.Dot(dragStartWorld, vp.RightAxis), Vector3.Dot(cur, vp.RightAxis)));
                    SetBoxOnAxis(vp.UpAxis,
                        MathF.Min(Vector3.Dot(dragStartWorld, vp.UpAxis), Vector3.Dot(cur, vp.UpAxis)),
                        MathF.Max(Vector3.Dot(dragStartWorld, vp.UpAxis), Vector3.Dot(cur, vp.UpAxis)));
                    break;
                case Drag2DMode.MoveAll:
                    Vector3 delta = cur - dragStartWorld;
                    Vector3 move = SnapVector(Vector3.Dot(delta, vp.RightAxis) * vp.RightAxis
                                            + Vector3.Dot(delta, vp.UpAxis) * vp.UpAxis);
                    boxMin = boxMinAtDragStart + move;
                    boxMax = boxMaxAtDragStart + move;
                    break;
            }
            MarkPreviewDirty();
        }

        bool IsInsideBox2D(EditorViewport vp, Vector2 ml)
        {
            float srMin = (Vector3.Dot(boxMin, vp.RightAxis) - vp.Pan.X) * vp.Zoom + vp.Width * 0.5f;
            float srMax = (Vector3.Dot(boxMax, vp.RightAxis) - vp.Pan.X) * vp.Zoom + vp.Width * 0.5f;
            float suMin = -(Vector3.Dot(boxMin, vp.UpAxis) - vp.Pan.Y) * vp.Zoom + vp.Height * 0.5f;
            float suMax = -(Vector3.Dot(boxMax, vp.UpAxis) - vp.Pan.Y) * vp.Zoom + vp.Height * 0.5f;
            return ml.X > Math.Min(srMin, srMax) && ml.X < Math.Max(srMin, srMax)
                && ml.Y > Math.Min(suMin, suMax) && ml.Y < Math.Max(suMin, suMax);
        }

        bool BoxHasArea(EditorViewport vp)
        {
            float gs = Transformable.GridSize * 0.5f;
            return MathF.Abs(Vector3.Dot(boxMax - boxMin, vp.RightAxis)) > gs
                && MathF.Abs(Vector3.Dot(boxMax - boxMin, vp.UpAxis)) > gs;
        }

        void NormaliseBox()
        {
            var lo = new Vector3(Math.Min(boxMin.X, boxMax.X), Math.Min(boxMin.Y, boxMax.Y), Math.Min(boxMin.Z, boxMax.Z));
            var hi = new Vector3(Math.Max(boxMin.X, boxMax.X), Math.Max(boxMin.Y, boxMax.Y), Math.Max(boxMin.Z, boxMax.Z));
            boxMin = lo; boxMax = hi;
        }

        void SetBoxOnAxis(Vector3 axis, float min, float max)
        { SetBoxMinOnAxis(axis, min); SetBoxMaxOnAxis(axis, max); }

        void SetBoxMinOnAxis(Vector3 axis, float val)
        { boxMin -= Vector3.Dot(boxMin, axis) * axis; boxMin += val * axis; }

        void SetBoxMaxOnAxis(Vector3 axis, float val)
        { boxMax -= Vector3.Dot(boxMax, axis) * axis; boxMax += val * axis; }

        Vector3 SnapVector(Vector3 v)
        {
            float gs = Transformable.GridSize;
            return new Vector3(MathF.Round(v.X / gs) * gs, MathF.Round(v.Y / gs) * gs, MathF.Round(v.Z / gs) * gs);
        }

        static Vector3 SnapPointToPlane(Vector3 point, Plane plane, Vector3? origin = null)
        {
            Vector3 up = plane.Normal;
            Vector3 right = Vector3.Cross(up, Vector3.Up);
            if (right.LengthSquared() < 0.01f) right = Vector3.Cross(up, Vector3.Right);
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, up);
            Vector3 refOrigin = origin ?? Vector3.Zero;
            float d = plane.DotCoordinate(point);
            Vector3 local = (point - plane.Normal * d) - refOrigin;
            float x = MathF.Round(Vector3.Dot(local, right) / Transformable.GridSize) * Transformable.GridSize;
            float y = MathF.Round(Vector3.Dot(local, forward) / Transformable.GridSize) * Transformable.GridSize;
            Vector3 snapped = refOrigin + right * x + forward * y;
            return snapped -= plane.Normal * plane.DotCoordinate(snapped);
        }
        List<VertexPositionColor> BuildBrushWireframe(Brush brush, Color color)
        {
            var verts = new List<VertexPositionColor>();

            foreach (var face in brush.Faces)
            {
                if (face.Indices == null || face.Indices.Length < 3) continue;

                var ring = face.Indices.Distinct().ToList();
                if (ring.Count < 3) continue;

                Vector3 center = Vector3.Zero;
                foreach (var idx in ring) center += brush.Vertices[idx];
                center /= ring.Count;

                Vector3 normal = face.Normal;
                Vector3 refAxis = Vector3.Cross(normal, Vector3.UnitZ);
                if (refAxis.LengthSquared() < 1e-6f)
                    refAxis = Vector3.Cross(normal, Vector3.UnitX);
                refAxis = Vector3.Normalize(refAxis);
                Vector3 perpAxis = Vector3.Normalize(Vector3.Cross(normal, refAxis));

                ring.Sort((a, b) =>
                {
                    Vector3 da = brush.Vertices[a] - center;
                    Vector3 db = brush.Vertices[b] - center;
                    float angA = MathF.Atan2(Vector3.Dot(da, perpAxis), Vector3.Dot(da, refAxis));
                    float angB = MathF.Atan2(Vector3.Dot(db, perpAxis), Vector3.Dot(db, refAxis));
                    return angA.CompareTo(angB);
                });

                for (int i = 0; i < ring.Count; i++)
                {
                    Vector3 p0 = brush.Vertices[ring[i]] + brush.Position;
                    Vector3 p1 = brush.Vertices[ring[(i + 1) % ring.Count]] + brush.Position;
                    verts.Add(new VertexPositionColor(p0, color));
                    verts.Add(new VertexPositionColor(p1, color));
                }
            }

            return verts;
        }
        static Vector3 SnapToCardinalAxis(Vector3 v)
        {
            Vector3 a = new Vector3(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
            if (a.X >= a.Y && a.X >= a.Z) return Vector3.UnitX;
            if (a.Y >= a.X && a.Y >= a.Z) return Vector3.UnitY;
            return Vector3.UnitZ;
        }
    }
}
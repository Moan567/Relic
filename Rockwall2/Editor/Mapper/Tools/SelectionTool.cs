using Avalonia.Input;
using Avalonia.Threading;
using DefaultUnDo;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using MsBox.Avalonia;
using MsBox.Avalonia.Models;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Mapper.Tools;
using Rockwall2.Editor.Mapper.Utils;
using Rockwall2.Editor.Mapper.ViewportManagement;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2.Tools
{
    public class SelectionTool : Tool
    {
        public enum Bonus3DToolMode { None, Move, Rotate }
        public Bonus3DToolMode Bonus3DTool = Bonus3DToolMode.None;

        float scaleGizmoTime;

        Vector3 worldPos;
        Vector3 selectionAtDragStart;
        bool movedAny;
        bool wasDragging;
        float totalScrollDelta;
        bool drag;
        bool rotateDrag;
        EditorViewport drag2DViewport;
        Vector3 dragStartWorld;
        Vector3 dragFloating;

        (int brush, bool isDragging) pivotDragState;
        AllBrushSnapshot pivotDragSnapshot;

        bool isHoldingFromVert;

        bool isAltDragging;
        List<Transformable> drag2DTargets;
        List<(Brush brush, int face)> altDragFakeBrushes;
        List<Plane> altDragOriginalPlanes;
        Vector3 altDragMoveDirection;

        bool waitDDown;

        bool paintSelecting;

        bool boxSelecting;
        EditorViewport boxSelectViewport;
        Vector3 boxSelectStart;
        Vector3 boxSelectCurrent;

        readonly Dictionary<EditorViewport, List<int>> drillIgnore = new();

        bool waitMenu = false;

        public static int ResolveBrushForEditing(Transformable o) =>
            o is BrushMoveable bm ? bm.brush :
            o is FaceMoveable fm ? fm.brush :
            o is BrushVertexMoveable bv ? bv.brush :
            o is BrushEdgeMoveable be ? be.brush :
            o is TerrainMoveable tm && tm.terrain >= 0 && tm.terrain < MapTools.Terrains.Length ? MapTools.Terrains[tm.terrain].BrushSource :
            -1;


        public override void OnSelected()
        {
            GizmoRotate.ResetPivot();
        }
        public override void OnDeselected() { wasDragging = false; paintSelecting = false; }

        public override void OnUpdate(float delta)
        {
            scaleGizmoTime += delta;
            if (KeyboardManager.IsPressed(Key.Escape)) Toolbelt.SelectedObjects.Clear();

            if (KeyboardManager.IsPressed(Key.Q))
            {
                Bonus3DTool = (Bonus3DToolMode)(((int)Bonus3DTool + 1) % Enum.GetValues<Bonus3DToolMode>().Length);
            }

            GizmoRotate.UpdateGeneral();

            if (IsUsedIn2D) { Update2D(); return; }
            Update3D(delta);
        }

        void Update3D(float dt)
        {
            if (waitMenu) return;

            if (DecalBoundsGizmo.IsDragging || DecalBoundsGizmo.IsHovering || DecalUVPreviewWindow.IsDragging || DecalUVPreviewWindow.IsHovering)
            {
                DecalUVPreviewWindow.Update();
                DecalBoundsGizmo.Update();

                return;
            }

            if (paintSelecting && !MouseManager.IsDown(MouseButton.Left))
                paintSelecting = false;

            if (!paintSelecting)
            {
                switch (Bonus3DTool)
                {
                    case Bonus3DToolMode.Move:

                        Toolbelt.TranslateTool.OnUpdate(dt);

                        if (GizmoTranslate.IsDragging) return;

                        break;
                    case Bonus3DToolMode.Rotate:

                        Toolbelt.RotationTool.OnUpdate(dt);

                        if (GizmoRotate.IsDragging) return;

                        break;
                }

                DecalUVPreviewWindow.Update();
                DecalBoundsGizmo.Update();
            }

            var ray = SceneRay;

            if (isAltDragging)
            {
                HandleFaceExtruding();
                return;
            }

            wasDragging = false;

            if ((KeyboardManager.IsDown(Key.LeftShift) || KeyboardManager.IsDown(Key.LeftAlt)) && Toolbelt.SelectedObjects.OfType<BrushMoveable>().Any())
            {
                var brushes = Toolbelt.SelectedObjects.OfType<BrushMoveable>();
                HandleFaceSelectionForActiveBrush();
            }

            float minDist = float.MaxValue;
            var mapHit = MapTools.RaycastMapGeometry(ray, true, true);
            Toolbelt.HighlightedObject = null;

            if (mapHit.distance > 0f && mapHit.face != -1 && mapHit.brush != -1)
            {
                Toolbelt.HighlightedObject = (KeyboardManager.IsDown(Key.LeftShift) != Toolbelt.FlipSelectShift)
                        ? new BrushMoveable(mapHit.brush)
                        : (Transformable)new FaceMoveable(mapHit.brush, mapHit.face);

                worldPos = ray.Position + ray.Direction * mapHit.distance;
                minDist = mapHit.distance;
            }

            var terrainHit = MapTools.RaycastTerrains(ray);
            if (terrainHit.distance > 0f && terrainHit.distance - 0.01f < minDist && terrainHit.terrain != -1)
            {
                Toolbelt.HighlightedObject = new TerrainMoveable(terrainHit.terrain);
                worldPos = ray.Position + ray.Direction * terrainHit.distance;
                minDist = terrainHit.distance;
            }

            var entHit = MapTools.RaycastEntities(ray);
            if (entHit.distance > 0f && entHit.distance < minDist && entHit.entity != -1)
            {
                Toolbelt.HighlightedObject = new EntityMoveable(entHit.entity);
                worldPos = ray.Position + ray.Direction * entHit.distance;
                minDist = entHit.distance;
            }

            var hintHit = MapTools.RaycastHints(ray);
            if (hintHit.distance > 0f && hintHit.distance < minDist && hintHit.hint != -1)
            {
                Toolbelt.HighlightedObject = new HintMoveable(hintHit.hint);
                worldPos = ray.Position + ray.Direction * hintHit.distance;
            }

            if (!KeyboardManager.IsDown(Key.Space))
            {
                if (!paintSelecting && MouseManager.IsDragging(MouseButton.Left))
                {
                    paintSelecting = true;
                    if (!KeyboardManager.IsDown(Key.LeftCtrl)) Toolbelt.SelectedObjects.Clear();
                }

                if (paintSelecting) HandlePaintSelect();
                else if (MouseManager.IsPressed(MouseButton.Left)) HandleSelection();
            }

            HandleFaceExtruding();
            HandleFaceShifting();
            HandleTextureApplication();
            HandleDelete();
            HandleDuplicate();
            HandleBrushEntityConvert();
            HandleMergeFaces();
            HandleCopyPaste();

            if (MouseManager.IsReleased(MouseButton.Right) && !MouseManager.WasDragging(MouseButton.Right) && !waitDDown && Toolbelt.SelectedObjects.Count > 0)
            {
                waitDDown = true;
                Dispatcher.UIThread.Post(() =>
                {
                    var menu = new Avalonia.Controls.ContextMenu();
                    menu.MaxHeight = 300;

                    if (Toolbelt.SelectedObjects[0] is EntityMoveable ent ||
                       (Toolbelt.SelectedObjects[0] is BrushMoveable brush && MapTools.Brushes[brush.brush].IsEntity))
                    {
                        Avalonia.Controls.MenuItem item = new();

                        item.Header = "Open Properties";
                        item.Click += (a, b) =>
                        {
                            var entityRefs = Toolbelt.SelectedObjects.OfType<EntityMoveable>()
                                .Select(i => MapTools.Entities[i.entity])
                                .Concat(Toolbelt.SelectedObjects.OfType<BrushMoveable>()
                                    .Select(b => MapTools.GetOwningEntity(b.brush)))
                                .Concat(Toolbelt.SelectedObjects.OfType<FaceMoveable>()
                                    .Select(f => MapTools.GetOwningEntity(f.brush)))
                                .Where(e => e != null)
                                .Distinct();
                            if (entityRefs.Any())
                            {
                                MapperView.OpenEntityInspector(entityRefs);
                                menu.Close();
                            }
                        };

                        menu.Items.Add(item);
                    }

                    var hitpt = ray.Position + ray.Direction * minDist;

                    (string header, Action onclick)[] actions = new (string header, Action onclick)[]
                    {
                        ("Place Point Light", () =>
                        {
                            var pos = Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize;

                            var entity = new EntityReference
                            {
                                Position = pos,
                                EntityName = "PointLight",
                                EntityOutputs = new System.Collections.Generic.List<(string, EntityOutput)>(),
                                Scale = Vector3.One,
                            };

                            var id = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == entity.EntityName);
                            if (id != -1 && GlobalEditorData.EditorOverrides.overrides[id].defaultProperties != null)
                            {
                                entity.Properties = new EntityProperty[GlobalEditorData.EditorOverrides.overrides[id].defaultProperties.Length];
                                Array.Copy(GlobalEditorData.EditorOverrides.overrides[id].defaultProperties, entity.Properties, entity.Properties.Length);
                            }

                            MapTools.AddEntity(entity);
                            var entityRef = entity;

                            Toolbelt.UndoManager.DoOnUndo(() =>
                            {
                                MapTools.RemoveEntity(entityRef);
                                MapTools.FinalizeDeletedObjects();
                            });
                        }),
                        ("Place Directional Light", () =>
                        {
                            var pos = Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize;

                            var entity = new EntityReference
                            {
                                Position = pos,
                                EntityName = "DirectionalLight",
                                EntityOutputs = new System.Collections.Generic.List<(string, EntityOutput)>(),
                                Scale = Vector3.One,
                            };

                            var id = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == entity.EntityName);
                            if (id != -1 && GlobalEditorData.EditorOverrides.overrides[id].defaultProperties != null)
                            {
                                entity.Properties = new EntityProperty[GlobalEditorData.EditorOverrides.overrides[id].defaultProperties.Length];
                                Array.Copy(GlobalEditorData.EditorOverrides.overrides[id].defaultProperties, entity.Properties, entity.Properties.Length);
                            }

                            MapTools.AddEntity(entity);
                            var entityRef = entity;

                            Toolbelt.UndoManager.DoOnUndo(() =>
                            {
                                MapTools.RemoveEntity(entityRef);
                                MapTools.FinalizeDeletedObjects();
                            });
                        }),
                        ("Place Player", () =>
                        {
                            var pos = Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize;

                            var entity = new EntityReference
                            {
                                Position = pos,
                                EntityName = "Player",
                                EntityOutputs = new System.Collections.Generic.List<(string, EntityOutput)>(),
                                Scale = Vector3.One,
                            };

                            var id = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == entity.EntityName);
                            if (id != -1 && GlobalEditorData.EditorOverrides.overrides[id].defaultProperties != null)
                            {
                                entity.Properties = new EntityProperty[GlobalEditorData.EditorOverrides.overrides[id].defaultProperties.Length];
                                Array.Copy(GlobalEditorData.EditorOverrides.overrides[id].defaultProperties, entity.Properties, entity.Properties.Length);
                            }

                            MapTools.AddEntity(entity);
                            var entityRef = entity;

                            Toolbelt.UndoManager.DoOnUndo(() =>
                            {
                                MapTools.RemoveEntity(entityRef);
                                MapTools.FinalizeDeletedObjects();
                            });
                        })
                    };

                    foreach (var src in actions)
                    {
                        Avalonia.Controls.MenuItem item = new();

                        item.Header = src.header;
                        item.Click += (a, b) =>
                        {
                            src.onclick();
                        };

                        menu.Items.Add(item);
                    }

                    menu.Closed += (s, e) => waitDDown = false;

                    menu.Open(MainWindow.Instance);
                });
            }
            else
            {
                waitDDown = false;
            }

            // Later.
            //HandleQuickContextMenu();
        }
        void Update2D()
        {
            if (waitMenu) return;

            var vp = ActiveViewport;
            var ray = SceneRay;
            var mouse = MouseLocalF;

            bool wasDrag = drag;

            if (GizmoScale.isDragging)
            {
                MouseManager.SetCursor(MainWindow.Instance, GizmoScale.GetActiveCursor()!.Value);

                GizmoScale.Update(vp, LocalToWorld(vp, mouse));
                return;
            }

            if (!drag && !pivotDragState.isDragging && !boxSelecting)
            {
                bool isDragging = GizmoRotate.IsDragging;
                if (isDragging && !rotateDrag)
                    foreach (var obj in Toolbelt.SelectedObjects)
                        obj.PrepareMove();
                if (!isDragging && rotateDrag)
                    Toolbelt.CreateUndoStateForAllSelectedObjects();

                rotateDrag = isDragging;
                bool anchor = Toolbelt.SelectedObjects.Count > 0 && (Toolbelt.SelectedObjects[0] is not EntityMoveable || Toolbelt.SelectedObjects.Count > 1);
                GizmoRotate.UpdateUse(anchor);
                if (rotateDrag || GizmoRotate.DraggingPivot || GizmoRotate.WasDraggingPivot) return;
            }

            if (pivotDragState.isDragging)
            {
                if (!MouseManager.IsDown(MouseButton.Left))
                {
                    pivotDragState.isDragging = false;

                    Toolbelt.UndoManager.DoOnUndo(() =>
                    {
                        pivotDragSnapshot.Restore();
                    });

                    return;
                }
                MouseManager.SetCursor(MainWindow.Instance, StandardCursorType.SizeAll);

                Vector3 delta = LocalToWorld(vp, mouse) - dragFloating;
                Vector3 move = OtherMath.SnapVector(Vector3.Dot(delta, vp.RightAxis) * vp.RightAxis + Vector3.Dot(delta, vp.UpAxis) * vp.UpAxis);
                dragFloating = LocalToWorld(vp, mouse);

                if (move.Length() > 0)
                {
                    ref var brush = ref MapTools.Brushes[pivotDragState.brush];

                    brush.Vertices = brush.Vertices.Select(v => v - move).ToArray();
                    brush.Position += move;

                    BrushOperations.RecalculateBrushPlanes(ref brush);
                    BrushOperations.RebuildBrush(ref brush);
                    MapTools.Brushes[pivotDragState.brush] = brush;
                    MapTools.RecomputeBrushBounds(pivotDragState.brush);
                }
                return;
            }

            if (drag)
            {
                if (!MouseManager.IsDown(MouseButton.Left))
                {
                    drag = false;
                    foreach (var sel in drag2DTargets) sel.PostMove();
                    Toolbelt.CreateUndoStateForAllSelectedObjects();
                    return;
                }
                MouseManager.SetCursor(MainWindow.Instance, StandardCursorType.SizeAll);

                Vector3 delta = LocalToWorld(vp, mouse) - dragFloating;
                Vector3 move = OtherMath.SnapVector(Vector3.Dot(delta, vp.RightAxis) * vp.RightAxis
                                                    + Vector3.Dot(delta, vp.UpAxis) * vp.UpAxis);
                dragFloating = LocalToWorld(vp, mouse);

                foreach (var sel in drag2DTargets) sel.Move(move);

                return;
            }

            if (boxSelecting)
            {
                UpdateBoxSelect(vp, mouse);
                return;
            }

            if (Toolbelt.SelectedObjects.Count > 0)
            {
                foreach (var bi in Toolbelt.GetSelectedBrushIds())
                {
                    if (bi == -1) continue;

                    var brush = MapTools.Brushes[bi];

                    if (Vector2.Distance(mouse, vp.WorldToLocal(brush.Position)) < 10f)
                    {
                        if (MouseManager.IsDown(MouseButton.Left))
                        {
                            pivotDragState.isDragging = true;
                            pivotDragState.brush = bi;

                            dragFloating = dragStartWorld = LocalToWorld(vp, mouse);

                            pivotDragSnapshot = new AllBrushSnapshot(MapTools.Brushes);
                        }
                        return;
                    }
                }

                GizmoScale.hoveredHandle = -1;
                if (!GizmoScale.isDragging && (Toolbelt.SelectedObjects[0] is not EntityMoveable || Toolbelt.SelectedObjects.Count > 1))
                {
                    var scaleHandles = GizmoScale.GetHandles(vp, out _);
                    for (int i = 0; i < scaleHandles.Length; i++)
                    {
                        if (Vector2.Distance(vp.WorldToLocal(scaleHandles[i]), mouse) < 10f)
                        {
                            GizmoScale.hoveredHandle = i;
                            MouseManager.SetCursor(MainWindow.Instance, GizmoScale.HandleCursors[i]);
                            if (MouseManager.IsDown(MouseButton.Left)) GizmoScale.BeginDrag(i, vp);
                            return;
                        }
                    }
                }

                var bounds = Toolbelt.GetSelectionBounds();

                if (DragUtils.IsInsideBox2D(vp, mouse, bounds.Min, bounds.Max))
                {
                    if (MouseManager.IsDragging(MouseButton.Left))
                    {
                        BeginDrag(vp, LocalToWorld(vp, mouse));
                        dragFloating = dragStartWorld;
                        foreach (var sel in drag2DTargets) sel.PrepareMove();
                        return;
                    }
                }
            }

            if (!KeyboardManager.IsDown(Key.Space) && MouseManager.IsDragging(MouseButton.Left))
            {
                BeginBoxSelect(vp, mouse);
                return;
            }

            if (!drillIgnore.ContainsKey(vp)) drillIgnore[vp] = new();

            var mapHit = MapTools.RaycastMapGeometry(ray, false, drillIgnore[vp].ToArray());

            if (mapHit.brush != -1 && mapHit.distance > 0f)
            {
                if ((Toolbelt.SelectedObjects.Any(o =>
                    (o is BrushMoveable bm && bm.brush == mapHit.brush) ||
                    (o is FaceMoveable fm && fm.brush == mapHit.brush)) ||
                    (drillIgnore[vp].Count > 0 && ray.Intersects(MapTools.BrushBounds[drillIgnore[vp][0]]).HasValue)))
                {
                    if (MouseManager.IsPressed(MouseButton.Left) || MouseManager.IsDown(MouseButton.Right))
                    {
                        drillIgnore[vp].Add(mapHit.brush);
                        mapHit = MapTools.RaycastMapGeometry(ray, false, drillIgnore[vp].ToArray());
                    }
                }
                else
                {
                    drillIgnore[vp].Clear();
                }

                if (mapHit.brush != -1 && mapHit.distance > 0f)
                {
                    var obj = new BrushMoveable(mapHit.brush);
                    Toolbelt.HighlightedObject = obj;
                }
            }
            else
            {
                drillIgnore[vp].Clear();
                Toolbelt.HighlightedObject = null;
            }

            var entHit = MapTools.RaycastEntities(ray);
            if (entHit.distance > 0f && entHit.entity != -1)
            {
                Toolbelt.HighlightedObject = new EntityMoveable(entHit.entity);
                worldPos = ray.Position + ray.Direction * entHit.distance;
            }

            if (KeyboardManager.IsPressed(Key.Right))
            {
                ShiftSelectionLocal(ActiveViewport, 1, 0);
            }
            if (KeyboardManager.IsPressed(Key.Left))
            {
                ShiftSelectionLocal(ActiveViewport, -1, 0);
            }
            if (KeyboardManager.IsPressed(Key.Up))
            {
                ShiftSelectionLocal(ActiveViewport, 0, 1);
            }
            if (KeyboardManager.IsPressed(Key.Down))
            {
                ShiftSelectionLocal(ActiveViewport, 0, -1);
            }

            if ((MouseManager.IsReleased(MouseButton.Left) && !wasDrag)
                || MouseManager.IsDown(MouseButton.Right))
                HandleSelection();

            HandleDelete();
            HandleDuplicate();
            HandleCopyPaste();
        }
        void ShiftSelectionLocal(EditorViewport vp, int x, int y)
        {
            Vector3 delta = vp.RightAxis * x + vp.UpAxis * y;
            delta *= Transformable.GridSize;
            MoveSelected(delta);
        }
        void MoveSelected(Vector3 delta)
        {
            foreach (var sel in Toolbelt.GetTransformablesFor2D())
            {
                sel.PrepareMove();
                sel.Move(delta);
                sel.PostMove();
            }
            Toolbelt.CreateUndoStateForAllSelectedObjects();
        }

        void BeginBoxSelect(EditorViewport vp, Vector2 mouse)
        {
            boxSelecting = true;
            boxSelectViewport = vp;
            boxSelectStart = LocalToWorld(vp, mouse);
            boxSelectCurrent = boxSelectStart;
        }

        void UpdateBoxSelect(EditorViewport vp, Vector2 mouse)
        {
            boxSelectCurrent = LocalToWorld(vp, mouse);

            if (!MouseManager.IsDown(MouseButton.Left))
                FinishBoxSelect(vp);
        }

        void FinishBoxSelect(EditorViewport vp)
        {
            boxSelecting = false;

            var rect = RectFromPoints(vp.WorldToLocal(boxSelectStart), vp.WorldToLocal(boxSelectCurrent));

            if (rect.Width < 3f && rect.Height < 3f) return;

            if (!KeyboardManager.IsDown(Key.LeftCtrl))
            {
                Toolbelt.SelectedObjects.Clear();
                GizmoRotate.ResetPivot();
            }

            BoxSelectFaceMode(vp, rect);

            TextureSettingsWindow.Instance?.UpdateValues();
        }

        void BoxSelectFaceMode(EditorViewport vp, RectangleF rect)
        {
            for (int bi = 0; bi < MapTools.BrushBounds.Length; bi++)
            {
                if (!RectsOverlap(ProjectBoundsToLocal(vp, MapTools.BrushBounds[bi]), rect)) continue;
                if (Toolbelt.SelectedObjects.Any(o => o is BrushMoveable bm && bm.brush == bi)) continue;

                Toolbelt.SelectedObjects.Add(new BrushMoveable(bi));
            }

            // Note: terrains and hints aren't included here yet
            foreach (var (entity, idx) in MapTools.Entities.Select((e, i) => (e, i)))
            {
                var local = vp.WorldToLocal(entity.Position);
                if (!RectContainsPoint(rect, local)) continue;
                if (Toolbelt.SelectedObjects.Any(o => o is EntityMoveable em && em.entity == idx)) continue;

                Toolbelt.SelectedObjects.Add(new EntityMoveable(idx));
            }
        }

        static RectangleF ProjectBoundsToLocal(EditorViewport vp, BoundingBox bounds)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            foreach (var corner in bounds.GetCorners())
            {
                var local = vp.WorldToLocal(corner);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            return new RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }

        public static RectangleF RectFromPoints(Vector2 a, Vector2 b)
        {
            var min = Vector2.Min(a, b);
            var max = Vector2.Max(a, b);
            return new RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }

        static bool RectContainsPoint(RectangleF r, Vector2 p) =>
            p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

        static bool RectsOverlap(RectangleF a, RectangleF b) =>
            a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;

        public override void OnRender(float delta)
        {
            if (IsRenderingIn2D)
            {
                Render2D(RenderingViewport);
                return;
            }
            Render3D(delta);
        }

        void Render3D(float dt)
        {
            DecalUVPreviewWindow.Draw(EditorHost.Instance.GraphicsDevice, MapperView.Instance.SpriteBatch);
            DecalBoundsGizmo.Draw(EditorHost.Instance.GraphicsDevice, MapperView.Instance.BasicEffect, MapperView.Instance.SpriteBatch);

            switch (Bonus3DTool)
            {
                case Bonus3DToolMode.Move:

                    Toolbelt.TranslateTool.OnRender(dt);

                    break;
                case Bonus3DToolMode.Rotate:

                    Toolbelt.RotationTool.OnRender(dt);

                    break;
            }

            RenderAltDrag();
        }
        void Render2D(EditorViewport vp)
        {
            RenderAltDrag();

            MapperView.Instance.DrawRotationGizmo();

            spriteBatch.Begin(blendState: BlendState.NonPremultiplied);

            if (Toolbelt.SelectedObjects.Count > 0 && (Toolbelt.SelectedObjects[0] is not EntityMoveable || Toolbelt.SelectedObjects.Count > 1))
                GizmoScale.Render(vp, spriteBatch, scaleGizmoTime);

            if (boxSelecting && boxSelectViewport == vp)
            {
                var boxRect = RectFromPoints(vp.WorldToLocal(boxSelectStart), vp.WorldToLocal(boxSelectCurrent));

                var tl = new Vector2(boxRect.Left, boxRect.Top);
                var tr = new Vector2(boxRect.Right, boxRect.Top);
                var br = new Vector2(boxRect.Right, boxRect.Bottom);
                var bl = new Vector2(boxRect.Left, boxRect.Bottom);

                RenderUtils.DrawDashedLine(spriteBatch, tl, tr, GizmoScale.DashColor, 2f, scaleGizmoTime);
                RenderUtils.DrawDashedLine(spriteBatch, tr, br, GizmoScale.DashColor, 2f, scaleGizmoTime);
                RenderUtils.DrawDashedLine(spriteBatch, br, bl, GizmoScale.DashColor, 2f, scaleGizmoTime);
                RenderUtils.DrawDashedLine(spriteBatch, bl, tl, GizmoScale.DashColor, 2f, scaleGizmoTime);

                float worldW = MathF.Abs(Vector3.Dot(boxSelectCurrent - boxSelectStart, vp.RightAxis));
                float worldH = MathF.Abs(Vector3.Dot(boxSelectCurrent - boxSelectStart, vp.UpAxis));

                var font = MapperView.Instance.FontSystem.GetFont(10);
                var labelColor = GizmoScale.DashColor;

                var widthLabel = $"{worldW:0.000}";
                var widthSize = font.MeasureString(widthLabel);
                spriteBatch.DrawString(font, widthLabel,
                    new Vector2((tl.X + tr.X) * 0.5f - widthSize.X * 0.5f, tl.Y - widthSize.Y - 4), labelColor);

                var heightLabel = $"{worldH:0.000}";
                var heightSize = font.MeasureString(heightLabel);
                spriteBatch.DrawString(font, heightLabel,
                    new Vector2(tl.X - heightSize.X - 4, (tl.Y + bl.Y) * 0.5f - heightSize.Y * 0.5f), labelColor);
            }

            if (Toolbelt.SelectedObjects.Count > 0 && !rotateDrag && !GizmoRotate.DraggingPivot && !GizmoRotate.WasDraggingPivot)
            {
                foreach (var bi in Toolbelt.GetSelectedBrushIds())
                {
                    if (bi == -1) continue;
                    var brush = MapTools.Brushes[bi];

                    var pivotScreen = vp.WorldToLocal(brush.Position);
                    var pivotColor = pivotDragState.isDragging ? Color.LightGray : Color.DarkGray;
                    spriteBatch.DrawCircle(new CircleF(pivotScreen, 6), 12, pivotColor, 2f);
                    spriteBatch.DrawLine(pivotScreen - Vector2.UnitX * 8, pivotScreen + Vector2.UnitX * 8, pivotColor, 1.0f);
                    spriteBatch.DrawLine(pivotScreen - Vector2.UnitY * 8, pivotScreen + Vector2.UnitY * 8, pivotColor, 1.0f);
                }
            }

            spriteBatch.End();
        }
        void RenderAltDrag()
        {
            if (!isAltDragging || altDragFakeBrushes == null) return;

            var FillColor = new Color(255, 140, 30, 28);
            var BorderColor = new Color(255, 160, 60, 220);
            var SliceColor = new Color(255, 30, 30, 220);

            for (int bi = 0; bi < altDragFakeBrushes.Count; bi++)
            {
                var (brush, faceIndex) = altDragFakeBrushes[bi];
                if (brush.Vertices == null || brush.Faces == null) continue;

                var face = brush.Faces[faceIndex];
                if (!face.Plane.HasValue) continue;

                Plane currentPlane = face.Plane.Value;
                Plane originalPlane = altDragOriginalPlanes[bi];

                bool isSlicing = currentPlane.D > originalPlane.D;

                if (isSlicing)
                {
                    // Just draw the face as a red outline
                    var faceVerts = new List<VertexPosition>();
                    var seenEdges = new HashSet<(int, int)>();

                    // Collect unique verts of the face polygon for a line loop
                    var uniqueVerts = face.Indices
                        .Select(i => brush.Vertices[i] + brush.Position)
                        .Distinct()
                        .ToList();

                    // Build line loop around face
                    var loopVerts = new List<VertexPosition>();
                    for (int i = 0; i < uniqueVerts.Count; i++)
                    {
                        loopVerts.Add(new VertexPosition(uniqueVerts[i]));
                        loopVerts.Add(new VertexPosition(uniqueVerts[(i + 1) % uniqueVerts.Count]));
                    }

                    basicEffect.DiffuseColor = SliceColor.ToVector3();
                    basicEffect.Alpha = 1f;
                    basicEffect.LightingEnabled = false;
                    basicEffect.VertexColorEnabled = false;
                    basicEffect.World = Matrix.Identity;
                    foreach (var pass in basicEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        if (loopVerts.Count >= 2)
                            graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList,
                                loopVerts.ToArray(), 0, loopVerts.Count / 2);
                    }
                }
                else
                {
                    var fillVerts = new List<VertexPosition>();
                    var lineVerts = new List<VertexPosition>();

                    // Use brush planes + original plane as cap to find clipped vertices
                    var clipPlanes = brush.Faces
                        .Where(f => f.Plane.HasValue)
                        .Select(f => f.Plane.Value)
                        .ToList();
                    clipPlanes.Add(new Plane(-originalPlane.Normal, -originalPlane.D));

                    var clippedVerts = new List<Vector3>();
                    for (int i = 0; i < clipPlanes.Count - 2; i++)
                    {
                        for (int j = i + 1; j < clipPlanes.Count - 1; j++)
                        {
                            for (int k = j + 1; k < clipPlanes.Count; k++)
                            {
                                var v = BrushOperations.IntersectThreePlanes(
                                    clipPlanes[i], clipPlanes[j], clipPlanes[k]);

                                if (!v.HasValue) continue;

                                bool inside = true;
                                for (int p = 0; p < clipPlanes.Count; p++)
                                {
                                    if (p == i || p == j || p == k) continue;
                                    if (clipPlanes[p].DotCoordinate(v.Value) > 0.01f)
                                    {
                                        inside = false;
                                        break;
                                    }
                                }

                                if (inside && !clippedVerts.Any(e => Vector3.DistanceSquared(e, v.Value) < 0.001f))
                                    clippedVerts.Add(v.Value + brush.Position);
                            }
                        }
                    }

                    // For each clip plane, gather its verts, sort CCW, then emit edges + fill
                    for (int fi = 0; fi < clipPlanes.Count; fi++)
                    {
                        var facePts = clippedVerts.Where(v =>
                            MathF.Abs(clipPlanes[fi].DotCoordinate(v - brush.Position)) < 0.1f).ToList();

                        if (facePts.Count < 3) continue;

                        Vector3 faceCenter = Vector3.Zero;
                        foreach (var p in facePts) faceCenter += p;
                        faceCenter /= facePts.Count;

                        Vector3 faceNormal = clipPlanes[fi].Normal;
                        Vector3 refDir = Vector3.Normalize(facePts[0] - faceCenter);
                        facePts.Sort((a, b) =>
                        {
                            Vector3 da = Vector3.Normalize(a - faceCenter);
                            Vector3 db = Vector3.Normalize(b - faceCenter);
                            float angleA = MathF.Atan2(Vector3.Dot(Vector3.Cross(refDir, da), faceNormal), Vector3.Dot(refDir, da));
                            float angleB = MathF.Atan2(Vector3.Dot(Vector3.Cross(refDir, db), faceNormal), Vector3.Dot(refDir, db));
                            return angleA.CompareTo(angleB);
                        });

                        for (int i = 0; i < facePts.Count; i++)
                        {
                            lineVerts.Add(new VertexPosition(facePts[i]));
                            lineVerts.Add(new VertexPosition(facePts[(i + 1) % facePts.Count]));
                        }

                        for (int i = 1; i < facePts.Count - 1; i++)
                        {
                            fillVerts.Add(new VertexPosition(facePts[0]));
                            fillVerts.Add(new VertexPosition(facePts[i]));
                            fillVerts.Add(new VertexPosition(facePts[i + 1]));
                        }
                    }

                    if (fillVerts.Count >= 3)
                    {
                        basicEffect.DiffuseColor = FillColor.ToVector3();
                        basicEffect.Alpha = 0.2f;
                        basicEffect.LightingEnabled = false;
                        basicEffect.VertexColorEnabled = false;
                        basicEffect.World = Matrix.Identity;
                        graphicsDevice.BlendState = BlendState.NonPremultiplied;
                        graphicsDevice.RasterizerState = RasterizerState.CullNone;
                        foreach (var pass in basicEffect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList,
                                fillVerts.ToArray(), 0, fillVerts.Count / 3);
                        }
                    }

                    if (lineVerts.Count >= 2)
                    {
                        basicEffect.DiffuseColor = BorderColor.ToVector3();
                        basicEffect.Alpha = 1f;
                        basicEffect.World = Matrix.Identity;
                        graphicsDevice.BlendState = BlendState.Opaque;
                        foreach (var pass in basicEffect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList,
                                lineVerts.ToArray(), 0, lineVerts.Count / 2);
                        }
                    }
                }
            }

            basicEffect.Alpha = 1f;
        }

        void HandleQuickContextMenu()
        {
            if (KeyboardManager.IsPressed(Key.M) && !waitMenu)
            {
                waitMenu = true;
                Dispatcher.UIThread.Post(() =>
                {
                    var menu = new Avalonia.Controls.ContextMenu();
                    menu.MaxHeight = 300;

                    var quickFixedConstraint = new Avalonia.Controls.MenuItem();
                    quickFixedConstraint.Header = "Quick Fixed Constraint";
                    quickFixedConstraint.Click += async (s, e) =>
                    {
                        bool selectedValid = true;

                        List<EntityReference> properSelections = new List<EntityReference>();

                        foreach (var selection in Toolbelt.SelectedObjects)
                        {
                            EntityReference source = selection switch
                            {
                                EntityMoveable entityMoveable => MapTools.Entities[entityMoveable.entity],
                                BrushMoveable brushMoveable => MapTools.GetOwningEntity(brushMoveable.brush),
                                FaceMoveable faceMoveable => MapTools.GetOwningEntity(faceMoveable.brush),
                                _ => null
                            };

                            if (source == null) continue;

                            var name = source.Name;
                            if (string.IsNullOrEmpty(name)) { selectedValid = false; break; }
                            if (MapTools.Entities.Any(e => e.Name == name && e != source)) { selectedValid = false; break; }

                            properSelections.Add(source);
                        }

                        if (!selectedValid)
                        {
                            var box = MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                            {
                                ButtonDefinitions = new List<ButtonDefinition>
                                {
                                    new ButtonDefinition
                                    {
                                        Name = "Ok"
                                    },
                                },
                                ContentTitle = "Cannot Create Constraint",
                                ContentMessage = $"Make sure you have entities/brush entities selected, and that their target names are unique.",
                                SystemDecorations = Avalonia.Controls.SystemDecorations.BorderOnly,
                                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
                                CloseOnClickAway = true
                            });
                            await box.ShowAsPopupAsync(MainWindow.Instance);
                            return;
                        }

                        if (properSelections.Count != 2)
                        {
                            var box = MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
                            {
                                ButtonDefinitions = new List<ButtonDefinition>
                                {
                                    new ButtonDefinition
                                    {
                                        Name = "Ok"
                                    },
                                },
                                ContentTitle = "Cannot Create Constraint",
                                ContentMessage = $"Select 2 entites.",
                                SystemDecorations = Avalonia.Controls.SystemDecorations.BorderOnly,
                                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
                                CloseOnClickAway = true
                            });
                            await box.ShowAsPopupAsync(MainWindow.Instance);
                            return;
                        }

                    };
                    menu.Items.Add(quickFixedConstraint);

                    menu.Closed += (s, e) => waitMenu = false;

                    menu.Open(MainWindow.Instance);
                });
            }
            else
            {
                waitMenu = false;
            }
        }

        static bool IsSameTarget(Transformable a, Transformable b)
        {
            if (a is FaceMoveable fa && b is FaceMoveable fb) return fa.brush == fb.brush && fa.face == fb.face;
            if (a is BrushMoveable ba && b is BrushMoveable bb) return ba.brush == bb.brush;
            if (a is BrushMoveable bc && b is FaceMoveable ff) return bc.brush == ff.brush;
            if (a is FaceMoveable fc && b is BrushMoveable bf) return fc.brush == bf.brush; 
            if (a is EntityMoveable ta && b is EntityMoveable tb) return ta.entity == tb.entity;
            if (a is TerrainMoveable tta && b is TerrainMoveable ttb) return tta.terrain == ttb.terrain;
            if (a is BrushEdgeMoveable ea && b is BrushEdgeMoveable eb)
            {
                if (ea.brush != eb.brush) return false;
                var brush = MapTools.Brushes[ea.brush];
                return Vector3.DistanceSquared(brush.Vertices[ea.vertA], brush.Vertices[eb.vertA]) < 0.01f
                    && Vector3.DistanceSquared(brush.Vertices[ea.vertB], brush.Vertices[eb.vertB]) < 0.01f;
            }
            return false;
        }

        void HandlePaintSelect()
        {
            if (Toolbelt.HighlightedObject is null) return;

            if (!Toolbelt.SelectedObjects.Any(o => IsSameTarget(o, Toolbelt.HighlightedObject)))
            {
                Toolbelt.SelectedObjects.Add(Toolbelt.HighlightedObject);
                GizmoRotate.ResetPivot();

                if (Toolbelt.HighlightedObject is TerrainMoveable paintTerrain)
                {
                    int srcBrush = paintTerrain.terrain >= 0 && paintTerrain.terrain < MapTools.Terrains.Length
                        ? MapTools.Terrains[paintTerrain.terrain].BrushSource : -1;

                    if (srcBrush >= 0 && srcBrush < MapTools.Brushes.Length &&
                        !Toolbelt.SelectedObjects.Any(o => o is BrushMoveable bm && bm.brush == srcBrush))
                    {
                        Toolbelt.SelectedObjects.Add(new BrushMoveable(srcBrush));
                    }
                }
            }

            TextureSettingsWindow.Instance?.UpdateValues();
        }

        void HandleSelection()
        {
            if (Toolbelt.HighlightedObject is null) return;

            if (!KeyboardManager.IsDown(Key.LeftCtrl)) Toolbelt.SelectedObjects.Clear();

            BrushMoveable hitBrush = Toolbelt.HighlightedObject as BrushMoveable;
            EntityMoveable hitEnt = Toolbelt.HighlightedObject as EntityMoveable;
            HintMoveable hitHint = Toolbelt.HighlightedObject as HintMoveable;
            FaceMoveable hitFace = Toolbelt.HighlightedObject as FaceMoveable;

            if (Toolbelt.TreatBrushEntitiesAsGroups && (hitFace != null || hitBrush != null))
            {
                int bID = 0;
                if (hitBrush != null) bID = hitBrush.brush;
                if (hitFace != null) bID = hitFace.brush;

                var owner = MapTools.GetOwningEntity(bID);

                if (owner != null)
                {
                    var groupIndices = owner != null ? owner.BrushIndices : new List<int> { bID };

                    bool anySelected = Toolbelt.SelectedObjects.Any(o => o is BrushMoveable bm && groupIndices.Contains(bm.brush));

                    if (anySelected)
                    {
                        if (!MouseManager.IsDown(MouseButton.Right))
                            Toolbelt.SelectedObjects.RemoveAll(o => o is BrushMoveable bm && groupIndices.Contains(bm.brush));
                    }
                    else
                    {
                        foreach (var idx in groupIndices)
                            Toolbelt.SelectedObjects.Add(new BrushMoveable(idx));
                    }

                    TextureSettingsWindow.Instance?.UpdateValues();
                    return;
                }
            }
            if (Toolbelt.HighlightedObject is TerrainMoveable hitTerrain)
            {
                int srcBrush = hitTerrain.terrain >= 0 && hitTerrain.terrain < MapTools.Terrains.Length
                    ? MapTools.Terrains[hitTerrain.terrain].BrushSource : -1;

                var foundTerrain = Toolbelt.SelectedObjects.Find(t => IsSameTarget(t, hitTerrain));

                if (foundTerrain != null)
                {
                    if (!MouseManager.IsDown(MouseButton.Right))
                    {
                        Toolbelt.SelectedObjects.Remove(foundTerrain);
                        if (srcBrush >= 0)
                            Toolbelt.SelectedObjects.RemoveAll(o => o is BrushMoveable bm && bm.brush == srcBrush);
                    }
                }
                else
                {
                    Toolbelt.SelectedObjects.Add(hitTerrain);
                    if (srcBrush >= 0 && srcBrush < MapTools.Brushes.Length &&
                        !Toolbelt.SelectedObjects.Any(o => o is BrushMoveable bm && bm.brush == srcBrush))
                    {
                        Toolbelt.SelectedObjects.Add(new BrushMoveable(srcBrush));
                    }
                    GizmoRotate.ResetPivot();
                }

                TextureSettingsWindow.Instance?.UpdateValues();
                return;
            }

            var group = MapTools.GetOwningGroup(Toolbelt.HighlightedObject.GetGuid());
            if (group != null && Toolbelt.EnableGroups)
            {
                Toolbelt.SelectedObjects.RemoveAll(t => IsSameTarget(t, Toolbelt.HighlightedObject));

                Toolbelt.SelectedObjects.AddRange(Toolbelt.GetFromGroup(group));
                return;
            }

            Transformable found = Toolbelt.SelectedObjects.Find(t => IsSameTarget(t, Toolbelt.HighlightedObject))!;

            if (found != null) { if (!MouseManager.IsDown(MouseButton.Right)) Toolbelt.SelectedObjects.Remove(found); }
            else
            {
                Toolbelt.SelectedObjects.Add(Toolbelt.HighlightedObject);
                GizmoRotate.ResetPivot();
            }

            TextureSettingsWindow.Instance?.UpdateValues();
        }

        void HandleFaceShifting()
        {
            var shiftKey = Toolbelt.FlipScrollShift ? Key.LeftAlt : Key.LeftShift;

            if (KeyboardManager.IsPressed(shiftKey))
            {
                foreach (var obj in Toolbelt.SelectedObjects)
                    if (obj is FaceMoveable face) face.PrepareMove();
                movedAny = false;
            }
            if (KeyboardManager.IsDown(shiftKey))
            {
                int wheel = float.Sign(MouseManager.ScrollDelta);
                foreach (var obj in Toolbelt.SelectedObjects)
                {
                    if (obj is not FaceMoveable face) continue;
                    var realFace = MapTools.Brushes[face.brush].Faces[face.face];
                    if (wheel != 0) movedAny = true;
                    face.Move(Vector3.Normalize(realFace.Normal) * Transformable.GridSize * wheel);

                    //if(EditorPrefs.EnableSFX)
                    //{
                    //    if (wheel > 0)
                    //    {
                    //        SoundDevice.Device.PlaySound(Path.Combine("Assets", "SFX", "click.ogg"), Vector3.Zero, disable3D: true, gain: 0.3f);
                    //    }
                    //    if (wheel < 0)
                    //    {
                    //        SoundDevice.Device.PlaySound(Path.Combine("Assets", "SFX", "click.ogg"), Vector3.Zero, disable3D: true, pitch: 0.8f, gain: 0.3f);
                    //    }
                    //}
                }
            }
            if (KeyboardManager.IsReleased(shiftKey) && movedAny)
            {
                Toolbelt.CreateUndoStateForSelectObjects(Toolbelt.SelectedObjects.Where(t => t is FaceMoveable).ToList());
                movedAny = false;
            }
        }
        void HandleFaceExtruding()
        {
            var extrudeKey = Toolbelt.FlipScrollShift ? Key.LeftShift : Key.LeftAlt;

            if (KeyboardManager.IsPressed(extrudeKey))
            {
                altDragFakeBrushes = new List<(Brush brush, int face)>();
                altDragOriginalPlanes = new List<Plane>();
                foreach (var obj in Toolbelt.SelectedObjects)
                {
                    if (obj is FaceMoveable face)
                    {
                        altDragFakeBrushes.Add((BrushOperations.DuplicateBrush(MapTools.Brushes[face.brush], false), face.face));
                        altDragOriginalPlanes.Add(MapTools.Brushes[face.brush].Faces[face.face].Plane.Value);
                    }
                }
                movedAny = false;
                isAltDragging = true;
                totalScrollDelta = 0f;

                altDragMoveDirection = altDragFakeBrushes.Count > 0
                    ? altDragFakeBrushes[0].brush.Faces[altDragFakeBrushes[0].face].Normal
                    : Vector3.Up;
            }
            if (KeyboardManager.IsDown(extrudeKey) && isAltDragging)
            {
                int wheel = float.Sign(MouseManager.ScrollDelta);
                totalScrollDelta += Transformable.GridSize * wheel;
                if (wheel != 0)
                {
                    movedAny = true;
                    for (int i = 0; i < altDragFakeBrushes.Count; i++)
                    {
                        var (brush, face) = altDragFakeBrushes[i];

                        brush.Faces[face].Plane = altDragOriginalPlanes[i];
                        BrushOperations.RebuildBrush(ref brush);
                        BrushOperations.MoveFace(ref brush, face, totalScrollDelta);
                        altDragFakeBrushes[i] = (brush, face);
                    }
                }
            }
            if (!KeyboardManager.IsDown(extrudeKey) && isAltDragging)
            {
                isAltDragging = false;
                if (movedAny)
                {
                    var brushSnapshot = new AllBrushSnapshot(MapTools.Brushes);
                    var entitySnapshot = new AllEntitySnapshot(MapTools.Entities);

                    var sourceFaces = Toolbelt.SelectedObjects.OfType<FaceMoveable>().ToList();

                    // Faces dragged INTO the brush behave like clipping the source brush itself;
                    // faces dragged OUTWARD add a brand new brush and leave the source untouched.
                    var slicingSplits = new List<CSGUtils.SplitResult>();
                    var extrudeJobs = new List<(Brush fakeBrush, int faceIndex, Plane originalPlane)>();

                    for (int i = 0; i < altDragFakeBrushes.Count; i++)
                    {
                        var (fakeBrush, faceIndex) = altDragFakeBrushes[i];
                        var originalPlane = altDragOriginalPlanes[i];
                        var currentPlane = fakeBrush.Faces[faceIndex].Plane.Value;
                        bool isSlicing = currentPlane.D > originalPlane.D;

                        var slicePlane = new Plane(currentPlane.Normal, currentPlane.D - Vector3.Dot(currentPlane.Normal, fakeBrush.Position));
                        var originalSlicePlane = new Plane(originalPlane.Normal, originalPlane.D - Vector3.Dot(originalPlane.Normal, fakeBrush.Position));

                        var sourceFace = sourceFaces.ElementAtOrDefault(i);
                        if (sourceFace == null) continue;

                        if (isSlicing)
                            slicingSplits.AddRange(CSGUtils.ComputeSplits(new[] { sourceFace.brush }, slicePlane, faceIndex));
                        else
                            extrudeJobs.Add((fakeBrush, faceIndex, originalSlicePlane));
                    }

                    var addedIndices = CSGUtils.CommitSplits(slicingSplits);

                    foreach (var (fakeBrush, faceIndex, originalPlane) in extrudeJobs)
                    {
                        var newIdx = CSGUtils.AddExtrudedBrush(fakeBrush, faceIndex, originalPlane);
                        if (newIdx.HasValue) addedIndices.Add(newIdx.Value);
                    }

                    MapTools.SyncBrushOwnership();
                    Toolbelt.SelectedObjects.Clear();
                    foreach (var idx in addedIndices)
                        Toolbelt.SelectedObjects.Add(new BrushMoveable(idx));

                    Toolbelt.UndoManager.DoOnUndo(() =>
                    {
                        brushSnapshot.Restore();
                        entitySnapshot.Restore();

                        MapTools.RecomputeAllBrushBounds();
                        MapTools.SyncBrushOwnership();
                    });
                }
                movedAny = false;
            }
        }
        void HandleCopyPaste()
        {
            if (!KeyboardManager.IsDown(Key.LeftCtrl)) return;

            if (KeyboardManager.IsPressed(Key.C))
            {
                MapClipboard.Copy(Toolbelt.SelectedObjects);
                return;
            }

            if (!KeyboardManager.IsPressed(Key.V) || !MapClipboard.HasContent) return;

            Vector3 offset = IsUsedIn2D ? GetPasteOffset2D() : GetPasteOffset3D();

            var brushSnapshot = new BrushWithOwnershipSnapshot(MapTools.Brushes, MapTools.Entities);
            var terrainSnapshot = new AllTerrainSnapshot(MapTools.Terrains, MapTools.Brushes);
            var hintSnapshot = new AllHintSnapshot(MapTools.Hints);

            var created = MapClipboard.Paste(offset);

            Toolbelt.SelectedObjects = created;
            GizmoRotate.ResetPivot();
            TextureSettingsWindow.Instance?.UpdateValues();

            Toolbelt.UndoManager.DoOnUndo(() =>
            {
                hintSnapshot.Restore();
                terrainSnapshot.Restore();
                brushSnapshot.Restore();
                Toolbelt.SelectedObjects.RemoveAll(o => created.Contains(o));
            });
        }
        Vector3 GetPasteOffset2D()
        {
            var vp = ActiveViewport;
            return MapClipboard.OffsetFor2D(LocalToWorld(vp, MouseLocalF), vp.RightAxis, vp.UpAxis);
        }
        Vector3 GetPasteOffset3D()
        {
            var ray = SceneRay;
            float bestDist = float.MaxValue;
            Vector3 normal = Vector3.Zero;

            var mapHit = MapTools.RaycastMapGeometry(ray, true, true);
            if (mapHit.distance > 0f && mapHit.brush != -1 && mapHit.face != -1)
            {
                bestDist = mapHit.distance;
                normal = Vector3.Normalize(MapTools.Brushes[mapHit.brush].Faces[mapHit.face].Normal);
            }

            var terrainHit = MapTools.RaycastTerrains(ray);
            if (terrainHit.distance > 0f && terrainHit.terrain != -1 && terrainHit.distance < bestDist)
            {
                bestDist = terrainHit.distance;
                normal = Vector3.Up;
            }

            if (bestDist == float.MaxValue)
            {
                var size = MapClipboard.Bounds.Max - MapClipboard.Bounds.Min;
                return MapClipboard.OffsetFor3D(ray.Position + ray.Direction * MathF.Max(8f, size.Length() * 1.5f), Vector3.Zero);
            }

            return MapClipboard.OffsetFor3D(ray.Position + ray.Direction * bestDist, normal);
        }
        void HandleTextureApplication()
        {
            bool leftPressed = MouseManager.IsPressed(MouseButton.Left);
            bool altDown = KeyboardManager.IsDown(Key.LeftAlt);
            bool ctrlDown = KeyboardManager.IsDown(Key.LeftCtrl);

            if (altDown && ctrlDown && leftPressed)
            {
                if (Toolbelt.HighlightedObject is FaceMoveable tf && tf.brush != -1 && tf.face != -1
                    && TextureClipboard.HasSource)
                {
                    SnapshotFaceForUndo(tf.brush, tf.face, out var restore);

                    TextureSeamSolver.Solve(
                        srcBrush: MapTools.Brushes[TextureClipboard.SourceBrush],
                        srcFaceIdx: TextureClipboard.SourceFace,
                        dstBrush: ref MapTools.Brushes[tf.brush],
                        dstFaceIdx: tf.face);

                    BrushOperations.RebuildBrush(ref MapTools.Brushes[tf.brush]);

                    TextureClipboard.LiftFromFace(tf.brush, tf.face);

                    Toolbelt.UndoManager.DoOnUndo(restore);
                }
                return;
            }

            if (ctrlDown && KeyboardManager.IsPressed(Key.A))
            {
                if (Toolbelt.HighlightedObject is FaceMoveable f)
                {
                    SnapshotFaceForUndo(f.brush, f.face, out var restore);

                    MapTools.Brushes[f.brush].Faces[f.face].MaterialName =
                        Toolbelt.ActiveTexture;
                    MapTools.Brushes[f.brush].Faces[f.face].Surface =
                        GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture];
                    BrushOperations.RebuildBrush(ref MapTools.Brushes[f.brush]);

                    Toolbelt.UndoManager.DoOnUndo(restore);
                }
                else if (Toolbelt.HighlightedObject is BrushMoveable b)
                {
                    var restores = new System.Action[MapTools.Brushes[b.brush].Faces.Length];
                    for (int j = 0; j < MapTools.Brushes[b.brush].Faces.Length; j++)
                    {
                        SnapshotFaceForUndo(b.brush, j, out restores[j]);
                        MapTools.Brushes[b.brush].Faces[j].MaterialName =
                            Toolbelt.ActiveTexture;
                        MapTools.Brushes[b.brush].Faces[j].Surface =
                            GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture];
                    }
                    BrushOperations.RebuildBrush(ref MapTools.Brushes[b.brush]);
                    Toolbelt.UndoManager.DoOnUndo(() => { foreach (var r in restores) r(); });
                }
            }
        }

        void SnapshotFaceForUndo(int bi, int fi, out System.Action restore)
        {
            var face = MapTools.Brushes[bi].Faces[fi];
            string n = face.MaterialName;
            int s = face.Surface;
            float ox = face.TOffX, oy = face.TOffY;
            float sx = face.TScaleX, sy = face.TScaleY;
            float r = face.UvRotation;
            float lx = face.LuxelScale;
            var pm = face.UvProjectionMode;

            restore = () => {
                MapTools.Brushes[bi].Faces[fi].MaterialName = n;
                MapTools.Brushes[bi].Faces[fi].Surface = s;
                MapTools.Brushes[bi].Faces[fi].TOffX = ox;
                MapTools.Brushes[bi].Faces[fi].TOffY = oy;
                MapTools.Brushes[bi].Faces[fi].TScaleX = sx;
                MapTools.Brushes[bi].Faces[fi].TScaleY = sy;
                MapTools.Brushes[bi].Faces[fi].UvRotation = r;
                MapTools.Brushes[bi].Faces[fi].LuxelScale = lx;
                MapTools.Brushes[bi].Faces[fi].UvProjectionMode = pm;
                BrushOperations.RebuildBrush(ref MapTools.Brushes[bi]);
            };
        }
        void HandleDelete()
        {
            if (!KeyboardManager.IsPressed(Key.Delete)) return;
            var objs = Toolbelt.SelectedObjects.ToList();
            var snaps = objs.Select(o => o.SnapshotForUndo()).ToList();
            foreach (var obj in objs) obj.Delete();
            Toolbelt.SelectedObjects.Clear();
            MapTools.FinalizeDeletedObjects();
            Toolbelt.UndoManager.DoOnUndo(() => { foreach (var s in snaps) s.Restore(); });
        }

        void HandleDuplicate()
        {
            if (!KeyboardManager.IsPressed(Key.D) || !KeyboardManager.IsDown(Key.LeftCtrl)) return;

            Toolbelt.DuplicateSelectedObjects();
        }

        void HandleBrushEntityConvert()
        {
            if (!KeyboardManager.IsPressed(Key.T) || !KeyboardManager.IsDown(Key.LeftCtrl)) return;

            var ids = Toolbelt.SelectedObjects
                .Select(o => o is FaceMoveable f ? f.brush : o is BrushMoveable bm ? bm.brush : -1)
                .Where(id => id >= 0).Distinct().ToList();

            if (ids.Count == 0) return;

            bool anyAlreadyOwned = ids.Any(id => MapTools.GetOwningEntity(id) != null);

            if (anyAlreadyOwned)
            {
                // Ungroup: detach every selected brush from whatever entity currently owns it.
                foreach (var id in ids)
                {
                    var owner = MapTools.GetOwningEntity(id);
                    MapTools.RemoveBrushFromEntity(owner, id);
                }
                MapTools.SyncBrushOwnership();
                return;
            }
            BoundingBox? unionBounds = null;

            foreach (var bi in ids)
            {
                if (bi < 0 || bi >= MapTools.BrushBounds.Length) continue;
                unionBounds = unionBounds.HasValue
                    ? BoundingBox.CreateMerged(unionBounds.Value, MapTools.BrushBounds[bi])
                    : MapTools.BrushBounds[bi];
            }

            // Group: one new entity owning every selected brush, not one entity per brush.
            var entity = new EntityReference
            {
                EntityName = "FuncDetail",
                Properties = Array.Empty<EntityProperty>(),
                BrushIndices = new List<int>(ids),
                brushOwnerGUIDs = MapTools.GuidsForBrushIndices(ids),
                Position = (unionBounds ?? default).Min,
            };

            var eid = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == "FuncDetail");
            if (eid != -1)
            {
                var src = GlobalEditorData.EditorOverrides.overrides[eid].defaultProperties;
                entity.Properties = new EntityProperty[src.Length];
                Array.Copy(src, entity.Properties, src.Length);
            }

            MapTools.AddEntity(entity);
            MapTools.SyncBrushOwnership();

            MapperView.OpenEntityInspector(new List<EntityReference> { entity });
        }

        void HandleMergeFaces()
        {
            if (!KeyboardManager.IsDown(Key.LeftCtrl) || !KeyboardManager.IsPressed(Key.F)) return;

            var faces = Toolbelt.SelectedObjects.Where(o => o is FaceMoveable)
                            .Select(o => o is FaceMoveable f ? (f.brush, f.face) : (-1, -1))
                            .Where(t => t.Item1 != -1 && t.Item1 != -1).ToArray();

            if (faces.Length != 2) return;

            var brush = BrushOperations.CreateHullFromFaces(faces[0].Item1, faces[0].Item2, faces[1].Item1, faces[1].Item2);
            if (brush.HasValue)
            {
                var oldBrushes = MapTools.Brushes;
                MapTools.AddBrush(brush.Value);
                Toolbelt.UndoManager.DoOnUndo(() =>
                {
                    MapTools.ActiveMap.Brushes = oldBrushes;
                    MapTools.RecomputeAllBrushBounds();
                });
            }
        }

        void HandleFaceSelectionForActiveBrush()
        {

        }

        void BeginDrag(EditorViewport vp, Vector3 worldPt)
        {
            if (KeyboardManager.IsDown(Key.LeftShift))
                Toolbelt.DuplicateSelectedObjects();

            drag = true;
            drag2DViewport = vp;
            dragStartWorld = worldPt;
            drag2DTargets = Toolbelt.GetTransformablesFor2D().ToList();

            selectionAtDragStart = Vector3.Zero;
            foreach (var sel in drag2DTargets) selectionAtDragStart += sel.GetPosition();
            selectionAtDragStart /= drag2DTargets.Count;
        }
    }
}
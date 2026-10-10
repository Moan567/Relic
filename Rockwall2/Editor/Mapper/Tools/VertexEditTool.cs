using Avalonia.Input;
using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
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
    public class VertexEditTool : Tool
    {
        const float PosEps = 1e-4f;
        const float Pick3D = 8f;
        const float Pick2D = 7f;
        const float Proj2DEps = 0.6f;

        struct Vert { public int brush; public Vector3 pos; }

        List<int> editingBrushes = new();
        List<Transformable> savedSelection = new();
        readonly List<Vert> selection = new();

        Vert? hoveredVert;
        (Vert a, Vert b)? hoveredEdge;

        bool wasGizmoDragging;

        bool drag2D;
        EditorViewport drag2DViewport;
        Vector3 drag2DFloating;

        bool boxSelecting;
        EditorViewport boxViewport;
        Vector3 boxStart, boxCurrent;

        Action dragRestore;
        bool dragMoved;

        Action CaptureBrushState()
        {
            var saved = editingBrushes
                .Select(bi => (bi,
                               verts: (Vector3[])MapTools.Brushes[bi].Vertices.Clone(),
                               faces: (Face[])MapTools.Brushes[bi].Faces.Clone()))
                .ToList();

            return () =>
            {
                foreach (var s in saved)
                {
                    ref var b = ref MapTools.Brushes[s.bi];
                    b.Vertices = (Vector3[])s.verts.Clone();
                    b.Faces = (Face[])s.faces.Clone();
                    BrushOperations.RebuildBrush(ref b);
                    MapTools.RecomputeBrushBounds(s.bi);
                }
            };
        }

        public override void OnSelected()
        {
            savedSelection = new List<Transformable>(Toolbelt.SelectedObjects);
            editingBrushes = savedSelection
                .Select(SelectionTool.ResolveBrushForEditing)
                .Where(id => id >= 0)
                .Distinct()
                .ToList();

            selection.Clear();
            GizmoRotate.ResetPivot();
        }

        public override void OnDeselected()
        {
            drag2D = false;
            boxSelecting = false;
            Toolbelt.SelectedObjects.Clear();
            Toolbelt.SelectedObjects.AddRange(savedSelection);
        }

        public override void OnUpdate(float delta)
        {
            if (editingBrushes.Count == 0) return;
            if (IsUsedIn2D) Update2D();
            else Update3D();
        }

        public override void OnRender(float delta)
        {
            if (editingBrushes.Count == 0) return;
            if (IsRenderingIn2D) Render2D(RenderingViewport);
            else Render3D();
        }

        IEnumerable<Vert> AllVerts()
        {
            foreach (var bi in editingBrushes)
            {
                var brush = MapTools.Brushes[bi];
                var seen = new List<Vector3>();
                foreach (var v in brush.Vertices)
                {
                    if (seen.Any(s => Vector3.DistanceSquared(s, v) < PosEps)) continue;
                    seen.Add(v);
                    yield return new Vert { brush = bi, pos = v };
                }
            }
        }

        IEnumerable<(int brush, Vector3 a, Vector3 b)> AllEdges()
        {
            foreach (var bi in editingBrushes)
            {
                var brush = MapTools.Brushes[bi];
                foreach (var (iA, iB) in OtherMath.GetUniqueEdges(bi))
                {
                    if (Vector3.Distance(brush.Vertices[iA], brush.Vertices[iB]) < 0.1f) continue;
                    yield return (bi, brush.Vertices[iA], brush.Vertices[iB]);
                }
            }
        }

        static bool SamePos(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) < PosEps;
        static bool SameVert(Vert a, Vert b) => a.brush == b.brush && SamePos(a.pos, b.pos);

        bool IsSelected(Vert v) => selection.Any(s => SameVert(s, v));

        static Vector3 World(Vert v) => v.pos + MapTools.Brushes[v.brush].Position;

        void SelectVert(Vert v)
        {
            var world = World(v);
            foreach (var candidate in AllVerts())
                if (SamePos(World(candidate), world) && !IsSelected(candidate))
                    selection.Add(candidate);
        }

        void ToggleVert(Vert v, bool additive)
        {
            if (!additive) selection.Clear();
            if (IsSelected(v)) selection.RemoveAll(s => SameVert(s, v));
            else SelectVert(v);
        }

        List<BrushVertexMoveable> ResolveHandles(IEnumerable<Vert> verts)
        {
            var result = new List<BrushVertexMoveable>();
            foreach (var v in verts)
            {
                var brush = MapTools.Brushes[v.brush];
                int i = Array.FindIndex(brush.Vertices, p => SamePos(p, v.pos));
                if (i != -1) result.Add(new BrushVertexMoveable { brush = v.brush, vert = i });
            }
            return result;
        }

        void ApplyDeltaToSelection(Vector3 delta)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                var v = selection[i];
                v.pos += delta;
                selection[i] = v;
            }
        }

        static void RebuildAffected(IEnumerable<int> brushes)
        {
            foreach (var bi in brushes.Distinct())
            {
                ref var brush = ref MapTools.Brushes[bi];
                BrushOperations.RecalculateBrushPlanes(ref brush);
                BrushOperations.RebuildBrush(ref brush);
                MapTools.RecomputeBrushBounds(bi);
            }
        }

        void Update3D()
        {
            bool isDragging = GizmoTranslate.IsDragging;
            if (!isDragging) SyncGizmoTargets();

            if (isDragging && !wasGizmoDragging)
            {
                dragRestore = CaptureBrushState();
                foreach (var obj in Toolbelt.SelectedObjects) obj.PrepareMove();
            }

            if (!isDragging && wasGizmoDragging)
            {
                foreach (var obj in Toolbelt.SelectedObjects) obj.PostMove();
                if (dragRestore != null) Toolbelt.UndoManager.DoOnUndo(dragRestore);
                dragRestore = null;

                // We don't know the exact delta the gizmo applied, so pull the final
                // positions straight from the brushes before anything reorders them.
                selection.Clear();
                foreach (var obj in Toolbelt.SelectedObjects.OfType<BrushVertexMoveable>())
                    selection.Add(new Vert { brush = obj.brush, pos = MapTools.Brushes[obj.brush].Vertices[obj.vert] });
            }
            wasGizmoDragging = isDragging;

            GizmoTranslate.Update();
            if (GizmoTranslate.IsDragging) return;

            PickIn3D();

            if (MouseManager.IsPressed(MouseButton.Left))
            {
                bool additive = KeyboardManager.IsDown(Key.LeftCtrl);
                if (hoveredVert.HasValue) ToggleVert(hoveredVert.Value, additive);
                else if (hoveredEdge.HasValue) ToggleEdgeAsVerts(hoveredEdge.Value, additive);
                else if (!additive) selection.Clear();
                SyncGizmoTargets();
            }
        }

        void SyncGizmoTargets()
        {
            Toolbelt.SelectedObjects.Clear();
            Toolbelt.SelectedObjects.AddRange(ResolveHandles(selection));
        }

        void ToggleEdgeAsVerts((Vert a, Vert b) edge, bool additive)
        {
            bool bothSelected = IsSelected(edge.a) && IsSelected(edge.b);
            if (!additive) selection.Clear();

            if (bothSelected)
            {
                selection.RemoveAll(s => SameVert(s, edge.a) || SameVert(s, edge.b));
            }
            else
            {
                if (!IsSelected(edge.a)) SelectVert(edge.a);
                if (!IsSelected(edge.b)) SelectVert(edge.b);
            }
        }

        void PickIn3D()
        {
            hoveredVert = null;
            hoveredEdge = null;

            var vp = ViewportManager.Perspective;
            var mouse = MouseLocalF;
            float best = Pick3D;

            foreach (var v in AllVerts())
            {
                var proj = graphicsDevice.Viewport.Project(World(v), vp.ProjectionMatrix, vp.ViewMatrix, vp.WorldMatrix);
                if (proj.Z < 0 || proj.Z > 1) continue;

                float d = Vector2.Distance(new Vector2(proj.X, proj.Y), mouse);
                if (d < best) { best = d; hoveredVert = v; }
            }

            if (hoveredVert.HasValue) return; // vertex nubs win over edge nubs

            best = Pick3D;
            foreach (var (bi, a, b) in AllEdges())
            {
                var wa = a + MapTools.Brushes[bi].Position;
                var wb = b + MapTools.Brushes[bi].Position;

                if (!OtherMath.ClipSegmentToNearPlane(wa, wb, vp.ViewMatrix, vp.ProjectionMatrix, out var ca, out var cb)) continue;

                var pa = graphicsDevice.Viewport.Project(ca, vp.ProjectionMatrix, vp.ViewMatrix, vp.WorldMatrix);
                var pb = graphicsDevice.Viewport.Project(cb, vp.ProjectionMatrix, vp.ViewMatrix, vp.WorldMatrix);
                var mid = (new Vector2(pa.X, pa.Y) + new Vector2(pb.X, pb.Y)) / 2f;

                float d = Vector2.Distance(mid, mouse);
                if (d < best) { best = d; hoveredEdge = (new Vert { brush = bi, pos = a }, new Vert { brush = bi, pos = b }); }
            }
        }

        void Render3D()
        {
            var vp = ViewportManager.Perspective;
            var mouse = MouseLocalF;

            var oldDepth = graphicsDevice.DepthStencilState;
            graphicsDevice.DepthStencilState = DepthStencilState.None;
            spriteBatch.Begin(blendState: BlendState.NonPremultiplied);

            foreach (var (bi, a, b) in AllEdges())
            {
                bool sel = IsSelected(new Vert { brush = bi, pos = a }) && IsSelected(new Vert { brush = bi, pos = b });
                bool hi = hoveredEdge.HasValue && hoveredEdge.Value.a.brush == bi &&
                          SamePos(hoveredEdge.Value.a.pos, a) && SamePos(hoveredEdge.Value.b.pos, b);

                var wa = a + MapTools.Brushes[bi].Position;
                var wb = b + MapTools.Brushes[bi].Position;
                if (!OtherMath.ClipSegmentToNearPlane(wa, wb, vp.ViewMatrix, vp.ProjectionMatrix, out var ca, out var cb)) continue;

                var pa = graphicsDevice.Viewport.Project(ca, vp.ProjectionMatrix, vp.ViewMatrix, vp.WorldMatrix);
                var pb = graphicsDevice.Viewport.Project(cb, vp.ProjectionMatrix, vp.ViewMatrix, vp.WorldMatrix);
                var sa = new Vector2(pa.X, pa.Y);
                var sb = new Vector2(pb.X, pb.Y);

                Color c = sel ? Color.MonoGameOrange : hi ? Color.White : new Color(100, 100, 100);
                spriteBatch.DrawLine(sa, sb, c, sel ? 2.5f : hi ? 2f : 1f);

                var mid = (sa + sb) / 2f;
                DrawNub(mid, sel, hi);
            }

            foreach (var v in AllVerts())
            {
                var proj = graphicsDevice.Viewport.Project(World(v), vp.ProjectionMatrix, vp.ViewMatrix, vp.WorldMatrix);
                if (proj.Z < 0 || proj.Z > 1) continue;

                bool sel = IsSelected(v);
                bool hi = hoveredVert.HasValue && SameVert(hoveredVert.Value, v);
                DrawNub(new Vector2(proj.X, proj.Y), sel, hi);
            }

            spriteBatch.End();
            graphicsDevice.DepthStencilState = oldDepth;
        }

        void Update2D()
        {
            var vp = ActiveViewport;
            var mouse = MouseLocalF;

            if (drag2D) { UpdateDrag2D(vp, mouse); return; }
            if (boxSelecting) { UpdateBoxSelect(vp, mouse); return; }

            int nx = (KeyboardManager.IsPressed(Key.Right) ? 1 : 0) - (KeyboardManager.IsPressed(Key.Left) ? 1 : 0);
            int ny = (KeyboardManager.IsPressed(Key.Up) ? 1 : 0) - (KeyboardManager.IsPressed(Key.Down) ? 1 : 0);
            if ((nx != 0 || ny != 0) && selection.Count > 0) NudgeSelection(vp, nx, ny);

            PickIn2D(vp, mouse);

            if (MouseManager.IsPressed(MouseButton.Left))
            {
                bool additive = KeyboardManager.IsDown(Key.LeftCtrl);

                if (hoveredVert.HasValue)
                {
                    if (!additive && !IsSelected(hoveredVert.Value)) selection.Clear();
                    if (!IsSelected(hoveredVert.Value)) SelectVert(hoveredVert.Value);
                    BeginDrag2D(vp, LocalToWorld(vp, mouse));
                }
                else if (hoveredEdge.HasValue)
                {
                    var (a, b) = hoveredEdge.Value;
                    if (!additive && !(IsSelected(a) && IsSelected(b))) selection.Clear();
                    if (!IsSelected(a)) SelectVert(a);
                    if (!IsSelected(b)) SelectVert(b);
                    BeginDrag2D(vp, LocalToWorld(vp, mouse));
                }
                else if (!additive) selection.Clear();

                return;
            }

            if (!KeyboardManager.IsDown(Key.Space) && MouseManager.IsDragging(MouseButton.Left)
                && !hoveredVert.HasValue && !hoveredEdge.HasValue)
            {
                BeginBoxSelect(vp, mouse);
            }
        }
        void NudgeSelection(EditorViewport vp, int x, int y)
        {
            Vector3 delta = (vp.RightAxis * x + vp.UpAxis * y) * Transformable.GridSize;
            var restore = CaptureBrushState();

            var handles = ResolveHandles(selection);
            foreach (var h in handles) h.Move(delta);
            RebuildAffected(handles.Select(h => h.brush));
            ApplyDeltaToSelection(delta);

            Toolbelt.UndoManager.DoOnUndo(restore);
        }

        void PickIn2D(EditorViewport vp, Vector2 mouse)
        {
            hoveredVert = null;
            hoveredEdge = null;

            float best = Pick2D;
            Vector2 bestScreen = default;

            foreach (var v in AllVerts())
            {
                var screen = vp.WorldToLocal(World(v));
                float d = Vector2.Distance(screen, mouse);
                if (d < best) { best = d; hoveredVert = v; bestScreen = screen; }
            }

            if (hoveredVert.HasValue)
            {
                var stack = AllVerts().Where(v => Vector2.Distance(vp.WorldToLocal(World(v)), bestScreen) < Proj2DEps).ToList();
                if (stack.Count == 2)
                {
                    hoveredEdge = (stack[0], stack[1]);
                    hoveredVert = null;
                }
                return;
            }

            best = Pick2D;
            foreach (var (bi, a, b) in AllEdges())
            {
                var sa = vp.WorldToLocal(a + MapTools.Brushes[bi].Position);
                var sb = vp.WorldToLocal(b + MapTools.Brushes[bi].Position);
                var mid = (sa + sb) / 2f;

                float d = Vector2.Distance(mid, mouse);
                if (d < best) { best = d; hoveredEdge = (new Vert { brush = bi, pos = a }, new Vert { brush = bi, pos = b }); }
            }
        }

        void BeginDrag2D(EditorViewport vp, Vector3 startWorld)
        {
            drag2D = true;
            drag2DViewport = vp;
            drag2DFloating = startWorld;
            dragRestore = CaptureBrushState();
            dragMoved = false;
        }

        void UpdateDrag2D(EditorViewport vp, Vector2 mouse)
        {
            if (!MouseManager.IsDown(MouseButton.Left)) { EndDrag2D(); return; }

            MouseManager.SetCursor(MainWindow.Instance, StandardCursorType.SizeAll);

            Vector3 world = LocalToWorld(vp, mouse);
            Vector3 raw = world - drag2DFloating;
            Vector3 delta = OtherMath.SnapVector(Vector3.Dot(raw, vp.RightAxis) * vp.RightAxis
                                                + Vector3.Dot(raw, vp.UpAxis) * vp.UpAxis);
            drag2DFloating = world;
            if (delta.LengthSquared() <= 0f) return;

            dragMoved = true;

            var handles = ResolveHandles(selection);
            foreach (var h in handles) h.Move(delta);
            RebuildAffected(handles.Select(h => h.brush));
            ApplyDeltaToSelection(delta);
        }

        void EndDrag2D()
        {
            drag2D = false;
            if (dragMoved && dragRestore != null) Toolbelt.UndoManager.DoOnUndo(dragRestore);
            dragRestore = null;
        }

        void BeginBoxSelect(EditorViewport vp, Vector2 mouse)
        {
            boxSelecting = true;
            boxViewport = vp;
            boxStart = LocalToWorld(vp, mouse);
            boxCurrent = boxStart;
        }

        void UpdateBoxSelect(EditorViewport vp, Vector2 mouse)
        {
            boxCurrent = LocalToWorld(vp, mouse);
            if (!MouseManager.IsDown(MouseButton.Left)) FinishBoxSelect(vp);
        }

        void FinishBoxSelect(EditorViewport vp)
        {
            boxSelecting = false;

            var rect = SelectionTool.RectFromPoints(vp.WorldToLocal(boxStart), vp.WorldToLocal(boxCurrent));
            if (rect.Width < 3f && rect.Height < 3f) return;

            if (!KeyboardManager.IsDown(Key.LeftCtrl)) selection.Clear();

            // Every editing brush's verts are scanned directly, so a vertex shared
            // between two brushes gets picked up on both without any extra step.
            foreach (var v in AllVerts())
            {
                var local = vp.WorldToLocal(World(v));
                if (!RectContains(rect, local)) continue;
                if (!IsSelected(v)) selection.Add(v);
            }
        }

        static bool RectContains(RectangleF r, Vector2 p) =>
            p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

        void Render2D(EditorViewport vp)
        {
            spriteBatch.Begin(blendState: BlendState.NonPremultiplied);

            if (boxSelecting && boxViewport == vp)
            {
                var rect = SelectionTool.RectFromPoints(vp.WorldToLocal(boxStart), vp.WorldToLocal(boxCurrent));
                var tl = new Vector2(rect.Left, rect.Top);
                var tr = new Vector2(rect.Right, rect.Top);
                var br = new Vector2(rect.Right, rect.Bottom);
                var bl = new Vector2(rect.Left, rect.Bottom);

                RenderUtils.DrawDashedLine(spriteBatch, tl, tr, GizmoScale.DashColor, 2f, 0f);
                RenderUtils.DrawDashedLine(spriteBatch, tr, br, GizmoScale.DashColor, 2f, 0f);
                RenderUtils.DrawDashedLine(spriteBatch, br, bl, GizmoScale.DashColor, 2f, 0f);
                RenderUtils.DrawDashedLine(spriteBatch, bl, tl, GizmoScale.DashColor, 2f, 0f);
            }

            foreach (var (bi, a, b) in AllEdges())
            {
                var sa = vp.WorldToLocal(a + MapTools.Brushes[bi].Position);
                var sb = vp.WorldToLocal(b + MapTools.Brushes[bi].Position);

                bool sel = IsSelected(new Vert { brush = bi, pos = a }) && IsSelected(new Vert { brush = bi, pos = b });
                bool hi = hoveredEdge.HasValue && hoveredEdge.Value.a.brush == bi &&
                          SamePos(hoveredEdge.Value.a.pos, a) && SamePos(hoveredEdge.Value.b.pos, b);

                Color c = sel ? Color.MonoGameOrange : hi ? Color.White : new Color(100, 100, 100);
                spriteBatch.DrawLine(sa, sb, c, sel ? 2.5f : hi ? 2f : 1f);
                DrawNub((sa + sb) / 2f, sel, hi);
            }

            foreach (var v in AllVerts())
            {
                var screen = vp.WorldToLocal(World(v));
                bool sel = IsSelected(v);
                bool hi = hoveredVert.HasValue && SameVert(hoveredVert.Value, v);
                DrawNub(screen, sel, hi);
            }

            spriteBatch.End();
        }

        void DrawNub(Vector2 screen, bool selected, bool hovered)
        {
            var rect = new RectangleF(screen.X - 4, screen.Y - 4, 8, 8);
            if (selected) { spriteBatch.FillRectangle(rect, Color.White); spriteBatch.DrawRectangle(rect, Color.Gray, 1); }
            else if (hovered) spriteBatch.DrawRectangle(rect, Color.White, 1);
            else spriteBatch.DrawRectangle(rect, Color.DarkGray, 1);
        }
    }
}
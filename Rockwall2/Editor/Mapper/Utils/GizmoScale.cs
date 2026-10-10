using Avalonia.Input;
using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper.ViewportManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;
public static class GizmoScale
{
    public static bool isDragging;

    static int activeHandle;
    static Vector3 anchor;
    static bool maskRight;
    static bool maskUp;
    static float origRightDist;
    static float origUpDist;

    static Func<BoundingBox> getBounds;
    static Action<Vector3, EditorViewport, float, float> applyScale;
    static Action onBeginDrag;
    static Action onEndDrag;

    static List<(int brush, Vector3[] origVerts, Vector3 origPos)> origBrushes;
    static List<(int entity, Vector3 origPos)> origEntities;
    static Dictionary<int, List<BrushOperations.TerrainSyncEntry>> terrainSyncContexts;
    static AllEntitySnapshot undoEntitySnapshot;
    static AllTerrainSnapshot undoTerrainSnapshot;

    static Vector3 lastScaleAnchor;
    static EditorViewport lastScaleVp;
    static float lastScaleRight, lastScaleUp;

    public static int hoveredHandle = -1;
    public static readonly Color DashColor = new Color(255, 200, 60);

    static float ClampExtent(float value, float origSign, float min)
    {
        float magnitude = MathF.Max(MathF.Abs(value), min);
        return origSign >= 0f ? magnitude : -magnitude;
    }

    public static readonly StandardCursorType[] HandleCursors =
    {
        StandardCursorType.BottomLeftCorner,
        StandardCursorType.SizeNorthSouth,
        StandardCursorType.BottomRightCorner,
        StandardCursorType.SizeWestEast,
        StandardCursorType.TopRightCorner,
        StandardCursorType.SizeNorthSouth,
        StandardCursorType.TopLeftCorner,
        StandardCursorType.SizeWestEast,
    };

    static BoundingBox SelectionBounds() => Toolbelt.GetSelectionBounds();

    public static Vector3[] GetHandles(EditorViewport vp, out Vector3 depthPoint) =>
        GetHandles(vp, SelectionBounds(), out depthPoint);

    public static Vector3[] GetHandles(EditorViewport vp, BoundingBox bounds, out Vector3 depthPoint)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;

        depthPoint = center - Vector3.Dot(center, vp.RightAxis) * vp.RightAxis
                             - Vector3.Dot(center, vp.UpAxis) * vp.UpAxis;

        float rA = Vector3.Dot(bounds.Min, vp.RightAxis);
        float rB = Vector3.Dot(bounds.Max, vp.RightAxis);
        float rMin = MathF.Min(rA, rB);
        float rMax = MathF.Max(rA, rB);
        float rMid = (rMin + rMax) * 0.5f;

        float uA = Vector3.Dot(bounds.Min, vp.UpAxis);
        float uB = Vector3.Dot(bounds.Max, vp.UpAxis);
        float uMin = MathF.Min(uA, uB);
        float uMax = MathF.Max(uA, uB);
        float uMid = (uMin + uMax) * 0.5f;

        var rVals = new[] { rMin, rMid, rMax, rMax, rMax, rMid, rMin, rMin };
        var uVals = new[] { uMin, uMin, uMin, uMid, uMax, uMax, uMax, uMid };

        var handles = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            handles[i] = depthPoint + vp.RightAxis * rVals[i] + vp.UpAxis * uVals[i];
        }

        return handles;
    }

    static (bool maskR, bool maskU) MaskFor(int handle)
    {
        if (handle % 2 == 0) return (true, true);
        return handle % 4 == 1 ? (false, true) : (true, false);
    }
    public static StandardCursorType? GetActiveCursor()
    {
        if (isDragging) return HandleCursors[activeHandle];
        if (hoveredHandle != -1) return HandleCursors[hoveredHandle];
        return null;
    }

    public static void BeginDrag(int handle, EditorViewport vp) =>
        BeginDrag(handle, vp, SelectionBounds, ApplySelectionScale, BeginSelectionDrag, EndSelectionDrag);

    public static void BeginDrag(int handle, EditorViewport vp,
        Func<BoundingBox> boundsGetter,
        Action<Vector3, EditorViewport, float, float> scaleApplier,
        Action beginCallback,
        Action endCallback)
    {
        activeHandle = handle;
        isDragging = true;

        getBounds = boundsGetter;
        applyScale = scaleApplier;
        onBeginDrag = beginCallback;
        onEndDrag = endCallback;

        var handles = GetHandles(vp, getBounds(), out _);
        anchor = handles[(handle + 4) % 8];
        (maskRight, maskUp) = MaskFor(handle);

        origRightDist = Vector3.Dot(handles[handle] - anchor, vp.RightAxis);
        origUpDist = Vector3.Dot(handles[handle] - anchor, vp.UpAxis);

        onBeginDrag?.Invoke();
    }

    public static void Update(EditorViewport vp, Vector3 mouseWorld)
    {
        if (!MouseManager.IsDown(MouseButton.Left))
        {
            EndDrag();
            return;
        }

        float curRightDist = Vector3.Dot(mouseWorld - anchor, vp.RightAxis);
        float curUpDist = Vector3.Dot(mouseWorld - anchor, vp.UpAxis);

        if (maskRight) curRightDist = ClampExtent(curRightDist, origRightDist, Transformable.GridSize);
        if (maskUp) curUpDist = ClampExtent(curUpDist, origUpDist, Transformable.GridSize);

        float scaleRight = maskRight ? SafeRatio(curRightDist, origRightDist) : 1f;
        float scaleUp = maskUp ? SafeRatio(curUpDist, origUpDist) : 1f;

        applyScale?.Invoke(anchor, vp, scaleRight, scaleUp);
    }

    static void EndDrag()
    {
        isDragging = false;
        onEndDrag?.Invoke();
    }

    public static void Render(EditorViewport vp, SpriteBatch spriteBatch, float time)
    {
        if (Toolbelt.SelectedObjects.Count == 0) return;
        Render(vp, spriteBatch, time, SelectionBounds());
    }

    public static void Render(EditorViewport vp, SpriteBatch spriteBatch, float time, BoundingBox bounds)
    {
        var handles = GetHandles(vp, bounds, out _);
        var screenPts = handles.Select(h => vp.WorldToLocal(h)).ToArray();

        RenderUtils.DrawDashedLine(spriteBatch, screenPts[0], screenPts[2], DashColor, 1.5f, time);
        RenderUtils.DrawDashedLine(spriteBatch, screenPts[2], screenPts[4], DashColor, 1.5f, time);
        RenderUtils.DrawDashedLine(spriteBatch, screenPts[4], screenPts[6], DashColor, 1.5f, time);
        RenderUtils.DrawDashedLine(spriteBatch, screenPts[6], screenPts[0], DashColor, 1.5f, time);

        for (int i = 0; i < handles.Length; i++)
        {
            var rect = new RectangleF(screenPts[i].X - 4, screenPts[i].Y - 4, 8, 8);
            bool active = isDragging && i == activeHandle;
            bool hovered = i == hoveredHandle;

            if (active) { spriteBatch.FillRectangle(rect, Color.White); spriteBatch.DrawRectangle(rect, Color.Gray, 1); }
            else if (hovered) { spriteBatch.DrawRectangle(rect, Color.White, 1); }
            else { spriteBatch.DrawRectangle(rect, Color.DarkGray, 1); }
        }
    }

    public static Vector3 TransformPoint(Vector3 pivot, Vector3 world, EditorViewport vp, float scaleRight, float scaleUp)
    {
        var offset = world - pivot;
        float r = Vector3.Dot(offset, vp.RightAxis);
        float u = Vector3.Dot(offset, vp.UpAxis);
        var depth = offset - r * vp.RightAxis - u * vp.UpAxis;
        return pivot + r * scaleRight * vp.RightAxis + u * scaleUp * vp.UpAxis + depth;
    }

    static void BeginSelectionDrag()
    {
        origBrushes = new List<(int, Vector3[], Vector3)>();
        terrainSyncContexts = new Dictionary<int, List<BrushOperations.TerrainSyncEntry>>();
        foreach (var bi in Toolbelt.GetSelectedBrushIds())
        {
            if (bi == -1) continue;
            var b = MapTools.Brushes[bi];
            origBrushes.Add((bi, (Vector3[])b.Vertices.Clone(), b.Position));
            terrainSyncContexts[bi] = BrushOperations.CaptureTerrainSync(bi, Array.Empty<Vector3>());
        }

        origEntities = new List<(int, Vector3)>();
        foreach (var obj in Toolbelt.SelectedObjects.OfType<EntityMoveable>())
        {
            origEntities.Add((obj.entity, MapTools.Entities[obj.entity].Position));
        }

        undoEntitySnapshot = new AllEntitySnapshot(MapTools.Entities);
        undoTerrainSnapshot = new AllTerrainSnapshot(MapTools.Terrains, MapTools.Brushes);
    }

    static void ApplySelectionScale(Vector3 dragAnchor, EditorViewport vp, float scaleRight, float scaleUp)
    {
        lastScaleAnchor = dragAnchor;
        lastScaleVp = vp;
        lastScaleRight = scaleRight;
        lastScaleUp = scaleUp;

        foreach (var (bi, origVerts, origPos) in origBrushes)
        {
            ref var brush = ref MapTools.Brushes[bi];
            for (int v = 0; v < origVerts.Length; v++)
            {
                var world = origVerts[v] + origPos;
                brush.Vertices[v] = TransformPoint(dragAnchor, world, vp, scaleRight, scaleUp) - origPos;
            }
            BrushOperations.RecalculateBrushPlanes(ref brush);
            BrushOperations.RebuildBrush(ref brush);
            MapTools.RecomputeBrushBounds(bi);
        }

        foreach (var (ei, origPos) in origEntities)
        {
            MapTools.Entities[ei].Position = TransformPoint(dragAnchor, origPos, vp, scaleRight, scaleUp);
        }
    }

    static void EndSelectionDrag()
    {
        var entitySnap = undoEntitySnapshot;
        var terrainSnap = undoTerrainSnapshot;

        Vector3 anchor2 = lastScaleAnchor;
        EditorViewport vp2 = lastScaleVp;
        float sr2 = lastScaleRight, su2 = lastScaleUp;
        Vector3 Transform(Vector3 p) => TransformPoint(anchor2, p, vp2, sr2, su2);

        bool anyBroken = false;
        foreach (var (bi, _, _) in origBrushes)
        {
            if (terrainSyncContexts.TryGetValue(bi, out var ctx) && ctx.Count > 0)
            {
                if (!BrushOperations.ApplyTerrainSyncTransform(bi, ctx, Transform, out _))
                    anyBroken = true;
            }
        }

        if (anyBroken)
        {
            terrainSnap.Restore();
            entitySnap.Restore();
            MapTools.RecomputeAllBrushBounds();
            Toolbelt.ShowTerrainQuadError();
            return;
        }

        Toolbelt.UndoManager.DoOnUndo(() =>
        {
            terrainSnap.Restore();
            entitySnap.Restore();
            MapTools.RecomputeAllBrushBounds();
        });
    }

    static float SafeRatio(float cur, float orig) => MathF.Abs(orig) < 0.01f ? 1f : cur / orig;
}
using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

namespace Rockwall2.Tools;

public class ClippingTool : Tool
{
    PlaneGrid grid;
    Plane currentPlane;
    Vector3 worldPos;
    Vector3 worldNormal;

    // The 3 clip points
    List<Vector3> clipPoints = new List<Vector3>();
    List<Vector3> clipNormals = new List<Vector3>();

    VertexPosition[] nubs = BrushOperations.GenerateSphereVerticesDirect(Vector3.Zero, 0.1f, 4, 8).Reverse().ToArray();
    VertexPosition[] cursorNub;

    bool quickClipVertical = true;
    bool only2D = false;

    public override void OnDeselected()
    {
        clipPoints.Clear();
    }

    public override void OnRender(float delta)
    {
        if (!IsRenderingIn2D && only2D && clipPoints.Count > 0) return;
        if (!IsRenderingIn2D && IsUsedIn2D) return;

        // Draw cursor nub at hover position
        basicEffect.Alpha = 1f;
        basicEffect.DiffuseColor = Color.White.ToVector3();
        basicEffect.World = Matrix.CreateWorld(worldPos, Vector3.Forward, Vector3.Up);
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, nubs, 0, nubs.Length / 3);
        }
        basicEffect.World = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);

        // Draw placed clip points
        for (int i = 0; i < clipPoints.Count; i++)
        {
            basicEffect.DiffuseColor = Color.MonoGameOrange.ToVector3();
            basicEffect.World = Matrix.CreateWorld(clipPoints[i], Vector3.Forward, Vector3.Up);
            foreach (var pass in basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, nubs, 0, nubs.Length / 3);
            }
        }
        basicEffect.World = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);
        basicEffect.VertexColorEnabled = true;

        // Draw line between first 2 points (and to cursor if 1 point placed)
        if (clipPoints.Count >= 1)
        {
            var lineVerts = new List<VertexPositionColor>();

            if (clipPoints.Count == 1)
            {
                lineVerts.Add(new VertexPositionColor(clipPoints[0], Color.MonoGameOrange));
                lineVerts.Add(new VertexPositionColor(worldPos, Color.MonoGameOrange));
            }
            else
            {
                lineVerts.Add(new VertexPositionColor(clipPoints[0], Color.MonoGameOrange));
                lineVerts.Add(new VertexPositionColor(clipPoints[1], Color.MonoGameOrange));

                if (clipPoints.Count == 2)
                {
                    // Second line to cursor showing where plane will go
                    lineVerts.Add(new VertexPositionColor(clipPoints[1], Color.Yellow));
                    lineVerts.Add(new VertexPositionColor(worldPos, Color.Yellow));
                }
            }
            foreach (var pass in basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lineVerts.ToArray(), 0, lineVerts.Count / 2);
            }
        }
        basicEffect.VertexColorEnabled = false;

        if (IsRenderingIn2D) return;

        // Draw plane visualization once we have 3 points
        if (clipPoints.Count >= 2)
        {
            Plane p = ComputeClipPlane();
            grid.SetPlane(p);
            grid.Draw(Viewport3DCamera.viewMatrix, Viewport3DCamera.projectionMatrix, clipPoints[0]);
        }
        else
        {
            grid.Draw(Viewport3DCamera.viewMatrix, Viewport3DCamera.projectionMatrix, worldPos);
        }
    }

    public override void OnSelected()
    {
        grid ??= new PlaneGrid(EditorHost.Instance.GraphicsDevice, Transformable.GridSize, 2, new Plane(Vector3.Up, 0));
        clipPoints.Clear();
    }

    public override void OnUpdate(float delta)
    {
        grid.SetSpacing(Transformable.GridSize);
        var sceneRay = SceneRay;

        var mapHit = MapTools.RaycastMapGeometry(sceneRay);
        if (mapHit.distance > 0f && mapHit.face != -1 && mapHit.brush != -1)
        {
            worldPos = sceneRay.Position + sceneRay.Direction * mapHit.distance;
            worldPos = Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize;

            var b = MapTools.Brushes[mapHit.brush];
            var f = b.Faces[mapHit.face];
            Vector3 faceNormal = f.Normal;

            worldNormal = faceNormal;

            Vector3 absNormal = new Vector3(Math.Abs(faceNormal.X), Math.Abs(faceNormal.Y), Math.Abs(faceNormal.Z));
            Vector3 snappedNormal;
            if (absNormal.X >= absNormal.Y && absNormal.X >= absNormal.Z)
                snappedNormal = new Vector3(Math.Sign(faceNormal.X), 0, 0);
            else if (absNormal.Y >= absNormal.X && absNormal.Y >= absNormal.Z)
                snappedNormal = new Vector3(0, Math.Sign(faceNormal.Y), 0);
            else
                snappedNormal = new Vector3(0, 0, Math.Sign(faceNormal.Z));

            if (clipPoints.Count < 3)
            {
                currentPlane = new Plane(snappedNormal, -Vector3.Dot(snappedNormal, worldPos));
                grid.SetPlane(currentPlane);
            }
        }
        else
        {
            currentPlane = new Plane(Vector3.Up, 0);
            grid.SetPlane(currentPlane);
            float? dst = sceneRay.Intersects(currentPlane);
            if (dst > 0)
            {
                worldPos = sceneRay.Position + sceneRay.Direction * dst.Value;
                worldPos = Vector3.Round(worldPos / Transformable.GridSize) * Transformable.GridSize;
                worldNormal = Vector3.Up;
            }
        }

        if (MouseManager.IsPressed(MouseButton.Left))
        {
            if (clipPoints.Count == 0)
                only2D = IsUsedIn2D;

            int max = only2D ? 2 : 3;

            if (clipPoints.Count < max)
            {
                if (clipPoints.All(p => Vector3.DistanceSquared(p, worldPos) > 0.001f))
                {
                    clipPoints.Add(worldPos);
                    clipNormals.Add(worldNormal);
                }
            }
            else
            {
                clipPoints[max - 1] = worldPos;
            }
        }

        // Right click or Enter = confirm clip
        bool confirmClip = KeyboardManager.IsPressed(Key.Enter) || MouseManager.IsDown(MouseButton.Right);

        if (confirmClip && clipPoints.Count >= 2)
        {
            DoClip();
            clipPoints.Clear();
        }

        if (KeyboardManager.IsPressed(Key.R))
        {
            quickClipVertical = !quickClipVertical;
        }

        // Escape or backspace removes last point
        if (KeyboardManager.IsPressed(Key.Escape) || KeyboardManager.IsPressed(Key.Back))
        {
            if (clipPoints.Count > 0)
                clipPoints.RemoveAt(clipPoints.Count - 1);
        }
    }
    Plane ComputeClipPlane()
    {
        Vector3 a = clipPoints[0];
        Vector3 b = clipPoints[1];

        Vector3 ab = Vector3.Normalize(b - a);

        Vector3 normal;
        if (clipPoints.Count >= 3)
        {
            Vector3 c = clipPoints[2];
            Vector3 ac = c - a;
            normal = Vector3.Cross(ab, ac);

            if (normal.LengthSquared() < 0.0001f)
            {
                Vector3 fallback = quickClipVertical ? Vector3.Up : Vector3.Forward;
                normal = Vector3.Cross(ab, fallback);
            }
        }
        else
        {
            if (only2D)
            {
                normal = Vector3.Cross(ab, ViewportManager.Active.ViewAxis);
            }
            else
            {
                var avgNormal = clipNormals[0];

                Vector3 preferred = Vector3.UnitY;

                if (avgNormal.X > avgNormal.Y && avgNormal.X > avgNormal.Z)
                {
                    preferred = Vector3.UnitX;
                }
                else if (avgNormal.Y > avgNormal.Z)
                {
                    preferred = Vector3.UnitY;
                }
                else
                {
                    preferred = Vector3.UnitZ;
                }

                normal = Vector3.Cross(ab, preferred);
            }
        }

        normal.Normalize();
        return new Plane(a, normal);
    }
    void DoClip()
    {
        Plane clipPlane = ComputeClipPlane();

        var selectedBrushes = Toolbelt.SelectedObjects
            .Select(o => o is BrushMoveable bm ? bm.brush : o is FaceMoveable fm ? fm.brush :
                         o is TerrainMoveable tm && tm.terrain >= 0 && tm.terrain < MapTools.Terrains.Length ? MapTools.Terrains[tm.terrain].BrushSource : -1)
            .Where(id => id >= 0)
            .Distinct()
            .ToList();

        if (selectedBrushes.Count == 0) return;

        var splits = CSGUtils.ComputeSplits(selectedBrushes, clipPlane);
        if (splits.Count == 0) return;

        var terrainsBySource = selectedBrushes
            .SelectMany(bi => Enumerable.Range(0, MapTools.Terrains.Length).Where(ti => MapTools.Terrains[ti].BrushSource == bi))
            .Select(ti => MapTools.Terrains[ti])
            .ToList();

        // Snapshot for undo before committing anything.
        var entitySnapshot = new AllEntitySnapshot(MapTools.Entities);
        var terrainSnapshot = new AllTerrainSnapshot(MapTools.Terrains, MapTools.Brushes);

        foreach (var s in splits)
            MapTools.RemoveBrush(s.original);
        MapTools.FinalizeDeletedObjects();

        var addedIndices = new List<int>();

        foreach (var s in splits)
        {
            int? frontIdx = null, backIdx = null;
            bool frontGotTerrain = false, backGotTerrain = false;

            if (s.front.HasValue)
            {
                MapTools.AddBrush(s.front.Value);
                frontIdx = MapTools.Brushes.Length - 1;
                MapTools.RecomputeBrushBounds(frontIdx.Value);
                addedIndices.Add(frontIdx.Value);
                if (s.owner != null) MapTools.AddBrushToEntity(s.owner, frontIdx.Value);
            }
            if (s.back.HasValue)
            {
                MapTools.AddBrush(s.back.Value);
                backIdx = MapTools.Brushes.Length - 1;
                MapTools.RecomputeBrushBounds(backIdx.Value);
                addedIndices.Add(backIdx.Value);
                if (s.owner != null) MapTools.AddBrushToEntity(s.owner, backIdx.Value);
            }

            foreach (var terrain in terrainsBySource.Where(t => t.BrushSource == s.sourceBrushIndex))
            {
                if (terrain.FaceSource < 0 || terrain.FaceSource >= s.original.Faces.Length) continue;

                var oldFace = s.original.Faces[terrain.FaceSource];
                var oldFaceCorners = BrushOperations.GetUniqueFaceCorners(s.original, oldFace);
                if (oldFaceCorners.Length != 4) continue;

                var refCorners = BrushOperations.TerrainReferenceCorners(terrain);
                var canonicalOldCorners = BrushOperations.MatchCornersToReference(oldFaceCorners, refCorners);

                if (TrySplitTerrainOntoPiece(terrain, canonicalOldCorners, frontIdx)) frontGotTerrain = true;
                if (TrySplitTerrainOntoPiece(terrain, canonicalOldCorners, backIdx)) backGotTerrain = true;
            }

            if (frontIdx.HasValue && !frontGotTerrain) MapTools.Brushes[frontIdx.Value].isUsedForTerrain = false;
            if (backIdx.HasValue && !backGotTerrain) MapTools.Brushes[backIdx.Value].isUsedForTerrain = false;
        }

        Toolbelt.SelectedObjects.Clear();
        foreach (var idx in addedIndices)
            Toolbelt.SelectedObjects.Add(new BrushMoveable(idx));

        Toolbelt.UndoManager.DoOnUndo(() =>
        {
            terrainSnapshot.Restore();
            entitySnapshot.Restore();

            MapTools.RecomputeAllBrushBounds();
            MapTools.SyncBrushOwnership();
        });
    }

    bool TrySplitTerrainOntoPiece(Terrain originalTerrain, Vector3[] canonicalOldCorners, int? pieceBrushIndex)
    {
        if (pieceBrushIndex == null) return false;

        var piece = MapTools.Brushes[pieceBrushIndex.Value];

        int faceIdx = Array.FindIndex(piece.Faces, f =>
            Vector3.Dot(f.Normal, originalTerrain.SourceNormal) > 0.99f &&
            BrushOperations.GetUniqueFaceCorners(piece, f).Length == 4);

        if (faceIdx == -1) return false;

        var newTerrain = BrushOperations.ResampleTerrainOntoFace(originalTerrain, canonicalOldCorners, pieceBrushIndex.Value, faceIdx);
        if (!newTerrain.HasValue) return false;

        MapTools.AddTerrain(newTerrain.Value);
        MapTools.Brushes[pieceBrushIndex.Value].isUsedForTerrain = true;
        return true;
    }
}
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
using Avalonia.Threading;

namespace Rockwall2.Tools;

public class TextureApplicationTool : Tool
{
    public static TextureApplicationTool Instance;

    const float SewEpsilon = 0.05f;

    int sourceBrush = -1;
    int sourceFace = -1;

    int originalSourceBrush = -1;
    int originalSourceFace = -1;

    bool isPainting = false;
    bool terrainsSelected = false;
    bool isPaintingTerrain = false;

    const float WeldEps = 0.01f;
    const float WeldCell = 0.05f;

    static (int, int, int) WeldKey(Vector3 p) =>
        ((int)MathF.Floor(p.X / WeldCell), (int)MathF.Floor(p.Y / WeldCell), (int)MathF.Floor(p.Z / WeldCell));

    readonly HashSet<(int terrain, int vertex)> pinnedVertices = new();
    int hoveredPinTerrain = -1, hoveredPinVertex = -1;

    Vector3 terrainEditPoint;
    float radius = 2f, strength = 0;
    VertexPositionColor[] nubs = GizmoShapes.WireSphere(Vector3.Zero,1f,Color.White);
    VertexPositionColor[] arrow = GizmoShapes.WireArrow(Vector3.Zero, Vector3.Forward, 1f, Color.Blue);
    readonly List<(int brush, int face)> paintedThisStroke = new();
    readonly List<System.Action> undosThisStroke = new();

    public override void OnSelected()
    {
        // Force the texture window open whenever this tool is active.
        new TextureSettingsWindow().Show(MainWindow.Instance);
        Instance = this;

        SyncWindowToSource();
    }

    public override void OnDeselected()
    {
        CommitStroke();
    }
    public override void OnUpdate(float delta)
    {
        if (IsUsedIn2D) return;

        var ray = SceneRay;
        var mapHit = MapTools.RaycastMapGeometry(ray, KeyboardManager.IsDown(Key.LeftAlt));

        if (mapHit.distance > 0f && mapHit.brush != -1 && mapHit.face != -1)
        {
            Toolbelt.HighlightedObject = new FaceMoveable(mapHit.brush, mapHit.face);
        }

        var terrainHit = MapTools.RaycastTerrains(ray);

        bool terrainsAreSelected = Toolbelt.SelectedObjects.OfType<TerrainMoveable>().Any();
        bool shiftDown = KeyboardManager.IsDown(Key.LeftShift);

        bool preferTerrain = (terrainsAreSelected && !shiftDown) || isPaintingTerrain || terrainHit.distance - 0.01f < mapHit.distance || mapHit.distance < 0f;

        if (preferTerrain)
        {
            if (terrainHit.terrain != -1)
                Toolbelt.HighlightedObject = new TerrainMoveable(terrainHit.terrain);

            HandleTerrainEditing(terrainHit, ray, delta);
        }
        else
        {
            HandleFaceSelection(mapHit);
            HandlePaintStroke(mapHit);
        }
    }
    void HandleFaceSelection((int brush, int face, float distance) mapHit)
    {
        bool justPressed = MouseManager.IsPressed(MouseButton.Left);
        if (!justPressed) return;

        if (mapHit.brush == -1 || mapHit.face == -1 || mapHit.distance <= 0f) return;

        sourceBrush = mapHit.brush;
        sourceFace = mapHit.face;

        originalSourceBrush = sourceBrush;
        originalSourceFace = sourceFace;

        if (!KeyboardManager.IsDown(Key.LeftCtrl)) Toolbelt.SelectedObjects.Clear();

        if (MouseManager.IsDoubleClicked(MouseButton.Left))
        {
            var connected = FindConnectedSameMaterialFaces(sourceBrush, sourceFace);
            foreach (var f in connected)
                Toolbelt.SelectedObjects.Add(new FaceMoveable(sourceBrush, f));
        }
        else
        {
            Toolbelt.SelectedObjects.Add(new FaceMoveable(sourceBrush, sourceFace));
        }

        TextureClipboard.LiftFromFace(sourceBrush, sourceFace);

        TextureSettingsWindow.Instance?.UpdateValues();
    }

    List<int> FindConnectedSameMaterialFaces(int brushIdx, int startFace, float epsilon = 0.01f)
    {
        var brush = MapTools.Brushes[brushIdx];
        string targetMaterial = brush.Faces[startFace].MaterialName;

        var result = new List<int>();
        var visited = new HashSet<int> { startFace };
        var queue = new Queue<int>();
        queue.Enqueue(startFace);
        result.Add(startFace);

        bool FacesShareEdge(Face a, Face b)
        {
            int shared = 0;
            foreach (var ai in a.Indices.Distinct())
            {
                Vector3 av = brush.Vertices[ai];
                foreach (var bi in b.Indices.Distinct())
                {
                    if (Vector3.DistanceSquared(av, brush.Vertices[bi]) < epsilon * epsilon)
                    {
                        shared++;
                        break;
                    }
                }
                if (shared >= 2) return true;
            }
            return false;
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            var currentFace = brush.Faces[current];

            for (int f = 0; f < brush.Faces.Length; f++)
            {
                if (visited.Contains(f)) continue;
                if (brush.Faces[f].MaterialName != targetMaterial) continue;
                if (brush.Faces[f].Indices == null || brush.Faces[f].Indices.Length == 0) continue;
                if (!FacesShareEdge(currentFace, brush.Faces[f])) continue;

                visited.Add(f);
                result.Add(f);
                queue.Enqueue(f);
            }
        }

        return result;
    }
    void HandlePaintStroke((int brush, int face, float distance) mapHit)
    {
        if(!KeyboardManager.IsDown(Key.LeftAlt))
        {
            if(sourceFace >= 0)
            {
                Toolbelt.ActiveTexture = MapTools.Brushes[sourceBrush].Faces[sourceFace].MaterialName;
                Dispatcher.UIThread.Post(()=> MainWindow.Instance.mapEditor.RefreshViews());
            }

            if(MouseManager.IsDown(MouseButton.Right))
            {
                MapTools.Brushes[mapHit.brush].Faces[mapHit.face].MaterialName = Toolbelt.ActiveTexture;

                BrushOperations.RebuildBrush(ref MapTools.Brushes[mapHit.brush]);
            }

            return;
        }

        bool rightHeld = MouseManager.IsDown(MouseButton.Right);

        // Begin stroke on the first frame right is held, if a source is set.
        if (rightHeld && !isPainting)
        {
            if (sourceBrush < 0 || sourceFace < 0) return;
            isPainting = true;
            paintedThisStroke.Clear();
            undosThisStroke.Clear();
        }

        // Paint while right is held.
        if (isPainting && rightHeld)
        {
            if (mapHit.brush == -1 || mapHit.face == -1 || mapHit.distance <= 0f) return;

            // Don't paint the source face itself, or any face already done this stroke.
            if (mapHit.brush == sourceBrush && mapHit.face == sourceFace) return;
            if (paintedThisStroke.Contains((mapHit.brush, mapHit.face))) return;

            SnapshotFaceForUndo(mapHit.brush, mapHit.face, out var restore);
            undosThisStroke.Add(restore);
            paintedThisStroke.Add((mapHit.brush, mapHit.face));

            TextureSeamSolver.Solve(
                srcBrush: MapTools.Brushes[sourceBrush],
                srcFaceIdx: sourceFace,
                dstBrush: ref MapTools.Brushes[mapHit.brush],
                dstFaceIdx: mapHit.face);

            BrushOperations.RebuildBrush(ref MapTools.Brushes[mapHit.brush]);

            sourceBrush = mapHit.brush;
            sourceFace = mapHit.face;
        }

        // Commit on release.
        if (isPainting && !rightHeld)
        {
            CommitStroke();
        }
    }
    void CommitStroke()
    {
        if (!isPainting) return;
        isPainting = false;

        if (undosThisStroke.Count == 0) return;

        var undos = new List<System.Action>(undosThisStroke);
        Toolbelt.UndoManager.DoOnUndo(() =>
        {
            foreach (var u in undos) u();
        });

        paintedThisStroke.Clear();
        undosThisStroke.Clear();

        sourceBrush = originalSourceBrush;
        sourceFace = originalSourceFace;
        Toolbelt.SelectedObjects.Clear();
        Toolbelt.SelectedObjects.Add(new FaceMoveable(sourceBrush, sourceFace));
    }
    public override void OnRender(float delta)
    {
        if (terrainsSelected && ViewportManager.Active.IsPerspective)
        {
            if (hoveredPinTerrain == -1) return;

            basicEffect.Alpha = 0.8f;
            basicEffect.World = Matrix.CreateScale(radius) * Matrix.CreateTranslation(terrainEditPoint);
            basicEffect.CurrentTechnique.Passes[0].Apply();
            graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, nubs, 0, nubs.Length / 2);

            var move = TextureSettingsWindow.Instance?.MoveMode ?? TextureSettingsWindow.TerainMoveMode.Normal;

            var normal = MapTools.Terrains[hoveredPinTerrain].SourceNormal;

            switch (move)
            {
                case TextureSettingsWindow.TerainMoveMode.X: normal = Vector3.UnitX; break;
                case TextureSettingsWindow.TerainMoveMode.Y: normal = Vector3.UnitY; break;
                case TextureSettingsWindow.TerainMoveMode.Z: normal = Vector3.UnitZ; break;
            }

            var arrowUp = move == TextureSettingsWindow.TerainMoveMode.Y ? Vector3.UnitZ : Vector3.UnitY;
            if (MathF.Abs(Vector3.Dot(normal, arrowUp)) > 0.99f)
            {
                arrowUp = Vector3.UnitX;
            }

            basicEffect.World = Matrix.CreateWorld(Vector3.Zero, normal, arrowUp) *
                Matrix.CreateTranslation(terrainEditPoint);
            basicEffect.CurrentTechnique.Passes[0].Apply();
            graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, arrow, 0, arrow.Length / 2);

            foreach (var (terrainIdx, vertexIdx) in pinnedVertices)
            {
                if (terrainIdx < 0 || terrainIdx >= MapTools.Terrains.Length) continue;
                var verts = MapTools.Terrains[terrainIdx].Vertices;
                if (vertexIdx < 0 || vertexIdx >= verts.Length) continue;

                basicEffect.World = Matrix.CreateScale(0.15f) * Matrix.CreateTranslation(verts[vertexIdx].Position);
                basicEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, nubs, 0, nubs.Length / 2);
            }
        }
    }
    void SyncWindowToSource()
    {
        if (sourceBrush < 0 || sourceFace < 0) return;

        Toolbelt.SelectedObjects.Clear();
        Toolbelt.SelectedObjects.Add(new FaceMoveable(sourceBrush, sourceFace));
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

        restore = () =>
        {
            ref var f = ref MapTools.Brushes[bi].Faces[fi];
            f.MaterialName = n;
            f.Surface = s;
            f.TOffX = ox;
            f.TOffY = oy;
            f.TScaleX = sx;
            f.TScaleY = sy;
            f.UvRotation = r;
            f.LuxelScale = lx;
            f.UvProjectionMode = pm;
            BrushOperations.RebuildBrush(ref MapTools.Brushes[bi]);
        };
    }
    void HandleTerrainEditing((int terrain, float distance) terrainHit, Ray ray, float delta)
    {
        var terrains = Toolbelt.SelectedObjects.OfType<TerrainMoveable>();
        terrainsSelected = false;
        if (terrains.Count() > 0 && !KeyboardManager.IsDown(Key.LeftShift))
        {
            terrainsSelected = true;

            radius = TextureSettingsWindow.Instance?.Radius ?? 2;
            strength = TextureSettingsWindow.Instance?.Strength ?? 2;
            var mode = TextureSettingsWindow.Instance?.EditMode ?? TextureSettingsWindow.TerrainEditMode.Shape;
            var move = TextureSettingsWindow.Instance?.MoveMode ?? TextureSettingsWindow.TerainMoveMode.Normal;

            bool leftDown = MouseManager.IsDown(MouseButton.Left);
            bool rightDown = MouseManager.IsDown(MouseButton.Right);
            isPaintingTerrain = (leftDown || rightDown) && terrainHit.terrain != -1;

            if (MouseManager.IsPressed(MouseButton.Middle) && hoveredPinTerrain != -1)
            {
                var key = (hoveredPinTerrain, hoveredPinVertex);
                if (!pinnedVertices.Add(key)) pinnedVertices.Remove(key);
            }

            float maxDist = float.MaxValue;
            hoveredPinTerrain = -1;
            hoveredPinVertex = -1;

            foreach (var t in terrains)
            {
                var terrain = MapTools.Terrains[t.terrain];

                for (int vi = 0; vi < terrain.Vertices.Length; vi++)
                {
                    float dist = Vector3.DistanceSquared(ray.Position + ray.Direction * terrainHit.distance, terrain.Vertices[vi].Position);
                    if (dist < maxDist)
                    {
                        maxDist = dist;
                        terrainEditPoint = terrain.Vertices[vi].Position;
                        hoveredPinTerrain = t.terrain;
                        hoveredPinVertex = vi;
                    }
                }
            }

            if (hoveredPinTerrain == -1) return;

            var normal = MapTools.Terrains[hoveredPinTerrain].SourceNormal;

            switch(move)
            {
                case TextureSettingsWindow.TerainMoveMode.X: normal = Vector3.UnitX; break;
                case TextureSettingsWindow.TerainMoveMode.Y: normal = Vector3.UnitY; break;
                case TextureSettingsWindow.TerainMoveMode.Z: normal = Vector3.UnitZ; break;
            }

            if (mode == TextureSettingsWindow.TerrainEditMode.Smooth)
            {
                if (MouseManager.IsDown(MouseButton.Left))
                    SmoothTerrains(terrains.Select(t => t.terrain).Distinct().ToList(), delta);
            }
            else
            {
                foreach (var t in terrains)
                {
                    var terrain = MapTools.Terrains[t.terrain];

                    for (int i = 0; i < terrain.Vertices.Length; i++)
                    {
                        if (pinnedVertices.Contains((t.terrain, i))) { continue; }

                        var vertex = terrain.Vertices[i];
                        float dist = Vector3.DistanceSquared(terrainEditPoint, vertex.Position);
                        if (dist < radius * radius)
                        {
                            float influence = (1 - (float.Sqrt(dist) / radius)) * strength;
                            Vector3 posDelta = Vector3.Zero;
                            float alphaDelta = 0f;

                            if (MouseManager.IsDown(MouseButton.Left))
                            {
                                switch (mode)
                                {
                                    case TextureSettingsWindow.TerrainEditMode.Shape:
                                        posDelta = normal * influence * delta;
                                        break;
                                    case TextureSettingsWindow.TerrainEditMode.Alpha:
                                        alphaDelta = influence * delta;
                                        break;
                                }
                            }
                            if (MouseManager.IsDown(MouseButton.Right))
                            {
                                switch (mode)
                                {
                                    case TextureSettingsWindow.TerrainEditMode.Shape:
                                        posDelta = -normal * influence * delta;
                                        break;
                                    case TextureSettingsWindow.TerrainEditMode.Alpha:
                                        alphaDelta = -influence * delta;
                                        break;
                                }
                            }

                            vertex.Position += posDelta;
                            if (alphaDelta != 0f)
                            {
                                vertex.TextureCoordinate.Z = float.Clamp(vertex.TextureCoordinate.Z + alphaDelta, 0, 1);
                            }
                        }

                        terrain.Vertices[i] = vertex;
                    }

                    BrushOperations.UpdateTerrain(ref terrain);
                    MapTools.Terrains[t.terrain] = terrain;

                    if (TextureSettingsWindow.Instance?.AutoSewEnabled == true)
                        SewSeamsForTerrain(t.terrain, SewEpsilon);
                }
            }
        }
        else
        {
            isPaintingTerrain = false;
        }

        if (isPaintingTerrain) return;

        bool justPressed = MouseManager.IsPressed(MouseButton.Left);
        if (!justPressed || !KeyboardManager.IsDown(Key.LeftShift)) return;

        if (!KeyboardManager.IsDown(Key.LeftCtrl)) Toolbelt.SelectedObjects.Clear();
        Toolbelt.SelectedObjects.Add(new TerrainMoveable(terrainHit.terrain));

        TextureSettingsWindow.Instance?.UpdateValues();
    }
    void SmoothTerrains(List<int> terrainIds, float dt)
    {
        var groupPos = new List<Vector3>();
        var members = new List<List<(int t, int v)>>();
        var groupOf = new Dictionary<(int, int), int>();
        var cells = new Dictionary<(int, int, int), List<int>>();

        foreach (var ti in terrainIds)
        {
            var verts = MapTools.Terrains[ti].Vertices;
            for (int vi = 0; vi < verts.Length; vi++)
            {
                var p = verts[vi].Position;
                var k = WeldKey(p);
                int g = -1;

                for (int dx = -1; dx <= 1 && g < 0; dx++)
                {
                    for (int dy = -1; dy <= 1 && g < 0; dy++)
                    {
                        for (int dz = -1; dz <= 1 && g < 0; dz++)
                        {
                            if (!cells.TryGetValue((k.Item1 + dx, k.Item2 + dy, k.Item3 + dz), out var list)) continue;
                            foreach (var cand in list)
                                if (Vector3.DistanceSquared(groupPos[cand], p) < WeldEps * WeldEps) { g = cand; break; }
                        }
                    }
                }

                if (g < 0)
                {
                    g = groupPos.Count;
                    groupPos.Add(p);
                    members.Add(new());
                    if (!cells.TryGetValue(k, out var list)) cells[k] = list = new();
                    list.Add(g);
                }

                members[g].Add((ti, vi));
                groupOf[(ti, vi)] = g;
            }
        }

        var neighbors = new HashSet<int>[groupPos.Count];
        for (int g = 0; g < groupPos.Count; g++)
        {
            neighbors[g] = new();
            foreach (var (ti, vi) in members[g])
            {
                int res = GetGridResolution(MapTools.Terrains[ti]);
                if (res < 2) continue;

                int row = vi / res, col = vi % res;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int nr = row + dr, nc = col + dc;
                        if (nr < 0 || nr >= res || nc < 0 || nc >= res) continue;
                        int ng = groupOf[(ti, nr * res + nc)];
                        if (ng != g) neighbors[g].Add(ng);
                    }
                }
            }
        }

        float r2 = radius * radius;
        var newPos = new Dictionary<int, Vector3>();

        for (int g = 0; g < groupPos.Count; g++)
        {
            float d2 = Vector3.DistanceSquared(groupPos[g], terrainEditPoint);
            if (d2 >= r2 || neighbors[g].Count == 0) continue;
            if (members[g].Any(m => pinnedVertices.Contains(m))) continue;

            Vector3 avg = Vector3.Zero;
            foreach (var ng in neighbors[g]) avg += groupPos[ng];
            avg /= neighbors[g].Count;

            float t = MathHelper.Clamp((1f - MathF.Sqrt(d2) / radius) * strength * dt, 0f, 1f);
            newPos[g] = Vector3.Lerp(groupPos[g], avg, t);
        }

        if (newPos.Count == 0) return;

        var touched = new HashSet<int>();
        foreach (var (g, pos) in newPos)
        {
            foreach (var (ti, vi) in members[g])
            {
                var v = MapTools.Terrains[ti].Vertices[vi];
                v.Position = pos;
                MapTools.Terrains[ti].Vertices[vi] = v;
                touched.Add(ti);
            }
        }

        foreach (var ti in touched)
        {
            var terrain = MapTools.Terrains[ti];
            BrushOperations.UpdateTerrain(ref terrain);
            MapTools.Terrains[ti] = terrain;
        }
    }
    static List<Vector3> GetTerrainSourceCorners(Brush brush, Face face)
    {
        if (face.Indices == null) return new List<Vector3>();

        HashSet<Vector3> uniqueVertices = new HashSet<Vector3>();
        for (int i = 0; i < face.Indices.Length; i++)
            uniqueVertices.Add(brush.Vertices[face.Indices[i]] + brush.Position);

        if (uniqueVertices.Count != 4) return new List<Vector3>();

        List<Vector3> corners = uniqueVertices.ToList();
        Vector3 normal = -Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
        normal.Normalize();
        corners.Sort((a, b) =>
        {
            Vector3 cross = Vector3.Cross(a - corners[0], b - corners[0]);
            float dot = Vector3.Dot(cross, normal);
            return dot > 0 ? -1 : 1;
        });
        return corners;
    }

    static int GetGridResolution(Terrain terrain)
    {
        int res = (int)MathF.Round(MathF.Sqrt(terrain.Vertices.Length));
        return (res >= 2 && res * res == terrain.Vertices.Length) ? res : -1;
    }

    static int[] GetGridCornerIndices(int res) => new[] { 0, res - 1, res * res - 1, (res - 1) * res };

    static List<(Vector3 p0, Vector3 p1, int cornerA, int cornerB)> GetCanonicalEdges(List<Vector3> corners4, int res)
    {
        var gridCorners = GetGridCornerIndices(res);
        var edges = new List<(Vector3, Vector3, int, int)>();
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            edges.Add((corners4[i], corners4[next], gridCorners[i], gridCorners[next]));
        }
        return edges;
    }
    static List<int> GetGridEdgeIndices(int res, int fromCornerIdx, int toCornerIdx)
    {
        var c = GetGridCornerIndices(res);
        int c0 = c[0], c1 = c[1], c2 = c[2], c3 = c[3];

        List<int> BuildRange(bool isRow, int fixedCoord, bool ascending)
        {
            var list = new List<int>();
            for (int i = 0; i < res; i++)
            {
                int t = ascending ? i : (res - 1 - i);
                list.Add(isRow ? fixedCoord * res + t : t * res + fixedCoord);
            }
            return list;
        }

        if (fromCornerIdx == c0 && toCornerIdx == c1) return BuildRange(true, 0, true);
        if (fromCornerIdx == c1 && toCornerIdx == c0) return BuildRange(true, 0, false);

        if (fromCornerIdx == c1 && toCornerIdx == c2) return BuildRange(false, res - 1, true);
        if (fromCornerIdx == c2 && toCornerIdx == c1) return BuildRange(false, res - 1, false);

        if (fromCornerIdx == c3 && toCornerIdx == c2) return BuildRange(true, res - 1, true);
        if (fromCornerIdx == c2 && toCornerIdx == c3) return BuildRange(true, res - 1, false);

        if (fromCornerIdx == c0 && toCornerIdx == c3) return BuildRange(false, 0, true);
        if (fromCornerIdx == c3 && toCornerIdx == c0) return BuildRange(false, 0, false);

        return null;
    }
    List<(int neighborTerrain, List<int> localIndices, List<int> neighborIndices)> FindTouchingSeams(int terrainIdx, float epsilon)
    {
        var results = new List<(int, List<int>, List<int>)>();

        var srcTerrain = MapTools.Terrains[terrainIdx];
        int srcBrushIdx = srcTerrain.BrushSource;
        if (srcBrushIdx < 0 || srcBrushIdx >= MapTools.Brushes.Length) return results;

        var srcBrush = MapTools.Brushes[srcBrushIdx];
        if (srcTerrain.FaceSource < 0 || srcTerrain.FaceSource >= srcBrush.Faces.Length) return results;

        int srcRes = GetGridResolution(srcTerrain);
        if (srcRes < 2) return results;

        var srcCorners = GetTerrainSourceCorners(srcBrush, srcBrush.Faces[srcTerrain.FaceSource]);
        if (srcCorners.Count != 4) return results;
        var srcEdges = GetCanonicalEdges(srcCorners, srcRes);

        for (int bi = 0; bi < MapTools.Brushes.Length; bi++)
        {
            var otherBrush = MapTools.Brushes[bi];

            for (int ofi = 0; ofi < otherBrush.Faces.Length; ofi++)
            {
                if (bi == srcBrushIdx && ofi == srcTerrain.FaceSource) continue;

                for (int ti = 0; ti < MapTools.Terrains.Length; ti++)
                {
                    if (ti == terrainIdx) continue;
                    if (MapTools.Terrains[ti].BrushSource != bi) continue;
                    if (MapTools.Terrains[ti].FaceSource != ofi) continue;

                    var otherTerrain = MapTools.Terrains[ti];
                    int otherRes = GetGridResolution(otherTerrain);
                    if (otherRes != srcRes) continue;

                    var otherCorners = GetTerrainSourceCorners(otherBrush, otherBrush.Faces[ofi]);
                    if (otherCorners.Count != 4) continue;
                    var otherEdges = GetCanonicalEdges(otherCorners, otherRes);

                    foreach (var eA in srcEdges)
                    {
                        foreach (var eB in otherEdges)
                        {
                            bool sameDir = Vector3.DistanceSquared(eA.p0, eB.p0) < epsilon * epsilon
                                        && Vector3.DistanceSquared(eA.p1, eB.p1) < epsilon * epsilon;
                            bool oppDir = Vector3.DistanceSquared(eA.p0, eB.p1) < epsilon * epsilon
                                       && Vector3.DistanceSquared(eA.p1, eB.p0) < epsilon * epsilon;
                            if (!sameDir && !oppDir) continue;

                            var localIdx = GetGridEdgeIndices(srcRes, eA.cornerA, eA.cornerB);
                            var neighborIdx = sameDir
                                ? GetGridEdgeIndices(otherRes, eB.cornerA, eB.cornerB)
                                : GetGridEdgeIndices(otherRes, eB.cornerB, eB.cornerA);

                            if (localIdx == null || neighborIdx == null) continue;

                            results.Add((ti, localIdx, neighborIdx));
                        }
                    }
                }
            }
        }

        return results;
    }

    void SewSeamsForTerrain(int terrainIdx, float epsilon)
    {
        foreach (var (neighborIdx, localIndices, neighborIndices) in FindTouchingSeams(terrainIdx, epsilon))
        {
            WeldAlongSeam(terrainIdx, neighborIdx, localIndices, neighborIndices);
        }
    }

    void WeldAlongSeam(int terrainAIdx, int terrainBIdx, List<int> localIndices, List<int> neighborIndices)
    {
        if (localIndices.Count != neighborIndices.Count) return;

        var a = MapTools.Terrains[terrainAIdx];
        var b = MapTools.Terrains[terrainBIdx];

        bool touchedA = false, touchedB = false;

        for (int k = 0; k < localIndices.Count; k++)
        {
            int i = localIndices[k];
            int j = neighborIndices[k];

            if (pinnedVertices.Contains((terrainAIdx, i))) continue;
            if (pinnedVertices.Contains((terrainBIdx, j))) continue;

            Vector3 mid = a.Vertices[i].Position;
            float alphaMid = a.Vertices[i].TextureCoordinate.Z;

            var va = a.Vertices[i]; va.Position = mid; va.TextureCoordinate.Z = alphaMid; a.Vertices[i] = va;
            var vb = b.Vertices[j]; vb.Position = mid; vb.TextureCoordinate.Z = alphaMid; b.Vertices[j] = vb;

            touchedA = true;
            touchedB = true;
        }

        if (touchedA) { BrushOperations.UpdateTerrain(ref a); MapTools.Terrains[terrainAIdx] = a; }
        if (touchedB) { BrushOperations.UpdateTerrain(ref b); MapTools.Terrains[terrainBIdx] = b; }
    }

    public void SewAllSeams()
    {
        var terrains = Toolbelt.SelectedObjects.OfType<TerrainMoveable>().Select(t => t.terrain).ToList();
        if (terrains.Count == 0) return;

        var snapshots = terrains.Select(ti => (ti, original: (TerrainVertex[])MapTools.Terrains[ti].Vertices.Clone())).ToList();

        foreach (var ti in terrains)
            SewSeamsForTerrain(ti, SewEpsilon);

        Toolbelt.UndoManager.DoOnUndo(() =>
        {
            foreach (var (ti, original) in snapshots)
            {
                var t = MapTools.Terrains[ti];
                t.Vertices = original;
                BrushOperations.UpdateTerrain(ref t);
                MapTools.Terrains[ti] = t;
            }
        });
    }
}
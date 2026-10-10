using Microsoft.Xna.Framework;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Tools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2.Editor.Mapper;

public static class MapClipboard
{
    class CopiedEntity
    {
        public EntityReference Entity;
        public List<int> OwnedBrushes = new();
    }

    static List<Brush> brushes = new();
    static List<Terrain> terrains = new();
    static List<CopiedEntity> entities = new();
    static List<Hint> hints = new();
    static BoundingBox bounds;

    public static bool HasContent => brushes.Count > 0 || entities.Count > 0 || hints.Count > 0;
    public static BoundingBox Bounds => bounds;

    public static bool Copy(IEnumerable<Transformable> selection)
    {
        var brushSet = new HashSet<int>();
        var entitySet = new HashSet<int>();
        var hintSet = new HashSet<int>();

        foreach (var obj in selection)
        {
            switch (obj)
            {
                case EntityMoveable em when em.entity >= 0 && em.entity < MapTools.Entities.Length:
                    entitySet.Add(em.entity);
                    var owned = MapTools.Entities[em.entity].BrushIndices;
                    if (owned != null)
                    {
                        foreach (int b in owned)
                        {
                            if (b >= 0 && b < MapTools.Brushes.Length) brushSet.Add(b);
                        }
                    }
                    break;
                case HintMoveable hm when hm.hint >= 0 && hm.hint < MapTools.Hints.Length:
                    hintSet.Add(hm.hint);
                    break;
                default:
                    int bi = SelectionTool.ResolveBrushForEditing(obj);
                    if (bi >= 0 && bi < MapTools.Brushes.Length) brushSet.Add(bi);
                    break;
            }
        }

        foreach (int b in brushSet)
        {
            var owner = MapTools.GetOwningEntity(b);
            if (owner == null) continue;
            int ei = Array.IndexOf(MapTools.Entities, owner);
            if (ei != -1) entitySet.Add(ei);
        }

        if (brushSet.Count == 0 && entitySet.Count == 0 && hintSet.Count == 0) return false;

        var brushOrder = brushSet.OrderBy(i => i).ToList();
        var brushMap = new Dictionary<int, int>();
        for (int i = 0; i < brushOrder.Count; i++)
        {
            brushMap[brushOrder[i]] = i;
        }

        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);
        void Include(BoundingBox box)
        {
            min = Vector3.Min(min, box.Min);
            max = Vector3.Max(max, box.Max);
        }

        brushes = new List<Brush>();
        foreach (int b in brushOrder)
        {
            brushes.Add(MapTools.Brushes[b].Clone());
            Include(MapTools.BrushBounds[b]);
        }

        terrains = new List<Terrain>();
        for (int i = 0; i < MapTools.Terrains.Length; i++)
        {
            if (!brushMap.TryGetValue(MapTools.Terrains[i].BrushSource, out int ci)) continue;
            var t = CloneTerrain(MapTools.Terrains[i]);
            t.BrushSource = ci;
            terrains.Add(t);
            Include(MapTools.Terrains[i].Bounds);
        }

        entities = new List<CopiedEntity>();
        foreach (int ei in entitySet.OrderBy(i => i))
        {
            var source = MapTools.Entities[ei];
            var copied = new CopiedEntity { Entity = CloneEntity(source) };

            foreach (int b in brushOrder)
            {
                if (MapTools.GetOwningEntity(b) == source) copied.OwnedBrushes.Add(brushMap[b]);
            }

            if (copied.OwnedBrushes.Count == 0)
            {
                var box = new BoundingBox(source.Position - Vector3.One * 0.25f, source.Position + Vector3.One * 0.25f);
                int oi = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == source.EntityName);
                if (oi != -1)
                {
                    box = new BoundingBox(GlobalEditorData.EditorOverrides.overrides[oi].boundsMin + source.Position,
                                          GlobalEditorData.EditorOverrides.overrides[oi].boundsMax + source.Position);
                }
                Include(box);
            }

            entities.Add(copied);
        }

        hints = new List<Hint>();
        foreach (int hi in hintSet.OrderBy(i => i))
        {
            var h = MapTools.Hints[hi];
            hints.Add(CloneHint(h));
            Include(new BoundingBox(h.Position - Vector3.One * 0.25f, h.Position + Vector3.One * 0.25f));
        }

        bounds = new BoundingBox(min, max);
        return true;
    }

    public static List<Transformable> Paste(Vector3 offset)
    {
        var created = new List<Transformable>();
        var pastedEntities = new List<EntityReference>();

        foreach (var ce in entities)
        {
            var e = CloneEntity(ce.Entity);
            e.Position += offset;
            if (ce.OwnedBrushes.Count > 0)
            {
                e.BrushIndices = new List<int>();
                e.brushOwnerGUIDs = new List<Guid>();
            }
            MapTools.AddEntity(e);
            pastedEntities.Add(e);

            if (ce.OwnedBrushes.Count == 0) created.Add(new EntityMoveable(MapTools.Entities.Length - 1));
        }

        var newBrushIndices = new int[brushes.Count];
        for (int i = 0; i < brushes.Count; i++)
        {
            var b = brushes[i].Clone();
            b.Position += offset;
            MapTools.AddBrush(b);

            int idx = MapTools.Brushes.Length - 1;
            var faces = MapTools.Brushes[idx].Faces;
            for (int f = 0; f < faces.Length; f++)
            {
                if (faces[f].MaterialName != null && GlobalMapData.MaterialNameToIndex.TryGetValue(faces[f].MaterialName, out int surface))
                {
                    faces[f].Surface = surface;
                }
            }

            BrushOperations.RebuildBrush(ref MapTools.Brushes[idx]);
            MapTools.RecomputeBrushBounds(idx);
            newBrushIndices[i] = idx;
            created.Add(new BrushMoveable(idx));
        }

        for (int k = 0; k < entities.Count; k++)
        {
            foreach (int ci in entities[k].OwnedBrushes)
            {
                MapTools.AddBrushToEntity(pastedEntities[k], newBrushIndices[ci]);
            }
        }
        MapTools.SyncBrushOwnership();

        foreach (var t in terrains)
        {
            var copy = CloneTerrain(t);
            copy.BrushSource = newBrushIndices[t.BrushSource];
            BrushOperations.MoveTerrain(ref copy, offset);
            MapTools.AddTerrain(copy);
            created.Add(new TerrainMoveable(MapTools.Terrains.Length - 1));
        }

        foreach (var h in hints)
        {
            var copy = CloneHint(h);
            copy.Position += offset;
            MapTools.AddHint(copy);
            created.Add(new HintMoveable(MapTools.Hints.Length - 1));
        }

        return created;
    }

    public static Vector3 OffsetFor2D(Vector3 cursorWorld, Vector3 right, Vector3 up)
    {
        Vector3 delta = cursorWorld - (bounds.Min + bounds.Max) * 0.5f;
        return Snap(Vector3.Dot(delta, right) * right + Vector3.Dot(delta, up) * up);
    }

    public static Vector3 OffsetFor3D(Vector3 hitPoint, Vector3 normal)
    {
        Vector3 center = (bounds.Min + bounds.Max) * 0.5f;
        Vector3 anchor = new Vector3(
            SupportAxis(normal.X, bounds.Min.X, bounds.Max.X, center.X),
            SupportAxis(normal.Y, bounds.Min.Y, bounds.Max.Y, center.Y),
            SupportAxis(normal.Z, bounds.Min.Z, bounds.Max.Z, center.Z));

        return Snap(hitPoint - anchor);
    }

    static float SupportAxis(float n, float min, float max, float center)
    {
        if (n > 0.5f) return min;
        if (n < -0.5f) return max;
        return center;
    }

    static Vector3 Snap(Vector3 v) => Vector3.Round(v / Transformable.GridSize) * Transformable.GridSize;

    static EntityReference CloneEntity(EntityReference s) => new EntityReference
    {
        EntityName = s.EntityName,
        Name = s.Name,
        Position = s.Position,
        SpawnRotation = s.SpawnRotation,
        Rotation = s.Rotation,
        Scale = s.Scale,
        EntityOutputs = s.EntityOutputs == null ? new List<(string, EntityOutput)>() : new List<(string, EntityOutput)>(s.EntityOutputs),
        Properties = (EntityProperty[])(s.Properties?.Clone() ?? Array.Empty<EntityProperty>()),
    };

    static Terrain CloneTerrain(Terrain t)
    {
        t.Vertices = (TerrainVertex[])t.Vertices?.Clone();
        t.Triangles = (short[])t.Triangles?.Clone();
        t.editor_cheat_flipalphavert = (TerrainVertex[])t.editor_cheat_flipalphavert?.Clone();
        return t;
    }

    static Hint CloneHint(Hint h) => new Hint
    {
        Position = h.Position,
        Header = h.Header,
        Body = h.Body,
    };
}
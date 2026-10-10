using Microsoft.Xna.Framework;
using Rockwall;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper;

/// <summary>
/// A snapshot of a Transformable that can restore it after deletion.
/// </summary>
public abstract class TransformableSnapshot
{
    public abstract void Restore();
}

public abstract class Transformable
{
    public static float GridSize = 0.5f;
    public static float RotationSnapDegrees = 15f;
    public Vector3 FloatingPosition;
    public Vector3 StartPosition;
    public abstract Vector3 GetPosition();
    public abstract Guid GetGuid();

    public void PrepareMove()
    {
        FloatingPosition = StartPosition = GetPosition();
        BeforeMove();
    }
    public void TryMove(Vector3 delta)
    {
        FloatingPosition += delta;
        Vector3 currentPos = GetPosition();

        Vector3 snappedPos = Vector3.Floor(FloatingPosition / GridSize) * GridSize;

        if (snappedPos != currentPos) Move(snappedPos - currentPos);
    }
    public abstract void Move(Vector3 delta);
    public virtual void PostMove() { }
    public virtual void BeforeMove() { }

    /// <summary>
    /// Rotates the moveable object by <paramref name="delta"/> degrees.
    /// </summary>
    /// <param name="delta">Euler angles, in degrees.</param>
    public abstract void Rotate(Vector3 axis, float radians);
    public virtual void RotateAbout(Vector3 pivot, Vector3 axis, float radians)
    {
        Vector3 pos = GetPosition();
        Quaternion q = Quaternion.CreateFromAxisAngle(axis, radians);
        Vector3 newPos = pivot + Vector3.Transform(pos - pivot, q);
        Move(newPos - pos);
        Rotate(axis, radians);
    }

    public abstract void Delete();
    public abstract Transformable Duplicate();

    /// <summary>
    /// Creates a snapshot of this object's current state that can restore it after deletion.
    /// </summary>
    public abstract TransformableSnapshot SnapshotForUndo();
}
public class HintMoveable : Transformable
{
    // Resolved through GuidMapper rather than cached at construction time: this wrapper can outlive
    // the operation it was created for (selection, or an undo/redo closure), and the hint's position
    // in MapTools.Hints can shift or disappear from edits that happen in between. -1 means "gone".
    public Guid HintId;
    public int hint => MapTools.ResolveIndex(HintId, MapTools.ObjType.Hint);

    public HintMoveable(int hint)
    {
        HintId = MapTools.Hints[hint].GroupingID!.Value;
    }

    public override Vector3 GetPosition()
    {
        int i = hint;
        return i == -1 ? Vector3.Zero : MapTools.Hints[i].Position;
    }

    public override void Move(Vector3 delta)
    {
        int i = hint;
        if (i == -1) return;
        MapTools.Hints[i].Position += delta;
    }

    public override void Rotate(Vector3 axis, float radians)
    {
        // TODO: implement, if hints ever gain orientation
    }

    public override void Delete()
    {
        int i = hint;
        if (i == -1) return;
        MapTools.RemoveHint(MapTools.Hints[i]);
    }

    public override Transformable Duplicate()
    {
        int i = hint;
        if (i == -1) return null;
        var duplicate = MapTools.Hints[i];
        MapTools.AddHint(duplicate);
        return new HintMoveable(MapTools.Hints.Length - 1);
    }

    public override TransformableSnapshot SnapshotForUndo()
    {
        return new AllHintSnapshot(MapTools.Hints);
    }

    public override Guid GetGuid()
    {
        return HintId;
    }
}

/// <summary>
/// Restores a deleted hint by re-adding a copy of its state at snapshot time.
/// </summary>
public class AllHintSnapshot : TransformableSnapshot
{
    private readonly Hint[] savedHints;

    public AllHintSnapshot(Hint[] hints)
    {
        savedHints = new Hint[hints.Length];
        Array.Copy(hints, savedHints, hints.Length);
    }

    public override void Restore()
    {
        MapTools.ActiveMap.Hints = savedHints;
        MapTools.RebuildGuidMapper();
    }
}
public class BrushVertexMoveable : Transformable
{
    public Guid BrushId;
    public int brush
    {
        get => MapTools.ResolveIndex(BrushId, MapTools.ObjType.Brush);
        set => BrushId = MapTools.Brushes[value].GroupingID!.Value;
    }
    public int vert; // index into the SHARED vertex pool (after dedup by position)

    // The world-space position this vert represents (set at PrepareMove time)
    private Vector3 originalWorldPos;

    private Vector3[] preEditVertices;
    private Face[] preEditFaces;
    private List<BrushOperations.TerrainSyncEntry> terrainSync;

    public override Vector3 GetPosition()
    {
        if (brush >= MapTools.Brushes.Length) return Vector3.Zero;
        if (vert >= MapTools.Brushes[brush].Vertices.Length) return Vector3.Zero;
        return MapTools.Brushes[brush].Vertices[vert] + MapTools.Brushes[brush].Position;
    }

    public override void BeforeMove()
    {
        originalWorldPos = GetPosition();

        if (brush < 0 || brush >= MapTools.Brushes.Length) return;
        preEditVertices = (Vector3[])MapTools.Brushes[brush].Vertices.Clone();
        preEditFaces = (Face[])MapTools.Brushes[brush].Faces.Clone();
        terrainSync = BrushOperations.CaptureTerrainSync(brush, new[] { originalWorldPos });
    }

    public override void Move(Vector3 delta)
    {
        if (brush >= MapTools.Brushes.Length) return;
        if (vert >= MapTools.Brushes[brush].Vertices.Length) return;

        Vector3 vertPos = GetPosition();

        List<int> viableVerts = new List<int> { vert };
        for (int v = 0; v < MapTools.Brushes[brush].Vertices.Length; v++)
        {
            Vector3 testPos = MapTools.Brushes[brush].Vertices[v] + MapTools.Brushes[brush].Position;
            if (Vector3.Distance(vertPos, testPos) < float.Epsilon)
                viableVerts.Add(v);
        }

        Vector3 newPos = vertPos + delta - MapTools.Brushes[brush].Position;

        foreach (int v in viableVerts)
            MapTools.Brushes[brush].Vertices[v] = newPos;
    }

    public override void PostMove()
    {
        if (brush >= MapTools.Brushes.Length) return;
        if (vert >= MapTools.Brushes[brush].Vertices.Length) return;

        Vector3 totalDelta = GetPosition() - originalWorldPos;

        BrushOperations.RecalculateBrushPlanes(ref MapTools.Brushes[brush]);
        BrushOperations.RebuildBrush(ref MapTools.Brushes[brush]);
        MapTools.RecomputeBrushBounds(brush);

        if (terrainSync != null && terrainSync.Count > 0 &&
            !BrushOperations.ApplyTerrainSync(brush, terrainSync, totalDelta, out _))
        {
            MapTools.Brushes[brush].Vertices = preEditVertices;
            MapTools.Brushes[brush].Faces = preEditFaces;
            MapTools.RecomputeBrushBounds(brush);
            Toolbelt.ShowTerrainQuadError();
        }

        //Vector3 finalWorldPos = originalWorldPos + totalDelta;
        //var verts = MapTools.Brushes[brush].vertices;
        //var bpos = MapTools.Brushes[brush].position;
        //int best = -1;
        //float bestDist = float.MaxValue;
        //for (int v = 0; v < verts.Length; v++)
        //{
        //    float d = Vector3.DistanceSquared(verts[v] + bpos, finalWorldPos);
        //    if (d < bestDist) { bestDist = d; best = v; }
        //}
        //if (best != -1) vert = best;
    }

    public override void Rotate(Vector3 axis, float radians) { }
    public override void Delete() { }
    public override Transformable Duplicate() => null;

    public override Guid GetGuid()
    {
        return BrushId;
    }

    public override TransformableSnapshot SnapshotForUndo() => new AllBrushSnapshot(MapTools.Brushes);
    private class AllBrushSnapshot : TransformableSnapshot
    {
        Brush[] original;
        public AllBrushSnapshot(Brush[] reference)
        {
            original = new Brush[reference.Length];
            Array.Copy(reference, original, reference.Length);
        }
        public override void Restore()
        {
            MapTools.ActiveMap.Brushes = original;
            MapTools.RecomputeAllBrushBounds();
            MapTools.RebuildGuidMapper();
        }
    }
}
public class BrushEdgeMoveable : Transformable
{
    public Guid BrushId;
    public int brush
    {
        get => MapTools.ResolveIndex(BrushId, MapTools.ObjType.Brush);
        set => BrushId = MapTools.Brushes[value].GroupingID!.Value;
    }
    public int vertA, vertB;

    private Vector3 finalAPos;
    private Vector3 finalBPos;

    private Vector3 originalAPosWorld;
    private Vector3 originalBPosWorld;

    private Vector3[] preEditVertices;
    private Face[] preEditFaces;
    private List<BrushOperations.TerrainSyncEntry> terrainSync;

    public override Vector3 GetPosition()
    {
        if (brush >= MapTools.Brushes.Length) return Vector3.Zero;

        return (MapTools.Brushes[brush].Vertices[vertA] + MapTools.Brushes[brush].Vertices[vertB]) * 0.5f + MapTools.Brushes[brush].Position;
    }

    public override void BeforeMove()
    {
        if (brush < 0 || brush >= MapTools.Brushes.Length) return;
        preEditVertices = (Vector3[])MapTools.Brushes[brush].Vertices.Clone();
        preEditFaces = (Face[])MapTools.Brushes[brush].Faces.Clone();

        originalAPosWorld = MapTools.Brushes[brush].Vertices[vertA] + MapTools.Brushes[brush].Position;
        originalBPosWorld = MapTools.Brushes[brush].Vertices[vertB] + MapTools.Brushes[brush].Position;

        terrainSync = BrushOperations.CaptureTerrainSync(brush, new[] { originalAPosWorld, originalBPosWorld });
    }

    public override void Move(Vector3 delta)
    {
        if (brush >= MapTools.Brushes.Length) return;

        var vertPosA = MapTools.Brushes[brush].Vertices[vertA] + MapTools.Brushes[brush].Position;

        List<int> viableVertsA = new List<int> { vertA };
        List<int> viableVertsB = new List<int> { vertB };
        for (int v = 0; v < MapTools.Brushes[brush].Vertices.Length; v++)
        {
            Vector3 testPos = MapTools.Brushes[brush].Vertices[v] + MapTools.Brushes[brush].Position;
            if (Vector3.Distance(vertPosA, testPos) < float.Epsilon)
                viableVertsA.Add(v);
        }

        var vertPosB = MapTools.Brushes[brush].Vertices[vertB] + MapTools.Brushes[brush].Position;

        for (int v = 0; v < MapTools.Brushes[brush].Vertices.Length; v++)
        {
            Vector3 testPos = MapTools.Brushes[brush].Vertices[v] + MapTools.Brushes[brush].Position;
            if (Vector3.Distance(vertPosB, testPos) < float.Epsilon)
                viableVertsB.Add(v);
        }

        Vector3 newPosA = vertPosA + delta - MapTools.Brushes[brush].Position;
        Vector3 newPosB = vertPosB + delta - MapTools.Brushes[brush].Position;

        foreach (int v in viableVertsA)
            MapTools.Brushes[brush].Vertices[v] = newPosA;
        foreach (int v in viableVertsB)
            MapTools.Brushes[brush].Vertices[v] = newPosB;

        finalAPos = newPosA;
        finalBPos = newPosB;
    }

    public override void PostMove()
    {
        if (brush >= MapTools.Brushes.Length) return;

        Vector3 totalDelta = (finalAPos + MapTools.Brushes[brush].Position) - originalAPosWorld;

        BrushOperations.RecalculateBrushPlanes(ref MapTools.Brushes[brush]);
        BrushOperations.RebuildBrush(ref MapTools.Brushes[brush]);
        MapTools.RecomputeBrushBounds(brush);

        if (terrainSync != null && terrainSync.Count > 0 &&
            !BrushOperations.ApplyTerrainSync(brush, terrainSync, totalDelta, out _))
        {
            MapTools.Brushes[brush].Vertices = preEditVertices;
            MapTools.Brushes[brush].Faces = preEditFaces;
            MapTools.RecomputeBrushBounds(brush);
            Toolbelt.ShowTerrainQuadError();
            return;
        }

        // Edge order may have changed

        //for (int v = 0; v < MapTools.Brushes[brush].vertices.Length; v++)
        //{
        //    Vector3 testPos = MapTools.Brushes[brush].vertices[v] + MapTools.Brushes[brush].position;
        //    if (Vector3.DistanceSquared(finalAPos + MapTools.Brushes[brush].position, testPos) < 0.0025f)
        //    {
        //        vertA = v;
        //        break;
        //    }
        //}
        //for (int v = 0; v < MapTools.Brushes[brush].vertices.Length; v++)
        //{
        //    Vector3 testPos = MapTools.Brushes[brush].vertices[v] + MapTools.Brushes[brush].position;
        //    if (Vector3.DistanceSquared(finalAPos + MapTools.Brushes[brush].position, testPos) < 0.0025f)
        //    {
        //        vertB = v;
        //        break;
        //    }
        //}
    }

    public override Guid GetGuid()
    {
        return BrushId;
    }

    public override void Rotate(Vector3 axis, float radians)
    {
        // Can't rotate a point
    }

    public override void Delete()
    {
        // Vertices aren't deleted individually
    }

    public override Transformable Duplicate()
    {
        return null;
    }

    public override TransformableSnapshot SnapshotForUndo()
    {
        // Vertex moves are handled via CreateUndoStateForAllSelectedObjects,
        // so this snapshot is a no-op for deletion (verts can't be deleted).
        return new AllBrushSnapshot(MapTools.Brushes);
    }

    private class AllBrushSnapshot : TransformableSnapshot
    {
        Brush[] original;
        public AllBrushSnapshot(Brush[] reference)
        {
            original = new Brush[reference.Length];
            Array.Copy(reference, original, reference.Length);
        }
        public override void Restore()
        {
            MapTools.ActiveMap.Brushes = original;
            MapTools.RecomputeAllBrushBounds();
            MapTools.RebuildGuidMapper();
        }
    }
}


public class FaceMoveable : Transformable
{
    public Guid BrushId;
    public int brush => MapTools.ResolveIndex(BrushId, MapTools.ObjType.Brush);
    public int face;

    public FaceMoveable(int brush, int face)
    {
        BrushId = MapTools.Brushes[brush].GroupingID!.Value;
        this.face = face;
    }

    public override Vector3 GetPosition()
    {
        if (brush == -1 || brush > MapTools.Brushes.Length) return Vector3.Zero;
        if (face == -1 || face > MapTools.Brushes[brush].Faces.Length) return Vector3.Zero;

        var b = MapTools.Brushes[brush];
        var f = b.Faces[face];
        return f.Indices.Select(i => b.Vertices[i]).Aggregate((s, v) => s + v) / f.Indices.Length + b.Position;
    }

    public override void Move(Vector3 delta)
    {
        if (brush >= MapTools.Brushes.Length) return;
        if (face >= MapTools.Brushes[brush].Faces.Length) return;
        if (brush < 0) return;

        if (delta.LengthSquared() < 0.001f) return;

        var norm = (MapTools.Brushes[brush].Faces[face].Normal);
        var move = Vector3.Dot(norm, delta);

        BrushOperations.MoveFace(brush, face, move);

        MapTools.RecomputeBrushBounds(brush);
    }
    public override void Rotate(Vector3 axis, float radians)
    {
        // TODO: implement
    }

    public override Guid GetGuid()
    {
        return BrushId;
    }

    public override void Delete()
    {
        var source = MapTools.Brushes[brush];
        MapTools.RemoveBrush(source);
    }

    public override Transformable Duplicate()
    {
        if (brush >= MapTools.Brushes.Length) return null;
        if (face >= MapTools.Brushes[brush].Faces.Length) return null;

        MapTools.AddBrush(MapTools.Brushes[brush].Clone());
        BrushOperations.RebuildBrush(ref MapTools.Brushes[MapTools.Brushes.Length - 1]);
        return new BrushMoveable(MapTools.Brushes.Length - 1);
    }

    public override TransformableSnapshot SnapshotForUndo()
    {
        // Deleting a face deletes the whole brush, so snapshot the whole brush
        return new AllBrushSnapshot(MapTools.Brushes);
    }
}

public class BrushMoveable : Transformable
{
    public Guid BrushId;
    public int brush => MapTools.ResolveIndex(BrushId, MapTools.ObjType.Brush);

    public BrushMoveable(int brush)
    {
        BrushId = MapTools.Brushes[brush].GroupingID!.Value;
    }

    public override Vector3 GetPosition()
    {
        if (brush >= MapTools.Brushes.Length) return Vector3.Zero;
        if (brush < 0) return Vector3.Zero;
        if (MapTools.Brushes[brush].Faces == null) return Vector3.Zero;
        if (MapTools.Brushes[brush].Vertices == null) return Vector3.Zero;

        return MapTools.Brushes[brush].Vertices.Aggregate((s, v) => s + v) / MapTools.Brushes[brush].Vertices.Length
               + MapTools.Brushes[brush].Position;
    }

    public override void Move(Vector3 delta)
    {
        if (brush >= MapTools.Brushes.Length) return;
        if (brush < 0) return;

        MapTools.Brushes[brush].Position += delta;
        BrushOperations.RebuildBrush(ref MapTools.Brushes[brush]);
        MapTools.RecomputeBrushBounds(brush);

        for (int i = 0; i < MapTools.Terrains.Length; i++)
        {
            if (MapTools.Terrains[i].BrushSource != brush) continue;
            var terrain = MapTools.Terrains[i];
            BrushOperations.MoveTerrain(ref terrain, delta);
            MapTools.Terrains[i] = terrain;
        }
    }
    public override void Rotate(Vector3 axis, float radians)
    {
        if (brush >= MapTools.Brushes.Length) return;
        if (brush < 0) return;

        var q = Quaternion.CreateFromAxisAngle(axis, radians);
        ref Brush b = ref MapTools.Brushes[brush];
        Vector3 center = GetPosition();

        for (int f = 0; f < b.Faces.Length; f++)
        {
            if (!b.Faces[f].Plane.HasValue) continue;

            Plane oldPlane = b.Faces[f].Plane.Value;
            Vector3 n = oldPlane.Normal;
            float nDotN = Vector3.Dot(n, n);
            if (nDotN < 1e-12f) continue;

            Vector3 anchorLocal = -oldPlane.D * n / nDotN;
            Vector3 anchorWorld = anchorLocal + b.Position;

            Vector3 newAnchorWorld = center + Vector3.Transform(anchorWorld - center, q);
            Vector3 newNormal = Vector3.Normalize(Vector3.Transform(n, q));
            Vector3 newAnchorLocal = newAnchorWorld - b.Position;

            b.Faces[f].Plane = new Plane(newNormal, -Vector3.Dot(newNormal, newAnchorLocal));
        }

        BrushOperations.RebuildBrush(ref b);
        MapTools.RecomputeBrushBounds(brush);

        for (int i = 0; i < MapTools.Terrains.Length; i++)
        {
            if (MapTools.Terrains[i].BrushSource != brush) continue;
            var terrain = MapTools.Terrains[i];
            for (int v = 0; v < terrain.Vertices.Length; v++)
                terrain.Vertices[v].Position = center + Vector3.Transform(terrain.Vertices[v].Position - center, q);
            BrushOperations.UpdateTerrain(ref terrain);
            MapTools.Terrains[i] = terrain;
        }
    }

    public override Guid GetGuid()
    {
        return BrushId;
    }

    public override void Delete()
    {
        if (brush >= MapTools.Brushes.Length) return;
        if (brush < 0) return;

        var source = MapTools.Brushes[brush];
        MapTools.RemoveBrush(source);
    }

    public override Transformable Duplicate() => Duplicate(null);

    public Transformable Duplicate(EntityReference targetEntity)
    {
        if (brush >= MapTools.Brushes.Length) return null;
        if (brush < 0) return null;

        var sourceOwner = MapTools.GetOwningEntity(brush);
        int sourceBrushIndex = brush;

        MapTools.AddBrush(MapTools.Brushes[sourceBrushIndex].Clone());
        int newIndex = MapTools.Brushes.Length - 1;
        BrushOperations.RebuildBrush(ref MapTools.Brushes[newIndex]);

        if (sourceOwner != null)
        {
            var owner = targetEntity ?? CloneEntityShell(sourceOwner);
            if (targetEntity == null) MapTools.AddEntity(owner);

            MapTools.AddBrushToEntity(owner, newIndex);
            MapTools.SyncBrushOwnership();
        }

        // If this brush is a terrain source, duplicate its terrain onto the new brush too.
        int terrainIndex = Array.FindIndex(MapTools.Terrains, t => t.BrushSource == sourceBrushIndex);
        if (terrainIndex != -1)
        {
            var dupTerrain = MapTools.Terrains[terrainIndex];

            dupTerrain.Vertices = (TerrainVertex[])dupTerrain.Vertices?.Clone();
            dupTerrain.Triangles = (short[])dupTerrain.Triangles?.Clone();
            dupTerrain.editor_cheat_flipalphavert = (TerrainVertex[])dupTerrain.editor_cheat_flipalphavert?.Clone();

            // AddTerrain resolves GroupingID/BrushOwnerGUID from brushSource itself.
            dupTerrain.BrushSource = newIndex;
            MapTools.AddTerrain(dupTerrain);
        }

        return new BrushMoveable(newIndex);
    }

    private static EntityReference CloneEntityShell(EntityReference source)
    {
        return new EntityReference
        {
            EntityName = source.EntityName,
            Name = source.Name,
            Position = source.Position,
            SpawnRotation = source.SpawnRotation,
            Rotation = source.Rotation,
            Scale = source.Scale,
            EntityOutputs = source.EntityOutputs == null ? null : new List<(string, EntityOutput)>(source.EntityOutputs),
            Properties = (EntityProperty[])(source.Properties?.Clone() ?? Array.Empty<EntityProperty>()),
            BrushIndices = new List<int>(),
            brushOwnerGUIDs = new List<Guid>(),
        };
    }

    public override TransformableSnapshot SnapshotForUndo()
    {
        return new BrushWithOwnershipSnapshot(MapTools.Brushes, MapTools.Entities);
    }
}
public class BrushWithOwnershipSnapshot : TransformableSnapshot
{
    private readonly Brush[] originalBrushes;
    private readonly EntityReference[] originalEntities;
    private readonly Dictionary<Guid, List<Guid>> originalOwnership;

    public BrushWithOwnershipSnapshot(Brush[] brushes, EntityReference[] entities)
    {
        originalBrushes = new Brush[brushes.Length];
        Array.Copy(brushes, originalBrushes, brushes.Length);

        originalEntities = new EntityReference[entities.Length];
        Array.Copy(entities, originalEntities, entities.Length);

        originalOwnership = entities
            .Where(e => e?.brushOwnerGUIDs != null && e.GroupingID.HasValue)
            .ToDictionary(e => e.GroupingID!.Value, e => new List<Guid>(e.brushOwnerGUIDs));
    }

    public override void Restore()
    {
        MapTools.ActiveMap.Brushes = originalBrushes;
        MapTools.ActiveMap.EntityReferences = originalEntities;
        foreach (var entity in originalEntities)
        {
            if (entity?.GroupingID is { } id && originalOwnership.TryGetValue(id, out var guids))
                entity.brushOwnerGUIDs = guids;
        }
        MapTools.RecomputeAllBrushBounds();
        MapTools.RebuildGuidMapper(); // re-resolves brushIndices from the restored brushOwnerGUIDs
    }
}

public class EntityMoveable : Transformable
{
    public Guid EntityId;
    public int entity => MapTools.ResolveIndex(EntityId, MapTools.ObjType.Entity);

    public EntityMoveable(int entity)
    {
        EntityId = MapTools.Entities[entity].GroupingID!.Value;
    }

    public override Vector3 GetPosition()
    {
        return MapTools.Entities[entity].Position;
    }

    public override void Move(Vector3 delta)
    {
        MapTools.Entities[entity].Position += delta;
    }
    public override void Rotate(Vector3 axis, float radians)
    {
        ref var ent = ref MapTools.Entities[entity];

        Quaternion current = Quaternion.CreateFromYawPitchRoll(
            MathHelper.ToRadians(ent.SpawnRotation.X),
            MathHelper.ToRadians(ent.SpawnRotation.Y),
            MathHelper.ToRadians(ent.SpawnRotation.Z));

        Quaternion delta = Quaternion.CreateFromAxisAngle(axis, radians);
        Quaternion result = delta * current;

        ent.SpawnRotation = QuaternionToYawPitchRollDegrees(result);
    }

    static Vector3 QuaternionToYawPitchRollDegrees(Quaternion q)
    {
        Matrix m = Matrix.CreateFromQuaternion(q);

        float pitch = MathF.Asin(MathHelper.Clamp(-m.M32, -1f, 1f));
        float yaw, roll;

        if (MathF.Abs(m.M32) < 0.9999f)
        {
            yaw = MathF.Atan2(m.M31, m.M33);
            roll = MathF.Atan2(m.M12, m.M22);
        }
        else
        {
            yaw = MathF.Atan2(-m.M13, m.M11);
            roll = 0f;
        }

        return new Vector3(MathHelper.ToDegrees(yaw), MathHelper.ToDegrees(pitch), MathHelper.ToDegrees(roll));
    }

    public override void Delete()
    {
        MapTools.RemoveEntity(MapTools.Entities[entity]);
    }

    public override Guid GetGuid()
    {
        return EntityId;
    }

    public override Transformable Duplicate()
    {
        var source = MapTools.Entities[entity];
        var duplicate = new EntityReference
        {
            EntityName = source.EntityName,
            Name = source.Name,
            Scale = source.Scale,
            SpawnRotation = source.SpawnRotation,
            EntityOutputs = source.EntityOutputs.ToArray().ToList(),
            Position = source.Position,
            Properties = (EntityProperty[])(source.Properties?.Clone() ?? Array.Empty<EntityProperty>()),
        };

        if (GlobalEditorData.RegisteredEntityMeta.TryGetValue(source.EntityName, out var meta) && meta.Link is { } link)
        {
            duplicate.Name = MapTools.GenerateUniqueName(source.Name);
            SetPropertyValue(source, link.Next, duplicate.Name);
            if (link.Previous != null) SetPropertyValue(duplicate, link.Previous, source.Name);
        }

        MapTools.AddEntity(duplicate);
        return new EntityMoveable(MapTools.Entities.Length - 1);
    }
    static void SetPropertyValue(EntityReference target, string propertyName, string value)
    {
        int i = Array.FindIndex(target.Properties ?? Array.Empty<EntityProperty>(), p => p.Name == propertyName);
        if (i != -1) { target.Properties[i].Value = value; return; }
        target.Properties = (target.Properties ?? Array.Empty<EntityProperty>()).Append(new EntityProperty { Name = propertyName, Value = value }).ToArray();
    }

    public override TransformableSnapshot SnapshotForUndo()
    {
        // Deep copy the entity so the snapshot is independent of later changes
        var e = MapTools.Entities[entity];
        var copy = new EntityReference
        {
            EntityName = e.EntityName,
            Name = e.Name,
            Scale = e.Scale,
            Position = e.Position,
            SpawnRotation = e.SpawnRotation,
            Rotation = e.Rotation,
            EntityOutputs = e.EntityOutputs?.ToArray().ToList() ?? new List<(string, EntityOutput)>(),
            Properties = e.Properties != null ? (EntityProperty[])e.Properties.Clone() : null,
        };
        return new AllEntitySnapshot(MapTools.Entities);
    }
}

public class TerrainMoveable : Transformable
{
    public Guid TerrainId;
    public int terrain => MapTools.ResolveIndex(TerrainId, MapTools.ObjType.Terrain);

    public TerrainMoveable(int terrain)
    {
        TerrainId = MapTools.Terrains[terrain].GroupingID!.Value;
    }

    public override Vector3 GetPosition()
    {
        if (terrain >= MapTools.Terrains.Length) return Vector3.Zero;
        if (terrain < 0) return Vector3.Zero;

        var t = MapTools.Terrains[terrain];
        var center = Vector3.Zero;
        foreach (var vert in t.Vertices) center += vert.Position;

        center /= t.Vertices.Length;

        return center;
    }

    public override void Move(Vector3 delta)
    {
    }
    public override void Rotate(Vector3 axis, float radians)
    {
    }

    public override Guid GetGuid()
    {
        return TerrainId;
    }

    public override void Delete()
    {
        if (terrain >= MapTools.Terrains.Length) return;
        if (terrain < 0) return;

        var source = MapTools.Terrains[terrain];
        MapTools.RemoveTerrain(source);
    }

    public override Transformable Duplicate()
    {
        return null;
    }

    public override TransformableSnapshot SnapshotForUndo()
    {
        return new AllTerrainSnapshot(MapTools.Terrains, MapTools.Brushes);
    }
}

/// <summary>
/// Restores a deleted brush by re-adding a clone of its state at snapshot time.
/// </summary>
public class AllBrushSnapshot : TransformableSnapshot
{
    private readonly Brush[] savedBrushes;

    public AllBrushSnapshot(Brush[] brushes)
    {
        savedBrushes = new Brush[brushes.Length];
        Array.Copy(brushes, savedBrushes, brushes.Length);
    }

    public override void Restore()
    {
        MapTools.ActiveMap.Brushes = savedBrushes;
        MapTools.RecomputeAllBrushBounds();
        MapTools.RebuildGuidMapper();
    }
}

/// <summary>
/// Restores a deleted entity by re-adding a copy of its state at snapshot time.
/// </summary>
public class AllEntitySnapshot : TransformableSnapshot
{
    private readonly EntityReference[] savedEntities;

    public AllEntitySnapshot(EntityReference[] entities)
    {
        savedEntities = new EntityReference[entities.Length];
        Array.Copy(entities, savedEntities, entities.Length);
    }

    public override void Restore()
    {
        MapTools.ActiveMap.EntityReferences = savedEntities;
        MapTools.RebuildGuidMapper();
    }
}

/// <summary>
/// Restores a deleted terrain by re-adding a copy of its state at snapshot time.
/// </summary>
public class AllTerrainSnapshot : TransformableSnapshot
{
    private readonly Terrain[] savedTerrains;
    private readonly Brush[] savedBrushes;

    public AllTerrainSnapshot(Terrain[] terrains, Brush[] brushes)
    {
        savedTerrains = new Terrain[terrains.Length];
        savedBrushes = new Brush[brushes.Length];
        Array.Copy(terrains, savedTerrains, terrains.Length);
        Array.Copy(brushes, savedBrushes, brushes.Length);
    }

    public override void Restore()
    {
        MapTools.ActiveMap.Terrains = savedTerrains;
        MapTools.ActiveMap.Brushes = savedBrushes;
        MapTools.RebuildGuidMapper();
    }
}

public struct TransformableState
{
    public List<Transformable> affectedTransformables;
    public List<Vector3> deltaPositions;
}
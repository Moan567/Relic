using Chisel.Collision;
using Chisel.Utils;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Engine.Utils
{
    public static class Blockmap
    {
        public const float blockSize = 10;

        const int keyBits = 21;
        const int keyBias = 1 << 20;
        const ulong keyMask = (1UL << keyBits) - 1;

        static Dictionary<ulong, List<WorldEntity>> blockmap = new Dictionary<ulong, List<WorldEntity>>();

        private static WorldEntity[] scratchBuffer = new WorldEntity[16];

        public static void Clear() => blockmap.Clear();

        static ulong PackKey(int x, int y, int z)
        {
            ulong ux = (ulong)(x + keyBias) & keyMask;
            ulong uy = (ulong)(y + keyBias) & keyMask;
            ulong uz = (ulong)(z + keyBias) & keyMask;
            return (ux << (keyBits * 2)) | (uy << keyBits) | uz;
        }

        static void GetCoords(Vector3 fromPos, out int x, out int y, out int z)
        {
            x = (int)(fromPos.X / blockSize);
            y = (int)(fromPos.Y / blockSize);
            z = (int)(fromPos.Z / blockSize);
        }

        public static void RemoveEntity(WorldEntity entity)
        {
            GetCoords(entity.Position, out int x, out int y, out int z);
            ulong key = PackKey(x, y, z);

            if (blockmap.TryGetValue(key, out List<WorldEntity> value))
            {
                value.Remove(entity);
                if (value.Count == 0)
                {
                    blockmap.Remove(key);
                }
            }
        }

        public static void UpdateEntity(WorldEntity entity)
        {
            GetCoords(entity.GetPreviousPosition(), out int oldX, out int oldY, out int oldZ);
            GetCoords(entity.Position, out int newX, out int newY, out int newZ);

            ulong oldKey = PackKey(oldX, oldY, oldZ);
            ulong newKey = PackKey(newX, newY, newZ);

            if (oldKey == newKey)
            {
                if (blockmap.TryGetValue(newKey, out List<WorldEntity> sameBlock))
                {
                    if (!sameBlock.Contains(entity))
                    {
                        sameBlock.Add(entity);
                    }
                }
                else
                {
                    blockmap.Add(newKey, new List<WorldEntity> { entity });
                }
                return;
            }

            if (blockmap.TryGetValue(oldKey, out List<WorldEntity> oldBlock))
            {
                oldBlock.Remove(entity);
            }

            if (blockmap.TryGetValue(newKey, out List<WorldEntity> newBlock))
            {
                if (!newBlock.Contains(entity))
                {
                    newBlock.Add(entity);
                }
            }
            else
            {
                blockmap.Add(newKey, new List<WorldEntity> { entity });
            }
        }

        private static void EnsureCapacity(ref WorldEntity[] buffer, int needed)
        {
            if (buffer.Length >= needed) return;
            buffer = new WorldEntity[Math.Max(needed, buffer.Length * 2)];
        }

        public static Span<WorldEntity> GetAllAtPoint(Vector3 point, float rad = blockSize)
        {
            return GetAllAtPoint(point, ref scratchBuffer, rad);
        }

        public static Span<WorldEntity> GetAllAtPoint(Vector3 point, ref WorldEntity[] buffer, float rad = blockSize)
        {
            GetCoords(point, out int px, out int py, out int pz);
            int dst = (int)float.Ceiling(rad / blockSize);

            int needed = 0;
            for (int xx = -dst; xx <= dst; xx++)
            {
                for (int yy = -dst; yy <= dst; yy++)
                {
                    for (int zz = -dst; zz <= dst; zz++)
                    {
                        if (blockmap.TryGetValue(PackKey(px + xx, py + yy, pz + zz), out List<WorldEntity> value))
                        {
                            needed += value.Count;
                        }
                    }
                }
            }

            EnsureCapacity(ref buffer, needed);

            int count = 0;
            for (int xx = -dst; xx <= dst; xx++)
            {
                for (int yy = -dst; yy <= dst; yy++)
                {
                    for (int zz = -dst; zz <= dst; zz++)
                    {
                        if (blockmap.TryGetValue(PackKey(px + xx, py + yy, pz + zz), out List<WorldEntity> value) && value.Count > 0)
                        {
                            Span<WorldEntity> src = CollectionsMarshal.AsSpan(value);
                            src.CopyTo(buffer.AsSpan(count));
                            count += src.Length;
                        }
                    }
                }
            }

            return buffer.AsSpan(0, count);
        }
    }
    public static class Collision
    {
        public enum CollideType
        {
            Collide,
            Ignore,
            Trigger
        }
        public static Dictionary<(int a, int b), CollideType> collisionLayers = new Dictionary<(int a, int b), CollideType>();

        [ThreadStatic]
        private static IgnoreSeveralBodiesFilter castPhysicsWorldFilter;
        [ThreadStatic]
        private static IgnoreSeveralBodiesFilter shapeCastBodyFilter;
        [ThreadStatic]
        private static IgnoreSeveralBodiesFilter shapeCastShapeFilter;
        [ThreadStatic]
        private static IgnoreSeveralBodiesFilter shapeCastExpansionFilter;

        public static void CleanUp()
        {
            castPhysicsWorldFilter?.Dispose();
            shapeCastBodyFilter?.Dispose();
            shapeCastShapeFilter?.Dispose();
            shapeCastExpansionFilter?.Dispose();
        }

        public static void AddRule(int a, int b, CollideType t)
        {
            if (!collisionLayers.ContainsKey((a, b)))
            {
                collisionLayers.Add((a, b), t);
            }
        }

        [Obsolete("Use BSPRoot.TraceRay instead, this used to be a shorthand and is no longer necessary.")]
        public static BSPHit CastBSPWorld(ref Ray ray, float maxDistance)
        {
            return BSPRoot.TraceRay(ray,maxDistance);
        }

        public static bool CastPhysicsWorld(Ray ray, float maxDistance, out JoltPhysicsSharp.RayCastResult result, params JoltPhysicsSharp.BodyID[] ignored)
        {
            return CastPhysicsWorld(ray,maxDistance,out result,null,ignored);
        }

        /// <summary>
        /// Shorthand for casting a ray into the physics space.
        /// </summary>
        /// <param name="ray">The ray to traverse</param>
        /// <param name="maxDistance">Maximum distance from the ray origin</param>
        /// <param name="result">Resulting hit information</param>
        public static bool CastPhysicsWorld(Ray ray, float maxDistance, out JoltPhysicsSharp.RayCastResult result, JoltPhysicsSharp.BroadPhaseLayerFilter broadphaseFilter, params JoltPhysicsSharp.BodyID[] ignored)
        {
            var filter = castPhysicsWorldFilter ??= new IgnoreSeveralBodiesFilter();
            filter.SetIgnored(ignored);
            filter.SetRay(ray);

            var joltRay = new JoltPhysicsSharp.Ray(ray.Position.ToNumerics(), (ray.Direction * maxDistance).ToNumerics());

            bool narrow = (PhysicsEngine.PhysicsSystem.NarrowPhaseQuery.CastRay(
                                joltRay,
                                out result,
                                broadPhaseFilter: broadphaseFilter ?? PhysicsFilters.DefaultBroadPhaseRayCastFilter,
                                objectLayerFilter: PhysicsFilters.DefaultObjectLayerRayCastFilter,
                                bodyFilter: filter));

            if (narrow)
            {
                return true;
            }
            return false;
        }
        public static bool CastPhysicsWorld(Ray ray, float maxDistance, out JoltPhysicsSharp.RayCastResult result, JoltPhysicsSharp.BodyID ignore)
        {
            return CastPhysicsWorld(ray, maxDistance, out result, null, ignore);
        }
        public static bool CastPhysicsWorld(Ray ray, float maxDistance, out JoltPhysicsSharp.RayCastResult result, JoltPhysicsSharp.BroadPhaseLayerFilter broadphaseFilter, JoltPhysicsSharp.BodyID ignore)
        {
            var filter = castPhysicsWorldFilter ??= new IgnoreSeveralBodiesFilter();
            filter.SetIgnored(ignore);
            filter.SetRay(ray);

            var joltRay = new JoltPhysicsSharp.Ray(ray.Position.ToNumerics(), (ray.Direction * maxDistance).ToNumerics());

            bool narrow = PhysicsEngine.PhysicsSystem.NarrowPhaseQuery.CastRay(
                                joltRay,
                                out result,
                                broadPhaseFilter: broadphaseFilter ?? PhysicsFilters.DefaultBroadPhaseRayCastFilter,
                                objectLayerFilter: PhysicsFilters.DefaultObjectLayerRayCastFilter,
                                bodyFilter: filter);

            return narrow;
        }

        public static bool ShapeCastPath(JoltPhysicsSharp.Body body, Vector3 to, out JoltPhysicsSharp.ShapeCastResult result, out Vector3 hitPoint, out Vector3 hitNormal, bool ignoreAllEntites, params JoltPhysicsSharp.BodyID[] ignored)
        {
            return ShapeCastPath(body, Vector3.Zero,to,out result,out hitPoint, out hitNormal, ignoreAllEntites,ignored);
        }

        public static bool ShapeCastPath(JoltPhysicsSharp.Body body, Vector3 offset, Vector3 to, out JoltPhysicsSharp.ShapeCastResult result, out Vector3 hitPoint, out Vector3 hitNormal, bool ignoreAllEntites, params JoltPhysicsSharp.BodyID[] ignored)
        {
            var dir = to - body.CenterOfMassPosition;

            var filter = shapeCastBodyFilter ??= new IgnoreSeveralBodiesFilter();
            filter.SetIgnored(ignored);

            var lst = new List<JoltPhysicsSharp.ShapeCastResult>();

            bool narrow = PhysicsEngine.PhysicsSystem.NarrowPhaseQuery.CastShape(body.Shape, body.GetCenterOfMassTransform() * Matrix.CreateTranslation(offset).ToNumerics(), 
                                                                                 dir.ToNumerics(), Vector3.Zero.ToNumerics(),
                                                                                 JoltPhysicsSharp.CollisionCollectorType.ClosestHit,
                                                                                 lst,
                                                                                 broadPhaseFilter: PhysicsFilters.DefaultBroadPhaseRayCastFilter,
                                                                                 objectLayerFilter: PhysicsFilters.DefaultObjectLayerRayCastFilter,
                                                                                 bodyFilter: ignoreAllEntites ? PhysicsFilters.IgnoreEntitiesBodyFilterRayCastFilter : filter);
            result = new JoltPhysicsSharp.ShapeCastResult();
            hitPoint = to;
            hitNormal = Vector3.Zero;
            if (narrow)
            {
                result = lst.First();
                hitPoint = body.CenterOfMassPosition + dir * result.Fraction;
                hitNormal = PhysicsEngine.BodyInterface.GetShape(result.BodyID2).GetSurfaceNormal(result.SubShapeID2,result.ContactPointOn2);

                //MainEngine.DebugDrawPositions.Add(hitPoint);
                return true;
            }
            return false;
        }
        public static bool ShapeCastPath(
            JoltPhysicsSharp.Shape shape,
            Vector3 from,
            Vector3 to,
            out JoltPhysicsSharp.ShapeCastResult result,
            out Vector3 hitPoint,
            out Vector3 hitNormal,
            bool ignoreAllEntities,
            params JoltPhysicsSharp.BodyID[] ignored)
        {
            var dir = to - from;
            var filter = shapeCastShapeFilter ??= new IgnoreSeveralBodiesFilter();
            filter.SetIgnored(ignored);
            var hits = new List<JoltPhysicsSharp.ShapeCastResult>();

            var startTransform = System.Numerics.Matrix4x4.CreateWorld(from.ToNumerics(), System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitY);

            bool hit = PhysicsEngine.PhysicsSystem.NarrowPhaseQuery.CastShape(
                shape,
                startTransform,
                dir.ToNumerics(),
                System.Numerics.Vector3.Zero,
                JoltPhysicsSharp.CollisionCollectorType.ClosestHit,
                hits,
                broadPhaseFilter: PhysicsFilters.DefaultBroadPhaseRayCastFilter,
                objectLayerFilter: PhysicsFilters.DefaultObjectLayerRayCastFilter,
                bodyFilter: ignoreAllEntities
                    ? PhysicsFilters.IgnoreEntitiesBodyFilterRayCastFilter
                    : filter);

            result = new JoltPhysicsSharp.ShapeCastResult();
            hitPoint = to;
            hitNormal = Vector3.Zero;

            if (hit && hits.Count > 0)
            {
                result = hits[0];
                hitPoint = from + dir * result.Fraction;
                hitNormal = PhysicsEngine.BodyInterface
                    .GetShape(result.BodyID2)
                    .GetSurfaceNormal(result.SubShapeID2, result.ContactPointOn2);
                return true;
            }

            return false;
        }
        public static bool ShapeCastPath(
            JoltPhysicsSharp.Body body,
            Vector3 to,
            float expansionFactor,
            out JoltPhysicsSharp.ShapeCastResult result,
            out Vector3 hitPoint,
            out Vector3 hitNormal,
            bool ignoreAllEntities,
            params JoltPhysicsSharp.BodyID[] ignored)
        {
            var dir = to - body.CenterOfMassPosition;
            var filter = shapeCastExpansionFilter ??= new IgnoreSeveralBodiesFilter();
            filter.SetIgnored(ignored);
            var hits = new List<JoltPhysicsSharp.ShapeCastResult>();

            var startTransform = body.GetCenterOfMassTransform();

            JoltPhysicsSharp.Shape castShape = body.Shape;
            JoltPhysicsSharp.ScaledShape? scaled = null;

            if (MathF.Abs(expansionFactor - 1f) > 0.001f)
            {
                scaled = new JoltPhysicsSharp.ScaledShape(
                    body.Shape,
                    System.Numerics.Vector3.One * expansionFactor);
                castShape = scaled;
            }

            bool hit = PhysicsEngine.PhysicsSystem.NarrowPhaseQuery.CastShape(
                castShape,
                startTransform,
                dir.ToNumerics(),
                System.Numerics.Vector3.Zero,                 // baseOffset
                JoltPhysicsSharp.CollisionCollectorType.ClosestHit,
                hits,
                broadPhaseFilter: PhysicsFilters.DefaultBroadPhaseRayCastFilter,
                objectLayerFilter: PhysicsFilters.DefaultObjectLayerRayCastFilter,
                bodyFilter: ignoreAllEntities
                    ? PhysicsFilters.IgnoreEntitiesBodyFilterRayCastFilter
                    : filter);

            // Clean up unmanaged Jolt resources before any return path.
            scaled?.Dispose();

            result = new JoltPhysicsSharp.ShapeCastResult();
            hitPoint = to;
            hitNormal = Vector3.Zero;

            if (hit && hits.Count > 0)
            {
                result = hits[0];

                hitPoint = body.CenterOfMassPosition + dir * result.Fraction;
                hitNormal = PhysicsEngine.BodyInterface
                    .GetShape(result.BodyID2)
                    .GetSurfaceNormal(result.SubShapeID2, result.ContactPointOn2);
                return true;
            }

            return false;
        }
        public static WorldEntity CastEntity(Ray ray, float maxDistance, out JoltPhysicsSharp.RayCastResult result, params JoltPhysicsSharp.BodyID[] ignored)
        {
            WorldEntity found = null;
            
            if (CastPhysicsWorld(ray,maxDistance,out result,null,ignored))
            {
                PhysicsEngine.BodyMapper.TryGetValue(result.BodyID, out found);
            }

            return found;
        }
        public static WorldEntity CastEntity(Ray ray, float maxDistance, out JoltPhysicsSharp.RayCastResult result,
                                                JoltPhysicsSharp.BroadPhaseLayerFilter broad, params JoltPhysicsSharp.BodyID[] ignored)
        {
            WorldEntity found = null;

            if (CastPhysicsWorld(ray, maxDistance, out result, broad, ignored))
            {
                PhysicsEngine.BodyMapper.TryGetValue(result.BodyID, out found);
            }

            return found;
        }
        private static HashSet<WorldEntity> pooledSet = new HashSet<WorldEntity>();
        public static HashSet<WorldEntity> GetEntitiesInSphere(Vector3 center, float radius)
        {
            pooledSet.Clear();
            var entities = Blockmap.GetAllAtPoint(center, radius);
            //var entities = EntityManager.entities.GetValues();
            float sqrRadius = radius * radius;

            foreach(var e in entities)
            {
                var bounds = e.OrientedBounds;
                if (Math.Abs(bounds.Center.X - center.X) > radius + bounds.Extents.X) continue;
                if (Math.Abs(bounds.Center.Y - center.Y) > radius + bounds.Extents.Y) continue;
                if (Math.Abs(bounds.Center.Z - center.Z) > radius + bounds.Extents.Z) continue;

                if (Vector3.DistanceSquared(bounds.Center, center) < sqrRadius)
                    pooledSet.Add(e);
            }

            return pooledSet;
        }
        public static bool CheckBounds(BoundingBox localBounds, Vector3 testPosition, WorldEntity ignoreEntity = null, int ex = -1, bool skipWorld = false, bool skipBrushEntities = false)
        {
            BoundingBox testBounds = new BoundingBox(localBounds.Min + testPosition, localBounds.Max + testPosition);

            if (!skipWorld)
            {
                for (int i = 0; i < GlobalMapData.ActiveMap.Brushes.Length; i++)
                {
                    if (i == ex) continue;
                    if (GlobalMapData.ActiveMap.Brushes[i].IsTrigger) continue;
                    if (skipBrushEntities && GlobalMapData.ActiveMap.Brushes[i].IsEntity) continue;
                    if (CheckBrushBound(testBounds, i)) return true;
                }
            }

            foreach (var other in Blockmap.GetAllAtPoint(testPosition))
            {
                if (other is BrushEntity) continue;
                if (other.IgnoreCollision) continue;
                if (other == ignoreEntity) continue;
                if (Intersects(testBounds, other.Bounds)) return true;
            }
            return false;
        }
        public static bool CheckBounds(WorldEntity entity, int ex = -1, bool skipWorld = false, bool skipBrushEntities = false)
        {
            if(!skipWorld)
            {
                for (int i = 0; i < GlobalMapData.ActiveMap.Brushes.Length; i++)
                {
                    if (i == ex) continue;
                    if (skipBrushEntities && GlobalMapData.ActiveMap.Brushes[i].IsEntity) continue;
                    if (CheckBrushBound(entity, i)) return true;
                }
            }

            var testBounds = GetLiveOrientedBounds(entity);

            foreach (var other in Blockmap.GetAllAtPoint(entity.Position))
            {
                if (other is BrushEntity) continue;
                if (other.IgnoreCollision) continue;
                if (other == entity) continue;
                if (testBounds.Contains(ref other.OrientedBounds) != ContainmentType.Disjoint) return true;
            }
            return false;
        }
        private static OrientedBoundingBox GetLiveOrientedBounds(WorldEntity entity)
        {
            var testBounds = entity.OrientedBounds;
            var b = entity.GetRealBounds();
            var center = (b.Max + b.Min) * 0.5f;

            testBounds.Transformation = entity.AxisAlignedBox
                ? Matrix.CreateScale(entity.WorldScale) * Matrix.CreateTranslation(center) * Matrix.CreateTranslation(entity.WorldPosition)
                : Matrix.CreateScale(entity.WorldScale) * Matrix.CreateTranslation(center) * Matrix.CreateFromQuaternion(entity.WorldRotation) * Matrix.CreateTranslation(entity.WorldPosition);

            return testBounds;
        }
        private static BoundingBox TransformBrushBounds(BoundingBox restBounds, BrushEntity owner)
        {
            var corners = restBounds.GetCorners();

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 worldCorner = owner.WorldPosition + Vector3.Transform(corners[i] - owner.SpawnAnchor, owner.WorldRotation);
                min = Vector3.Min(min, worldCorner);
                max = Vector3.Max(max, worldCorner);
            }

            return new BoundingBox(min, max);
        }

        public static void GatherLeaves(uint nodeID, BoundingBox box, ulong[] leavesOut)
        {
            var node = BSPRoot.Nodes[nodeID];

            if (!node.split)
            {
                Bitset.Set(leavesOut, (int)node.id);
                return;
            }

            Plane splitPlane = node.SplittingPlane;

            Vector3 center = (box.Min + box.Max) * 0.5f;
            Vector3 extents = (box.Max - box.Min) * 0.5f;

            float r = extents.X * MathF.Abs(splitPlane.Normal.X)
                    + extents.Y * MathF.Abs(splitPlane.Normal.Y)
                    + extents.Z * MathF.Abs(splitPlane.Normal.Z);

            float d = splitPlane.DotCoordinate(center);

            if (d > r)
            {
                GatherLeaves(node.front, box, leavesOut);
            }
            else if (d < -r)
            {
                GatherLeaves(node.back, box, leavesOut);
            }
            else
            {
                GatherLeaves(node.front, box, leavesOut);
                GatherLeaves(node.back, box, leavesOut);
            }
        }

        private static bool BrushIntersectsBounds(BoundingBox testBounds, int brushIndex)
        {
            ref readonly var brush = ref GlobalMapData.ActiveMap.Brushes[brushIndex];
            BrushEntity owner = brush.IsEntity ? brush.Entity as BrushEntity : null;

            BoundingBox brushBounds = owner != null
                ? TransformBrushBounds(GlobalMapData.ActiveMap.BrushBounds[brushIndex], owner)
                : GlobalMapData.ActiveMap.BrushBounds[brushIndex];

            if (!Intersects(testBounds, brushBounds))
            {
                return false;
            }

            var verts = brush.Vertices;

            foreach (ref readonly var face in brush.Faces.AsSpan())
            {
                if (face.Indices.Length == 0) continue;

                Vector3 normal = face.Normal;
                Vector3 pointOnFace = verts[face.Indices[0]] + brush.Position;

                if (owner != null)
                {
                    normal = Vector3.Transform(normal, owner.WorldRotation);
                    pointOnFace = owner.WorldPosition + Vector3.Transform(pointOnFace - owner.SpawnAnchor, owner.WorldRotation);
                }

                Plane facePlane = new Plane(pointOnFace, normal);
                if (PlaneIntersectsBox(ref facePlane, ref testBounds) == PlaneIntersectionType.Front)
                {
                    return false;
                }
            }

            return true;
        }
        public static bool CheckBrushBound(WorldEntity entity, int i)
        {
            BoundingBox brush = GlobalMapData.ActiveMap.BrushBounds[i];

            if (GlobalMapData.ActiveMap.Brushes[i].IsTrigger) return false;

            bool intersection = Intersects(entity.Bounds, brush);

            if (!intersection) return false;

            if (GlobalMapData.ActiveMap.Brushes[i].Abnormal && ResolveBrushCollision(entity, i, Vector3.Zero, MainEngine.PreviousFrameDelta, out _).move.LengthSquared() > 0) return true;

            if (intersection && !GlobalMapData.ActiveMap.Brushes[i].Abnormal) return true;

            return false;
        }
        public static bool CheckBrushBound(BoundingBox box, int i)
        {
            BoundingBox brush = GlobalMapData.ActiveMap.BrushBounds[i];

            if (GlobalMapData.ActiveMap.Brushes[i].IsTrigger) return false;
            if (GlobalMapData.ActiveMap.Brushes[i].IsEntity && !GlobalMapData.ActiveMap.Brushes[i].IsDetail) return false;

            bool intersection = Intersects(box, brush);

            if (!intersection) return false;

            if (GlobalMapData.ActiveMap.Brushes[i].Abnormal && ResolveBrushCollision(box, i, out _).move.LengthSquared() > 0) return true;

            if (intersection && !GlobalMapData.ActiveMap.Brushes[i].Abnormal) return true;

            return false;
        }
        public static bool Intersects(BoundingBox a, BoundingBox b)
        {
            bool result;
            if (a.Max.X > b.Min.X && a.Min.X < b.Max.X)
            {
                if (a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y)
                {
                    result = false;
                }
                else
                {
                    result = a.Max.Z > b.Min.Z && a.Min.Z < b.Max.Z;
                }
            }
            else
            {
                result = false;
            }
            return result;
        }

        public static (Vector3 move, Vector3 normal) ResolveTerrainCollision(BoundingBox box, int terrain)
        {
            BoundingBox a = box;

            Vector3 move = Vector3.Zero;
            Vector3 normal = Vector3.Zero;

            if (!GlobalMapData.ActiveMap.Terrains[terrain].Bounds.Intersects(a)) return (move, normal);

            Vector3 boxCenter = (a.Min + a.Max) / 2f;
            Vector3 extents = (a.Max - a.Min) / 2f;

            float minDot = float.MaxValue;

            var tris = GlobalMapData.ActiveMap.Terrains[terrain].Triangles;
            var verts = GlobalMapData.ActiveMap.Terrains[terrain].Vertices;
            for (int j = 0; j < GlobalMapData.ActiveMap.Terrains[terrain].Triangles.Length; j += 3)
            {
                Vector3 v0 = verts[tris[j + 0]].Position;
                Vector3 v1 = verts[tris[j + 1]].Position;
                Vector3 v2 = verts[tris[j + 2]].Position;

                Vector3 edge1 = v1 - v0;
                Vector3 edge2 = v2 - v0;
                Vector3 norm = Vector3.Cross(edge1, edge2);
                norm.Normalize();

                if (!Intersects(v0, v1, v2, a, out float minproj, out Vector3 minaxis))
                    continue;

                float dot = -new Plane(v0, norm).DotCoordinate(boxCenter);

                if (dot < minDot)
                {
                    minDot = dot;
                    move = minproj * norm;
                    normal = norm;
                }
            }

            return (move, normal);
        }
        public static (Vector3 move, Vector3 normal) ResolveTerrainCollision(WorldEntity ent, int terrain, Vector3 velocity, float delta)
        {
            BoundingBox a = ent.Bounds;

            Vector3 move = Vector3.Zero;
            Vector3 normal = Vector3.Zero;

            if (!GlobalMapData.ActiveMap.Terrains[terrain].Bounds.Intersects(a)) return (move, normal);

            Vector3 boxCenter = (a.Min + a.Max) / 2f;
            Vector3 extents = (a.Max - a.Min) / 2f;

            float minDot = float.MaxValue;

            var tris = GlobalMapData.ActiveMap.Terrains[terrain].Triangles;
            var verts = GlobalMapData.ActiveMap.Terrains[terrain].Vertices;
            for (int j = 0; j < GlobalMapData.ActiveMap.Terrains[terrain].Triangles.Length; j += 3)
            {
                Vector3 v0 = verts[tris[j + 0]].Position;
                Vector3 v1 = verts[tris[j + 1]].Position;
                Vector3 v2 = verts[tris[j + 2]].Position;

                // Fast AABB check per‐triangle
                var triBox = new BoundingBox(
                    Vector3.Min(Vector3.Min(v0, v1), v2),
                    Vector3.Max(Vector3.Max(v0, v1), v2)
                );
                if (triBox.Contains(a) == ContainmentType.Disjoint)
                    continue;

                Vector3 edge1 = v1 - v0;
                Vector3 edge2 = v2 - v0;
                Vector3 norm = Vector3.Cross(edge1, edge2);
                norm.Normalize();

                if (!Intersects(v0, v1, v2, a, out float minproj, out Vector3 minaxis))
                    continue;

                float dot = -new Plane(v0, norm).DotCoordinate(boxCenter);

                if (dot < minDot)
                {
                    minDot = dot;
                    move = minproj * norm;
                    normal = norm;
                }
            }

            return (move, normal);
        }
        public static (Vector3 move, Vector3 normal) ResolveBrushCollision(BoundingBox box, int brush, out float depth)
        {
            if (GlobalMapData.ActiveMap.Brushes[brush].Abnormal)
            {
                BoundingBox a = box;
                Vector3 move = Vector3.Zero;
                Vector3 normal = Vector3.Zero;

                Vector3 boxCenter = (a.Min + a.Max) / 2f;
                Vector3 extents = (a.Max - a.Min) / 2f;

                float minDot = float.MaxValue;
                depth = 0;

                for (int i = 0; i < GlobalMapData.ActiveMap.Brushes[brush].Faces.Length; i++)
                {
                    var face = GlobalMapData.ActiveMap.Brushes[brush].Faces[i];

                    for (int j = 0; j < face.Indices.Length; j += 3)
                    {
                        var verts = GlobalMapData.ActiveMap.Brushes[brush].Vertices;
                        Vector3 v0 = verts[face.Indices[j + 0]] + GlobalMapData.ActiveMap.Brushes[brush].Position;
                        Vector3 v1 = verts[face.Indices[j + 1]] + GlobalMapData.ActiveMap.Brushes[brush].Position;
                        Vector3 v2 = verts[face.Indices[j + 2]] + GlobalMapData.ActiveMap.Brushes[brush].Position;

                        if (!Intersects(v0, v1, v2, a, out float minproj, out Vector3 minaxis))
                            continue;

                        float dot = -new Plane(v0, face.Normal).DotCoordinate(boxCenter);

                        if (dot < minDot)
                        {
                            minDot = dot;
                            move = minproj * face.Normal;
                            depth = minproj;
                            normal = face.Normal;
                        }
                    }
                }

                return (move, normal);
            }
            else
            {
                return ResolveCollision(box, GlobalMapData.ActiveMap.BrushBounds[brush], out depth);
            }
        }
        public static (Vector3 move, Vector3 normal) ResolveBrushCollision(WorldEntity ent, int brush, Vector3 velocity, float delta, out float depth)
        {
            if (GlobalMapData.ActiveMap.Brushes[brush].Abnormal)
            {
                BoundingBox a = ent.Bounds;
                Vector3 move = Vector3.Zero;
                Vector3 normal = Vector3.Zero;

                Vector3 boxCenter = (a.Min + a.Max) / 2f;
                Vector3 extents = (a.Max - a.Min) / 2f;

                float minDot = float.MaxValue;
                depth = 0;

                for (int i = 0; i < GlobalMapData.ActiveMap.Brushes[brush].Faces.Length; i++)
                {
                    var face = GlobalMapData.ActiveMap.Brushes[brush].Faces[i];

                    for (int j = 0; j < face.Indices.Length; j += 3)
                    {
                        var verts = GlobalMapData.ActiveMap.Brushes[brush].Vertices;
                        Vector3 v0 = verts[face.Indices[j + 0]] + GlobalMapData.ActiveMap.Brushes[brush].Position;
                        Vector3 v1 = verts[face.Indices[j + 1]] + GlobalMapData.ActiveMap.Brushes[brush].Position;
                        Vector3 v2 = verts[face.Indices[j + 2]] + GlobalMapData.ActiveMap.Brushes[brush].Position;

                        if (!Intersects(v0, v1, v2, a, out float minproj, out Vector3 minaxis))
                            continue;

                        float dot = -new Plane(v0, face.Normal).DotCoordinate(boxCenter);

                        if (dot < minDot)
                        {
                            minDot = dot;
                            move = minproj * face.Normal;
                            depth = minproj;
                            normal = face.Normal;
                        }
                    }
                }

                return (move, normal);
            }
            else
            {
                return ent.AxisAlignedBox ? ResolveCollision(ent.Bounds, GlobalMapData.ActiveMap.BrushBounds[brush], out depth) : ResolveCollision(ent.OrientedBounds, new OrientedBoundingBox(GlobalMapData.ActiveMap.BrushBounds[brush]), out depth);
            }
        }
        private static (Vector3 move, Vector3 normal) ResolveCollision(BoundingBox a, BoundingBox b, out float depth)
        {
            // Calculate the overlap along each axis
            float overlapX = Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X);
            float overlapY = Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y);
            float overlapZ = Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z);
            Vector3 move = new Vector3();
            Vector3 normal;
            depth = 0;
            // Determine which axis has the smallest overlap
            if (overlapX < overlapY && overlapX < overlapZ)
            {
                // Resolve collision along the X-axis
                float direction = (a.Max.X + a.Min.X) / 2 > (b.Max.X + b.Min.X) / 2 ? 1 : -1;
                float penetration = Math.Abs(overlapX);
                move.X = penetration * direction;
                normal = direction * Vector3.UnitX;
                depth = penetration;
            }
            else if (overlapY < overlapX && overlapY < overlapZ)
            {
                // Resolve collision along the Y-axis
                float direction = (a.Max.Y + a.Min.Y) / 2 > (b.Max.Y + b.Min.Y) / 2 ? 1 : -1;
                float penetration = Math.Abs(overlapY);
                move.Y = penetration * direction;
                normal = direction * Vector3.UnitY;
                depth = penetration;
            }
            else
            {
                // Resolve collision along the Z-axis
                float direction = (a.Max.Z + a.Min.Z) / 2 > (b.Max.Z + b.Min.Z) / 2 ? 1 : -1;
                float penetration = Math.Abs(overlapZ);
                move.Z = penetration * direction;
                normal = direction * Vector3.UnitZ;
                depth = penetration;
            }
            return (move, normal);
        }
        private static (Vector3 move, Vector3 normal) ResolveCollision(OrientedBoundingBox a, OrientedBoundingBox b, out float depth)
        {
            a.Overlap(ref b, out var penetration);

            float dot = Vector3.Dot(a.Center - b.Center, penetration.vector);

            depth = 0;

            if (float.IsNaN(dot)) return (Vector3.Zero,Vector3.Zero);

            depth = penetration.penetration;

            return (penetration.vector * penetration.penetration * MathF.Sign(dot), penetration.vector);
        }


        public static bool PointInsideBrush(Vector3 point, int brush)
        {
            for (int i = 0; i < GlobalMapData.ActiveMap.Brushes[brush].Faces.Length; i++)
            {
                if (Vector3.Dot(point - GlobalMapData.ActiveMap.Brushes[brush].Vertices[GlobalMapData.ActiveMap.Brushes[brush].Faces[i].Indices[0]], GlobalMapData.ActiveMap.Brushes[brush].Faces[i].Normal) > 0)
                {
                    return false;
                }
            }
            return true;
        }

        public struct RayHit
        {
            public bool hit;
            public Vector3 position, normal;
            public float distance;
        }
        public static bool Intersects(Vector3 v0, Vector3 v1, Vector3 v2, BoundingBox aabb, out float penetrationDepth, out Vector3 collisionAxis)
        {
            Vector3 c = (aabb.Max + aabb.Min) / 2;
            Vector3 e = (aabb.Max - c);

            v0 -= c;
            v1 -= c;
            v2 -= c;

            Vector3 f0 = v1 - v0;
            Vector3 f1 = v2 - v1;
            Vector3 f2 = v0 - v2;

            Vector3 u0 = Vector3.UnitX;
            Vector3 u1 = Vector3.UnitY;
            Vector3 u2 = Vector3.UnitZ;

            Vector3[] axes = new Vector3[]
            {
                Vector3.Cross(u0, f0), Vector3.Cross(u0, f1), Vector3.Cross(u0, f2),
                Vector3.Cross(u1, f0), Vector3.Cross(u1, f1), Vector3.Cross(u1, f2),
                Vector3.Cross(u2, f0), Vector3.Cross(u2, f1), Vector3.Cross(u2, f2),
                u0, u1, u2, Vector3.Cross(f0, f1)
            };

            penetrationDepth = float.MaxValue;
            collisionAxis = Vector3.Zero;

            foreach (var axis in axes)
            {
                if (axis.LengthSquared() < 1e-6) continue; // Skip near-zero axes
                axis.Normalize();
                if (!TestAxis(axis, v0, v1, v2, e, u0, u1, u2, ref penetrationDepth, ref collisionAxis))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TestAxis(Vector3 axis, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 e, Vector3 u0, Vector3 u1, Vector3 u2, ref float minProj, ref Vector3 minAxis)
        {
            float p0 = Vector3.Dot(v0, axis);
            float p1 = Vector3.Dot(v1, axis);
            float p2 = Vector3.Dot(v2, axis);

            float r = e.X * Math.Abs(Vector3.Dot(u0, axis)) +
                      e.Y * Math.Abs(Vector3.Dot(u1, axis)) +
                      e.Z * Math.Abs(Vector3.Dot(u2, axis));

            float minTri = MathF.Min(p0, MathF.Min(p1, p2));
            float maxTri = MathF.Max(p0, MathF.Max(p1, p2));

            if (minTri > r || maxTri < -r) return false;

            float overlap = MathF.Min(r - minTri, maxTri + r);
            if (overlap < minProj)
            {
                minProj = overlap;
                minAxis = axis;
            }

            return true;
        }
        #region ADVANCED COLLISION FUNCTIONS

        /// <summary>
        /// Determines the closest point between a point and a triangle.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="vertex1">The first vertex to test.</param>
        /// <param name="vertex2">The second vertex to test.</param>
        /// <param name="vertex3">The third vertex to test.</param>
        /// <param name="result">When the method completes, contains the closest point between the two objects.</param>
        public static void ClosestPointPointTriangle(ref Vector3 point, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3, out Vector3 result)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 136

            //Check if P in vertex region outside A
            Vector3 ab = vertex2 - vertex1;
            Vector3 ac = vertex3 - vertex1;
            Vector3 ap = point - vertex1;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0.0f && d2 <= 0.0f)
            {
                result = vertex1; //Barycentric coordinates (1,0,0)
                return;
            }

            //Check if P in vertex region outside B
            Vector3 bp = point - vertex2;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0.0f && d4 <= d3)
            {
                result = vertex2; // Barycentric coordinates (0,1,0)
                return;
            }

            //Check if P in edge region of AB, if so return projection of P onto AB
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f)
            {
                float v = d1 / (d1 - d3);
                result = vertex1 + v * ab; //Barycentric coordinates (1-v,v,0)
                return;
            }

            //Check if P in vertex region outside C
            Vector3 cp = point - vertex3;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0.0f && d5 <= d6)
            {
                result = vertex3; //Barycentric coordinates (0,0,1)
                return;
            }

            //Check if P in edge region of AC, if so return projection of P onto AC
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f)
            {
                float w = d2 / (d2 - d6);
                result = vertex1 + w * ac; //Barycentric coordinates (1-w,0,w)
                return;
            }

            //Check if P in edge region of BC, if so return projection of P onto BC
            float va = d3 * d6 - d5 * d4;
            if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                result = vertex2 + w * (vertex3 - vertex2); //Barycentric coordinates (0,1-w,w)
                return;
            }

            //P inside face region. Compute Q through its Barycentric coordinates (u,v,w)
            float denom = 1.0f / (va + vb + vc);
            float v2 = vb * denom;
            float w2 = vc * denom;
            result = vertex1 + ab * v2 + ac * w2; //= u*vertex1 + v*vertex2 + w*vertex3, u = va * denom = 1.0f - v - w
        }

        /// <summary>
        /// Determines the closest point between a <see cref="Plane"/> and a point.
        /// </summary>
        /// <param name="plane">The plane to test.</param>
        /// <param name="point">The point to test.</param>
        /// <param name="result">When the method completes, contains the closest point between the two objects.</param>
        public static void ClosestPointPlanePoint(ref Plane plane, ref Vector3 point, out Vector3 result)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 126

            float dot;
            Vector3.Dot(ref plane.Normal, ref point, out dot);
            float t = dot - plane.D;

            result = point - (t * plane.Normal);
        }

        /// <summary>
        /// Determines the closest point between a <see cref="BoundingBox"/> and a point.
        /// </summary>
        /// <param name="box">The box to test.</param>
        /// <param name="point">The point to test.</param>
        /// <param name="result">When the method completes, contains the closest point between the two objects.</param>
        public static void ClosestPointBoxPoint(ref BoundingBox box, ref Vector3 point, out Vector3 result)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 130

            Vector3 temp;
            Vector3.Max(ref point, ref box.Min, out temp);
            Vector3.Min(ref temp, ref box.Max, out result);
        }

        /// <summary>
        /// Determines the closest point between a <see cref="BoundingSphere"/> and a point.
        /// </summary>
        /// <param name="sphere"></param>
        /// <param name="point">The point to test.</param>
        /// <param name="result">When the method completes, contains the closest point between the two objects;
        /// or, if the point is directly in the center of the sphere, contains <see cref="Vector3.Zero"/>.</param>
        public static void ClosestPointSpherePoint(ref BoundingSphere sphere, ref Vector3 point, out Vector3 result)
        {
            //Source: Jorgy343
            //Reference: None

            //Get the unit direction from the sphere's center to the point.
            Vector3.Subtract(ref point, ref sphere.Center, out result);
            result.Normalize();

            //Multiply the unit direction by the sphere's radius to get a vector
            //the length of the sphere.
            result *= sphere.Radius;

            //Add the sphere's center to the direction to get a point on the sphere.
            result += sphere.Center;
        }

        /// <summary>
        /// Determines the closest point between a <see cref="BoundingSphere"/> and a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="sphere1">The first sphere to test.</param>
        /// <param name="sphere2">The second sphere to test.</param>
        /// <param name="result">When the method completes, contains the closest point between the two objects;
        /// or, if the point is directly in the center of the sphere, contains <see cref="Vector3.Zero"/>.</param>
        /// <remarks>
        /// If the two spheres are overlapping, but not directly on top of each other, the closest point
        /// is the 'closest' point of intersection. This can also be considered is the deepest point of
        /// intersection.
        /// </remarks>
        public static void ClosestPointSphereSphere(ref BoundingSphere sphere1, ref BoundingSphere sphere2, out Vector3 result)
        {
            //Source: Jorgy343
            //Reference: None

            //Get the unit direction from the first sphere's center to the second sphere's center.
            Vector3.Subtract(ref sphere2.Center, ref sphere1.Center, out result);
            result.Normalize();

            //Multiply the unit direction by the first sphere's radius to get a vector
            //the length of the first sphere.
            result *= sphere1.Radius;

            //Add the first sphere's center to the direction to get a point on the first sphere.
            result += sphere1.Center;
        }

        /// <summary>
        /// Determines the distance between a <see cref="Plane"/> and a point.
        /// </summary>
        /// <param name="plane">The plane to test.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>The distance between the two objects.</returns>
        public static float DistancePlanePoint(ref Plane plane, ref Vector3 point)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 127

            float dot;
            Vector3.Dot(ref plane.Normal, ref point, out dot);
            return dot - plane.D;
        }

        /// <summary>
        /// Determines the distance between a <see cref="BoundingBox"/> and a point.
        /// </summary>
        /// <param name="box">The box to test.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>The distance between the two objects.</returns>
        public static float DistanceBoxPoint(ref BoundingBox box, ref Vector3 point)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 131

            float distance = 0f;

            if (point.X < box.Min.X)
                distance += (box.Min.X - point.X) * (box.Min.X - point.X);
            if (point.X > box.Max.X)
                distance += (point.X - box.Max.X) * (point.X - box.Max.X);

            if (point.Y < box.Min.Y)
                distance += (box.Min.Y - point.Y) * (box.Min.Y - point.Y);
            if (point.Y > box.Max.Y)
                distance += (point.Y - box.Max.Y) * (point.Y - box.Max.Y);

            if (point.Z < box.Min.Z)
                distance += (box.Min.Z - point.Z) * (box.Min.Z - point.Z);
            if (point.Z > box.Max.Z)
                distance += (point.Z - box.Max.Z) * (point.Z - box.Max.Z);

            return (float)Math.Sqrt(distance);
        }

        /// <summary>
        /// Determines the distance between a <see cref="BoundingBox"/> and a <see cref="BoundingBox"/>.
        /// </summary>
        /// <param name="box1">The first box to test.</param>
        /// <param name="box2">The second box to test.</param>
        /// <returns>The distance between the two objects.</returns>
        public static float DistanceBoxBox(ref BoundingBox box1, ref BoundingBox box2)
        {
            //Source:
            //Reference:

            float distance = 0f;

            //Distance for X.
            if (box1.Min.X > box2.Max.X)
            {
                float delta = box2.Max.X - box1.Min.X;
                distance += delta * delta;
            }
            else if (box2.Min.X > box1.Max.X)
            {
                float delta = box1.Max.X - box2.Min.X;
                distance += delta * delta;
            }

            //Distance for Y.
            if (box1.Min.Y > box2.Max.Y)
            {
                float delta = box2.Max.Y - box1.Min.Y;
                distance += delta * delta;
            }
            else if (box2.Min.Y > box1.Max.Y)
            {
                float delta = box1.Max.Y - box2.Min.Y;
                distance += delta * delta;
            }

            //Distance for Z.
            if (box1.Min.Z > box2.Max.Z)
            {
                float delta = box2.Max.Z - box1.Min.Z;
                distance += delta * delta;
            }
            else if (box2.Min.Z > box1.Max.Z)
            {
                float delta = box1.Max.Z - box2.Min.Z;
                distance += delta * delta;
            }

            return (float)Math.Sqrt(distance);
        }

        /// <summary>
        /// Determines the distance between a <see cref="BoundingSphere"/> and a point.
        /// </summary>
        /// <param name="sphere">The sphere to test.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>The distance between the two objects.</returns>
        public static float DistanceSpherePoint(ref BoundingSphere sphere, ref Vector3 point)
        {
            //Source: Jorgy343
            //Reference: None

            float distance;
            Vector3.Distance(ref sphere.Center, ref point, out distance);
            distance -= sphere.Radius;

            return Math.Max(distance, 0f);
        }

        /// <summary>
        /// Determines the distance between a <see cref="BoundingSphere"/> and a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="sphere1">The first sphere to test.</param>
        /// <param name="sphere2">The second sphere to test.</param>
        /// <returns>The distance between the two objects.</returns>
        public static float DistanceSphereSphere(ref BoundingSphere sphere1, ref BoundingSphere sphere2)
        {
            //Source: Jorgy343
            //Reference: None

            float distance;
            Vector3.Distance(ref sphere1.Center, ref sphere2.Center, out distance);
            distance -= sphere1.Radius + sphere2.Radius;

            return Math.Max(distance, 0f);
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a point.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>Whether the two objects intersect.</returns>
        public static bool RayIntersectsPoint(ref Ray ray, ref Vector3 point)
        {
            //Source: RayIntersectsSphere
            //Reference: None

            Vector3 m;
            Vector3.Subtract(ref ray.Position, ref point, out m);

            //Same thing as RayIntersectsSphere except that the radius of the sphere (point)
            //is the epsilon for zero.
            float b = Vector3.Dot(m, ray.Direction);
            float c = Vector3.Dot(m, m) - CMath.ZeroTolerance;

            if (c > 0f && b > 0f)
                return false;

            float discriminant = b * b - c;

            if (discriminant < 0f)
                return false;

            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a <see cref="Ray"/>.
        /// </summary>
        /// <param name="ray1">The first ray to test.</param>
        /// <param name="ray2">The second ray to test.</param>
        /// <param name="point">When the method completes, contains the point of intersection,
        /// or <see cref="Vector3.Zero"/> if there was no intersection.</param>
        /// <returns>Whether the two objects intersect.</returns>
        /// <remarks>
        /// This method performs a ray vs ray intersection test based on the following formula
        /// from Goldman.
        /// <code>s = det([o_2 - o_1, d_2, d_1 x d_2]) / ||d_1 x d_2||^2</code>
        /// <code>t = det([o_2 - o_1, d_1, d_1 x d_2]) / ||d_1 x d_2||^2</code>
        /// Where o_1 is the position of the first ray, o_2 is the position of the second ray,
        /// d_1 is the normalized direction of the first ray, d_2 is the normalized direction
        /// of the second ray, det denotes the determinant of a matrix, x denotes the cross
        /// product, [ ] denotes a matrix, and || || denotes the length or magnitude of a vector.
        /// </remarks>
        public static bool RayIntersectsRay(ref Ray ray1, ref Ray ray2, out Vector3 point)
        {
            //Source: Real-Time Rendering, Third Edition
            //Reference: Page 780

            Vector3 cross;

            Vector3.Cross(ref ray1.Direction, ref ray2.Direction, out cross);
            float denominator = cross.Length();

            //Lines are parallel.
            if (CMath.IsZero(denominator))
            {
                //Lines are parallel and on top of each other.
                if (CMath.NearEqual(ray2.Position.X, ray1.Position.X) &&
                    CMath.NearEqual(ray2.Position.Y, ray1.Position.Y) &&
                    CMath.NearEqual(ray2.Position.Z, ray1.Position.Z))
                {
                    point = Vector3.Zero;
                    return true;
                }
            }

            denominator = denominator * denominator;

            //3x3 matrix for the first ray.
            float m11 = ray2.Position.X - ray1.Position.X;
            float m12 = ray2.Position.Y - ray1.Position.Y;
            float m13 = ray2.Position.Z - ray1.Position.Z;
            float m21 = ray2.Direction.X;
            float m22 = ray2.Direction.Y;
            float m23 = ray2.Direction.Z;
            float m31 = cross.X;
            float m32 = cross.Y;
            float m33 = cross.Z;

            //Determinant of first matrix.
            float dets =
                m11 * m22 * m33 +
                m12 * m23 * m31 +
                m13 * m21 * m32 -
                m11 * m23 * m32 -
                m12 * m21 * m33 -
                m13 * m22 * m31;

            //3x3 matrix for the second ray.
            m21 = ray1.Direction.X;
            m22 = ray1.Direction.Y;
            m23 = ray1.Direction.Z;

            //Determinant of the second matrix.
            float dett =
                m11 * m22 * m33 +
                m12 * m23 * m31 +
                m13 * m21 * m32 -
                m11 * m23 * m32 -
                m12 * m21 * m33 -
                m13 * m22 * m31;

            //t values of the point of intersection.
            float s = dets / denominator;
            float t = dett / denominator;

            //The points of intersection.
            Vector3 point1 = ray1.Position + (s * ray1.Direction);
            Vector3 point2 = ray2.Position + (t * ray2.Direction);

            //If the points are not equal, no intersection has occurred.
            if (!CMath.NearEqual(point2.X, point1.X) ||
                !CMath.NearEqual(point2.Y, point1.Y) ||
                !CMath.NearEqual(point2.Z, point1.Z))
            {
                point = Vector3.Zero;
                return false;
            }

            point = point1;
            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a <see cref="Plane"/>.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="plane">The plane to test.</param>
        /// <param name="distance">When the method completes, contains the distance of the intersection,
        /// or 0 if there was no intersection.</param>
        /// <returns>Whether the two objects intersect.</returns>
        public static bool RayIntersectsPlane(ref Ray ray, ref Plane plane, out float distance)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 175

            float direction;
            Vector3.Dot(ref plane.Normal, ref ray.Direction, out direction);

            if (CMath.IsZero(direction))
            {
                distance = 0f;
                return false;
            }

            float position;
            Vector3.Dot(ref plane.Normal, ref ray.Position, out position);
            distance = (-plane.D - position) / direction;

            if (distance < 0f)
            {
                distance = 0f;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a <see cref="Plane"/>.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="plane">The plane to test</param>
        /// <param name="point">When the method completes, contains the point of intersection,
        /// or <see cref="Vector3.Zero"/> if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool RayIntersectsPlane(ref Ray ray, ref Plane plane, out Vector3 point)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 175

            float distance;
            if (!RayIntersectsPlane(ref ray, ref plane, out distance))
            {
                point = Vector3.Zero;
                return false;
            }

            point = ray.Position + (ray.Direction * distance);
            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a triangle.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="vertex1">The first vertex of the triangle to test.</param>
        /// <param name="vertex2">The second vertex of the triangle to test.</param>
        /// <param name="vertex3">The third vertex of the triangle to test.</param>
        /// <param name="distance">When the method completes, contains the distance of the intersection,
        /// or 0 if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        /// <remarks>
        /// This method tests if the ray intersects either the front or back of the triangle.
        /// If the ray is parallel to the triangle's plane, no intersection is assumed to have
        /// happened. If the intersection of the ray and the triangle is behind the origin of
        /// the ray, no intersection is assumed to have happened. In both cases of assumptions,
        /// this method returns false.
        /// </remarks>
        public static bool RayIntersectsTriangle(ref Ray ray, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3, out float distance)
        {
            //Source: Fast Min Storage Ray / Triangle Intersection
            //Reference: http://www.cs.virginia.edu/~gfx/Courses/2003/ImageSynthesis/papers/Acceleration/Fast%20MinimumStorage%20RayTriangle%20Intersection.pdf

            //Compute vectors along two edges of the triangle.
            Vector3 edge1, edge2;

            //Edge 1
            edge1.X = vertex2.X - vertex1.X;
            edge1.Y = vertex2.Y - vertex1.Y;
            edge1.Z = vertex2.Z - vertex1.Z;

            //Edge2
            edge2.X = vertex3.X - vertex1.X;
            edge2.Y = vertex3.Y - vertex1.Y;
            edge2.Z = vertex3.Z - vertex1.Z;

            //Cross product of ray direction and edge2 - first part of determinant.
            Vector3 directioncrossedge2;
            directioncrossedge2.X = (ray.Direction.Y * edge2.Z) - (ray.Direction.Z * edge2.Y);
            directioncrossedge2.Y = (ray.Direction.Z * edge2.X) - (ray.Direction.X * edge2.Z);
            directioncrossedge2.Z = (ray.Direction.X * edge2.Y) - (ray.Direction.Y * edge2.X);

            //Compute the determinant.
            float determinant;
            //Dot product of edge1 and the first part of determinant.
            determinant = (edge1.X * directioncrossedge2.X) + (edge1.Y * directioncrossedge2.Y) + (edge1.Z * directioncrossedge2.Z);

            //If the ray is parallel to the triangle plane, there is no collision.
            //This also means that we are not culling, the ray may hit both the
            //back and the front of the triangle.
            if (CMath.IsZero(determinant))
            {
                distance = 0f;
                return false;
            }

            float inversedeterminant = 1.0f / determinant;

            //Calculate the U parameter of the intersection point.
            Vector3 distanceVector;
            distanceVector.X = ray.Position.X - vertex1.X;
            distanceVector.Y = ray.Position.Y - vertex1.Y;
            distanceVector.Z = ray.Position.Z - vertex1.Z;

            float triangleU;
            triangleU = (distanceVector.X * directioncrossedge2.X) + (distanceVector.Y * directioncrossedge2.Y) + (distanceVector.Z * directioncrossedge2.Z);
            triangleU *= inversedeterminant;

            //Make sure it is inside the triangle.
            if (triangleU < 0f || triangleU > 1f)
            {
                distance = 0f;
                return false;
            }

            //Calculate the V parameter of the intersection point.
            Vector3 distancecrossedge1;
            distancecrossedge1.X = (distanceVector.Y * edge1.Z) - (distanceVector.Z * edge1.Y);
            distancecrossedge1.Y = (distanceVector.Z * edge1.X) - (distanceVector.X * edge1.Z);
            distancecrossedge1.Z = (distanceVector.X * edge1.Y) - (distanceVector.Y * edge1.X);

            float triangleV;
            triangleV = ((ray.Direction.X * distancecrossedge1.X) + (ray.Direction.Y * distancecrossedge1.Y)) + (ray.Direction.Z * distancecrossedge1.Z);
            triangleV *= inversedeterminant;

            //Make sure it is inside the triangle.
            if (triangleV < 0f || triangleU + triangleV > 1f)
            {
                distance = 0f;
                return false;
            }

            //Compute the distance along the ray to the triangle.
            float raydistance;
            raydistance = (edge2.X * distancecrossedge1.X) + (edge2.Y * distancecrossedge1.Y) + (edge2.Z * distancecrossedge1.Z);
            raydistance *= inversedeterminant;

            //Is the triangle behind the ray origin?
            if (raydistance < 0f)
            {
                distance = 0f;
                return false;
            }

            distance = raydistance;
            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a triangle.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="vertex1">The first vertex of the triangle to test.</param>
        /// <param name="vertex2">The second vertex of the triangle to test.</param>
        /// <param name="vertex3">The third vertex of the triangle to test.</param>
        /// <param name="point">When the method completes, contains the point of intersection,
        /// or <see cref="Vector3.Zero"/> if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool RayIntersectsTriangle(ref Ray ray, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3, out Vector3 point)
        {
            float distance;
            if (!RayIntersectsTriangle(ref ray, ref vertex1, ref vertex2, ref vertex3, out distance))
            {
                point = Vector3.Zero;
                return false;
            }

            point = ray.Position + (ray.Direction * distance);
            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a <see cref="BoundingBox"/>.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="box">The box to test.</param>
        /// <param name="distance">When the method completes, contains the distance of the intersection,
        /// or 0 if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool RayIntersectsBox(ref Ray ray, ref BoundingBox box, out float distance)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 179

            distance = 0f;
            float tmax = float.MaxValue;

            if (CMath.IsZero(ray.Direction.X))
            {
                if (ray.Position.X < box.Min.X || ray.Position.X > box.Max.X)
                {
                    distance = 0f;
                    return false;
                }
            }
            else
            {
                float inverse = 1.0f / ray.Direction.X;
                float t1 = (box.Min.X - ray.Position.X) * inverse;
                float t2 = (box.Max.X - ray.Position.X) * inverse;

                if (t1 > t2)
                {
                    float temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                distance = Math.Max(t1, distance);
                tmax = Math.Min(t2, tmax);

                if (distance > tmax)
                {
                    distance = 0f;
                    return false;
                }
            }

            if (CMath.IsZero(ray.Direction.Y))
            {
                if (ray.Position.Y < box.Min.Y || ray.Position.Y > box.Max.Y)
                {
                    distance = 0f;
                    return false;
                }
            }
            else
            {
                float inverse = 1.0f / ray.Direction.Y;
                float t1 = (box.Min.Y - ray.Position.Y) * inverse;
                float t2 = (box.Max.Y - ray.Position.Y) * inverse;

                if (t1 > t2)
                {
                    float temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                distance = Math.Max(t1, distance);
                tmax = Math.Min(t2, tmax);

                if (distance > tmax)
                {
                    distance = 0f;
                    return false;
                }
            }

            if (CMath.IsZero(ray.Direction.Z))
            {
                if (ray.Position.Z < box.Min.Z || ray.Position.Z > box.Max.Z)
                {
                    distance = 0f;
                    return false;
                }
            }
            else
            {
                float inverse = 1.0f / ray.Direction.Z;
                float t1 = (box.Min.Z - ray.Position.Z) * inverse;
                float t2 = (box.Max.Z - ray.Position.Z) * inverse;

                if (t1 > t2)
                {
                    float temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                distance = Math.Max(t1, distance);
                tmax = Math.Min(t2, tmax);

                if (distance > tmax)
                {
                    distance = 0f;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a <see cref="Plane"/>.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="box">The box to test.</param>
        /// <param name="point">When the method completes, contains the point of intersection,
        /// or <see cref="Vector3.Zero"/> if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool RayIntersectsBox(ref Ray ray, ref BoundingBox box, out Vector3 point)
        {
            float distance;
            if (!RayIntersectsBox(ref ray, ref box, out distance))
            {
                point = Vector3.Zero;
                return false;
            }

            point = ray.Position + (ray.Direction * distance);
            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="sphere">The sphere to test.</param>
        /// <param name="distance">When the method completes, contains the distance of the intersection,
        /// or 0 if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool RayIntersectsSphere(ref Ray ray, ref BoundingSphere sphere, out float distance)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 177

            Vector3 m;
            Vector3.Subtract(ref ray.Position, ref sphere.Center, out m);

            float b = Vector3.Dot(m, ray.Direction);
            float c = Vector3.Dot(m, m) - (sphere.Radius * sphere.Radius);

            if (c > 0f && b > 0f)
            {
                distance = 0f;
                return false;
            }

            float discriminant = b * b - c;

            if (discriminant < 0f)
            {
                distance = 0f;
                return false;
            }

            distance = -b - (float)Math.Sqrt(discriminant);

            if (distance < 0f)
                distance = 0f;

            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Ray"/> and a <see cref="BoundingSphere"/>. 
        /// </summary>
        /// <param name="ray">The ray to test.</param>
        /// <param name="sphere">The sphere to test.</param>
        /// <param name="point">When the method completes, contains the point of intersection,
        /// or <see cref="Vector3.Zero"/> if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool RayIntersectsSphere(ref Ray ray, ref BoundingSphere sphere, out Vector3 point)
        {
            float distance;
            if (!RayIntersectsSphere(ref ray, ref sphere, out distance))
            {
                point = Vector3.Zero;
                return false;
            }

            point = ray.Position + (ray.Direction * distance);
            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Plane"/> and a point.
        /// </summary>
        /// <param name="plane">The plane to test.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static PlaneIntersectionType PlaneIntersectsPoint(ref Plane plane, ref Vector3 point)
        {
            float distance;
            Vector3.Dot(ref plane.Normal, ref point, out distance);
            distance += plane.D;

            if (distance > 0f)
                return PlaneIntersectionType.Front;

            if (distance < 0f)
                return PlaneIntersectionType.Back;

            return PlaneIntersectionType.Intersecting;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Plane"/> and a <see cref="Plane"/>.
        /// </summary>
        /// <param name="plane1">The first plane to test.</param>
        /// <param name="plane2">The second plane to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool PlaneIntersectsPlane(ref Plane plane1, ref Plane plane2)
        {
            Vector3 direction;
            Vector3.Cross(ref plane1.Normal, ref plane2.Normal, out direction);

            //If direction is the zero vector, the planes are parallel and possibly
            //coincident. It is not an intersection. The dot product will tell us.
            float denominator;
            Vector3.Dot(ref direction, ref direction, out denominator);

            if (CMath.IsZero(denominator))
                return false;

            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Plane"/> and a <see cref="Plane"/>.
        /// </summary>
        /// <param name="plane1">The first plane to test.</param>
        /// <param name="plane2">The second plane to test.</param>
        /// <param name="line">When the method completes, contains the line of intersection
        /// as a <see cref="Ray"/>, or a zero ray if there was no intersection.</param>
        /// <returns>Whether the two objects intersected.</returns>
        /// <remarks>
        /// Although a ray is set to have an origin, the ray returned by this method is really
        /// a line in three dimensions which has no real origin. The ray is considered valid when
        /// both the positive direction is used and when the negative direction is used.
        /// </remarks>
        public static bool PlaneIntersectsPlane(ref Plane plane1, ref Plane plane2, out Ray line)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 207

            Vector3 direction;
            Vector3.Cross(ref plane1.Normal, ref plane2.Normal, out direction);

            //If direction is the zero vector, the planes are parallel and possibly
            //coincident. It is not an intersection. The dot product will tell us.
            float denominator;
            Vector3.Dot(ref direction, ref direction, out denominator);

            //We assume the planes are normalized, therefore the denominator
            //only serves as a parallel and coincident check. Otherwise we need
            //to divide the point by the denominator.
            if (CMath.IsZero(denominator))
            {
                line = new Ray();
                return false;
            }

            Vector3 point;
            Vector3 temp = plane1.D * plane2.Normal - plane2.D * plane1.Normal;
            Vector3.Cross(ref temp, ref direction, out point);

            line.Position = point;
            line.Direction = direction;
            line.Direction.Normalize();

            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Plane"/> and a triangle.
        /// </summary>
        /// <param name="plane">The plane to test.</param>
        /// <param name="vertex1">The first vertex of the triangle to test.</param>
        /// <param name="vertex2">The second vertex of the triangle to test.</param>
        /// <param name="vertex3">The third vertex of the triangle to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static PlaneIntersectionType PlaneIntersectsTriangle(ref Plane plane, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 207

            PlaneIntersectionType test1 = PlaneIntersectsPoint(ref plane, ref vertex1);
            PlaneIntersectionType test2 = PlaneIntersectsPoint(ref plane, ref vertex2);
            PlaneIntersectionType test3 = PlaneIntersectsPoint(ref plane, ref vertex3);

            if (test1 == PlaneIntersectionType.Front && test2 == PlaneIntersectionType.Front && test3 == PlaneIntersectionType.Front)
                return PlaneIntersectionType.Front;

            if (test1 == PlaneIntersectionType.Back && test2 == PlaneIntersectionType.Back && test3 == PlaneIntersectionType.Back)
                return PlaneIntersectionType.Back;

            return PlaneIntersectionType.Intersecting;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Plane"/> and a <see cref="BoundingBox"/>.
        /// </summary>
        /// <param name="plane">The plane to test.</param>
        /// <param name="box">The box to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static PlaneIntersectionType PlaneIntersectsBox(ref Plane plane, ref BoundingBox box)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 161

            Vector3 min;
            Vector3 max;

            max.X = (plane.Normal.X >= 0.0f) ? box.Min.X : box.Max.X;
            max.Y = (plane.Normal.Y >= 0.0f) ? box.Min.Y : box.Max.Y;
            max.Z = (plane.Normal.Z >= 0.0f) ? box.Min.Z : box.Max.Z;
            min.X = (plane.Normal.X >= 0.0f) ? box.Max.X : box.Min.X;
            min.Y = (plane.Normal.Y >= 0.0f) ? box.Max.Y : box.Min.Y;
            min.Z = (plane.Normal.Z >= 0.0f) ? box.Max.Z : box.Min.Z;

            float distance;
            Vector3.Dot(ref plane.Normal, ref max, out distance);

            if (distance + plane.D > 0.0f)
                return PlaneIntersectionType.Front;

            distance = Vector3.Dot(plane.Normal, min);

            if (distance + plane.D < 0.0f)
                return PlaneIntersectionType.Back;

            return PlaneIntersectionType.Intersecting;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="Plane"/> and a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="plane">The plane to test.</param>
        /// <param name="sphere">The sphere to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static PlaneIntersectionType PlaneIntersectsSphere(ref Plane plane, ref BoundingSphere sphere)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 160

            float distance;
            Vector3.Dot(ref plane.Normal, ref sphere.Center, out distance);
            distance += plane.D;

            if (distance > sphere.Radius)
                return PlaneIntersectionType.Front;

            if (distance < -sphere.Radius)
                return PlaneIntersectionType.Back;

            return PlaneIntersectionType.Intersecting;
        }

        /* This implementation is wrong
        /// <summary>
        /// Determines whether there is an intersection between a <see cref="SharpDX.BoundingBox"/> and a triangle.
        /// </summary>
        /// <param name="box">The box to test.</param>
        /// <param name="vertex1">The first vertex of the triangle to test.</param>
        /// <param name="vertex2">The second vertex of the triangle to test.</param>
        /// <param name="vertex3">The third vertex of the triangle to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool BoxIntersectsTriangle(ref BoundingBox box, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3)
        {
            if (BoxContainsPoint(ref box, ref vertex1) == ContainmentType.Contains)
                return true;

            if (BoxContainsPoint(ref box, ref vertex2) == ContainmentType.Contains)
                return true;

            if (BoxContainsPoint(ref box, ref vertex3) == ContainmentType.Contains)
                return true;

            return false;
        }
        */

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="BoundingBox"/> and a <see cref="BoundingBox"/>.
        /// </summary>
        /// <param name="box1">The first box to test.</param>
        /// <param name="box2">The second box to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool BoxIntersectsBox(ref BoundingBox box1, ref BoundingBox box2)
        {
            if (box1.Min.X > box2.Max.X || box2.Min.X > box1.Max.X)
                return false;

            if (box1.Min.Y > box2.Max.Y || box2.Min.Y > box1.Max.Y)
                return false;

            if (box1.Min.Z > box2.Max.Z || box2.Min.Z > box1.Max.Z)
                return false;

            return true;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="BoundingBox"/> and a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="box">The box to test.</param>
        /// <param name="sphere">The sphere to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool BoxIntersectsSphere(ref BoundingBox box, ref BoundingSphere sphere)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 166

            Vector3 vector;
            Vector3.Clamp(ref sphere.Center, ref box.Min, ref box.Max, out vector);
            float distance = Vector3.DistanceSquared(sphere.Center, vector);

            return distance <= sphere.Radius * sphere.Radius;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="BoundingSphere"/> and a triangle.
        /// </summary>
        /// <param name="sphere">The sphere to test.</param>
        /// <param name="vertex1">The first vertex of the triangle to test.</param>
        /// <param name="vertex2">The second vertex of the triangle to test.</param>
        /// <param name="vertex3">The third vertex of the triangle to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool SphereIntersectsTriangle(ref BoundingSphere sphere, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3)
        {
            //Source: Real-Time Collision Detection by Christer Ericson
            //Reference: Page 167

            Vector3 point;
            ClosestPointPointTriangle(ref sphere.Center, ref vertex1, ref vertex2, ref vertex3, out point);
            Vector3 v = point - sphere.Center;

            float dot;
            Vector3.Dot(ref v, ref v, out dot);

            return dot <= sphere.Radius * sphere.Radius;
        }

        /// <summary>
        /// Determines whether there is an intersection between a <see cref="BoundingSphere"/> and a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="sphere1">First sphere to test.</param>
        /// <param name="sphere2">Second sphere to test.</param>
        /// <returns>Whether the two objects intersected.</returns>
        public static bool SphereIntersectsSphere(ref BoundingSphere sphere1, ref BoundingSphere sphere2)
        {
            float radiisum = sphere1.Radius + sphere2.Radius;
            return Vector3.DistanceSquared(sphere1.Center, sphere2.Center) <= radiisum * radiisum;
        }

        /// <summary>
        /// Determines whether a <see cref="BoundingBox"/> contains a point.
        /// </summary>
        /// <param name="box">The box to test.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType BoxContainsPoint(ref BoundingBox box, ref Vector3 point)
        {
            if (box.Min.X <= point.X && box.Max.X >= point.X &&
                box.Min.Y <= point.Y && box.Max.Y >= point.Y &&
                box.Min.Z <= point.Z && box.Max.Z >= point.Z)
            {
                return ContainmentType.Contains;
            }

            return ContainmentType.Disjoint;
        }

        /* This implementation is wrong
        /// <summary>
        /// Determines whether a <see cref="SharpDX.BoundingBox"/> contains a triangle.
        /// </summary>
        /// <param name="box">The box to test.</param>
        /// <param name="vertex1">The first vertex of the triangle to test.</param>
        /// <param name="vertex2">The second vertex of the triangle to test.</param>
        /// <param name="vertex3">The third vertex of the triangle to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType BoxContainsTriangle(ref BoundingBox box, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3)
        {
            ContainmentType test1 = BoxContainsPoint(ref box, ref vertex1);
            ContainmentType test2 = BoxContainsPoint(ref box, ref vertex2);
            ContainmentType test3 = BoxContainsPoint(ref box, ref vertex3);

            if (test1 == ContainmentType.Contains && test2 == ContainmentType.Contains && test3 == ContainmentType.Contains)
                return ContainmentType.Contains;

            if (test1 == ContainmentType.Contains || test2 == ContainmentType.Contains || test3 == ContainmentType.Contains)
                return ContainmentType.Intersects;

            return ContainmentType.Disjoint;
        }
        */

        /// <summary>
        /// Determines whether a <see cref="BoundingBox"/> contains a <see cref="BoundingBox"/>.
        /// </summary>
        /// <param name="box1">The first box to test.</param>
        /// <param name="box2">The second box to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType BoxContainsBox(ref BoundingBox box1, ref BoundingBox box2)
        {
            if (box1.Max.X < box2.Min.X || box1.Min.X > box2.Max.X)
                return ContainmentType.Disjoint;

            if (box1.Max.Y < box2.Min.Y || box1.Min.Y > box2.Max.Y)
                return ContainmentType.Disjoint;

            if (box1.Max.Z < box2.Min.Z || box1.Min.Z > box2.Max.Z)
                return ContainmentType.Disjoint;

            if (box1.Min.X <= box2.Min.X && (box2.Max.X <= box1.Max.X &&
                box1.Min.Y <= box2.Min.Y && box2.Max.Y <= box1.Max.Y) &&
                box1.Min.Z <= box2.Min.Z && box2.Max.Z <= box1.Max.Z)
            {
                return ContainmentType.Contains;
            }

            return ContainmentType.Intersects;
        }

        /// <summary>
        /// Determines whether a <see cref="BoundingBox"/> contains a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="box">The box to test.</param>
        /// <param name="sphere">The sphere to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType BoxContainsSphere(ref BoundingBox box, ref BoundingSphere sphere)
        {
            Vector3 vector;
            Vector3.Clamp(ref sphere.Center, ref box.Min, ref box.Max, out vector);
            float distance = Vector3.DistanceSquared(sphere.Center, vector);

            if (distance > sphere.Radius * sphere.Radius)
                return ContainmentType.Disjoint;

            if ((((box.Min.X + sphere.Radius <= sphere.Center.X) && (sphere.Center.X <= box.Max.X - sphere.Radius)) && ((box.Max.X - box.Min.X > sphere.Radius) &&
                (box.Min.Y + sphere.Radius <= sphere.Center.Y))) && (((sphere.Center.Y <= box.Max.Y - sphere.Radius) && (box.Max.Y - box.Min.Y > sphere.Radius)) &&
                (((box.Min.Z + sphere.Radius <= sphere.Center.Z) && (sphere.Center.Z <= box.Max.Z - sphere.Radius)) && (box.Max.Z - box.Min.Z > sphere.Radius))))
            {
                return ContainmentType.Contains;
            }

            return ContainmentType.Intersects;
        }

        /// <summary>
        /// Determines whether a <see cref="BoundingSphere"/> contains a point.
        /// </summary>
        /// <param name="sphere">The sphere to test.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType SphereContainsPoint(ref BoundingSphere sphere, ref Vector3 point)
        {
            if (Vector3.DistanceSquared(point, sphere.Center) <= sphere.Radius * sphere.Radius)
                return ContainmentType.Contains;

            return ContainmentType.Disjoint;
        }

        /// <summary>
        /// Determines whether a <see cref="BoundingSphere"/> contains a triangle.
        /// </summary>
        /// <param name="sphere">The sphere to test.</param>
        /// <param name="vertex1">The first vertex of the triangle to test.</param>
        /// <param name="vertex2">The second vertex of the triangle to test.</param>
        /// <param name="vertex3">The third vertex of the triangle to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType SphereContainsTriangle(ref BoundingSphere sphere, ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3)
        {
            //Source: Jorgy343
            //Reference: None

            ContainmentType test1 = SphereContainsPoint(ref sphere, ref vertex1);
            ContainmentType test2 = SphereContainsPoint(ref sphere, ref vertex2);
            ContainmentType test3 = SphereContainsPoint(ref sphere, ref vertex3);

            if (test1 == ContainmentType.Contains && test2 == ContainmentType.Contains && test3 == ContainmentType.Contains)
                return ContainmentType.Contains;

            if (SphereIntersectsTriangle(ref sphere, ref vertex1, ref vertex2, ref vertex3))
                return ContainmentType.Intersects;

            return ContainmentType.Disjoint;
        }

        /// <summary>
        /// Determines whether a <see cref="BoundingSphere"/> contains a <see cref="BoundingBox"/>.
        /// </summary>
        /// <param name="sphere">The sphere to test.</param>
        /// <param name="box">The box to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType SphereContainsBox(ref BoundingSphere sphere, ref BoundingBox box)
        {
            Vector3 vector;

            if (!BoxIntersectsSphere(ref box, ref sphere))
                return ContainmentType.Disjoint;

            float radiussquared = sphere.Radius * sphere.Radius;
            vector.X = sphere.Center.X - box.Min.X;
            vector.Y = sphere.Center.Y - box.Max.Y;
            vector.Z = sphere.Center.Z - box.Max.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            vector.X = sphere.Center.X - box.Max.X;
            vector.Y = sphere.Center.Y - box.Max.Y;
            vector.Z = sphere.Center.Z - box.Max.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            vector.X = sphere.Center.X - box.Max.X;
            vector.Y = sphere.Center.Y - box.Min.Y;
            vector.Z = sphere.Center.Z - box.Max.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            vector.X = sphere.Center.X - box.Min.X;
            vector.Y = sphere.Center.Y - box.Min.Y;
            vector.Z = sphere.Center.Z - box.Max.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            vector.X = sphere.Center.X - box.Min.X;
            vector.Y = sphere.Center.Y - box.Max.Y;
            vector.Z = sphere.Center.Z - box.Min.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            vector.X = sphere.Center.X - box.Max.X;
            vector.Y = sphere.Center.Y - box.Max.Y;
            vector.Z = sphere.Center.Z - box.Min.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            vector.X = sphere.Center.X - box.Max.X;
            vector.Y = sphere.Center.Y - box.Min.Y;
            vector.Z = sphere.Center.Z - box.Min.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            vector.X = sphere.Center.X - box.Min.X;
            vector.Y = sphere.Center.Y - box.Min.Y;
            vector.Z = sphere.Center.Z - box.Min.Z;

            if (vector.LengthSquared() > radiussquared)
                return ContainmentType.Intersects;

            return ContainmentType.Contains;
        }

        /// <summary>
        /// Determines whether a <see cref="BoundingSphere"/> contains a <see cref="BoundingSphere"/>.
        /// </summary>
        /// <param name="sphere1">The first sphere to test.</param>
        /// <param name="sphere2">The second sphere to test.</param>
        /// <returns>The type of containment the two objects have.</returns>
        public static ContainmentType SphereContainsSphere(ref BoundingSphere sphere1, ref BoundingSphere sphere2)
        {
            float distance = Vector3.Distance(sphere1.Center, sphere2.Center);

            if (sphere1.Radius + sphere2.Radius < distance)
                return ContainmentType.Disjoint;

            if (sphere1.Radius - sphere2.Radius < distance)
                return ContainmentType.Intersects;

            return ContainmentType.Contains;
        }
        #endregion
    }
}
using Engine.Entities;
using Engine.Sound;
using Engine.Utils;
using JoltPhysicsSharp;
using Microsoft.Xna.Framework;
using RenderingLibrary.Graphics;
using Rockwall;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;

namespace Engine.Physics
{
    [Flags]
    public enum PhysicsUserData
    {
        RAYPASS = 1,
        INTERACTABLE = 2,
        FLESH = 4,
        WORLD = 8,
    }
    public static class MeshPreprocessor
    {
        struct GridKey : IEquatable<GridKey>
        {
            public readonly int X, Y, Z;
            public GridKey(int x, int y, int z) { X = x; Y = y; Z = z; }
            public bool Equals(GridKey other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is GridKey g && Equals(g);
            public override int GetHashCode() => ((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791));
        }

        // Welds vertices and reindexes triangles, removes degenerate and duplicate triangles.
        // I didnt want to have to do this, because it removes any way of being able to find 
        // brush and face info from the physics system. Oh well.
        public static void WeldAndReindexTriangles(
            List<Vector3> srcVerts,
            List<int> srcIndices,
            float weldEpsilon,
            out Vector3[] outVerts,
            out IndexedTriangle[] outTris)
        {
            if (srcIndices.Count % 3 != 0) throw new ArgumentException("srcIndices must be multiple of 3");

            // quantization factor
            float invE = 1.0f / weldEpsilon;

            var grid = new Dictionary<GridKey, List<int>>(); // map cell -> list of candidate indices
            var canonicalIndex = new int[srcVerts.Count]; // maps old vert idx -> new vert idx
            for (int i = 0; i < canonicalIndex.Length; i++) canonicalIndex[i] = -1;

            var newVerts = new List<Vector3>();

            // try to find an equivalent vertex already stored (within epsilon)
            int FindOrAdd(int oldIdx)
            {
                if (canonicalIndex[oldIdx] != -1) return canonicalIndex[oldIdx];

                var v = srcVerts[oldIdx];
                int qx = (int)MathF.Round(v.X * invE);
                int qy = (int)MathF.Round(v.Y * invE);
                int qz = (int)MathF.Round(v.Z * invE);
                var key = new GridKey(qx, qy, qz);

                if (!grid.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var candIdx = list[i];
                    var cand = newVerts[candIdx];
                    if (Vector3.DistanceSquared(cand, v) <= weldEpsilon * weldEpsilon)
                    {
                        canonicalIndex[oldIdx] = candIdx;
                        return candIdx;
                    }
                }

                int newIdx = newVerts.Count;
                newVerts.Add(v);
                list.Add(newIdx);
                canonicalIndex[oldIdx] = newIdx;
                return newIdx;
            }

            var triSet = new HashSet<(int, int, int)>();
            var outTrianglesTemp = new List<IndexedTriangle>();

            for (int t = 0; t < srcIndices.Count; t += 3)
            {
                int a = srcIndices[t + 0];
                int b = srcIndices[t + 1];
                int c = srcIndices[t + 2];

                int na = FindOrAdd(a);
                int nb = FindOrAdd(b);
                int nc = FindOrAdd(c);

                if (na == nb || nb == nc || nc == na) continue;

                var va = newVerts[na];
                var vb = newVerts[nb];
                var vc = newVerts[nc];
                var e1 = vb - va;
                var e2 = vc - va;
                var cross = Vector3.Cross(e1, e2);
                float area2 = cross.LengthSquared();
                if (area2 < 1e-10f) continue;

                int[] sorted = new int[] { na, nb, nc };
                Array.Sort(sorted);
                var key = (sorted[0], sorted[1], sorted[2]);
                if (triSet.Contains(key)) continue;
                triSet.Add(key);

                outTrianglesTemp.Add(new IndexedTriangle(na, nb, nc));
            }

            outVerts = newVerts.ToArray();
            outTris = outTrianglesTemp.ToArray();
        }
    }
    public static class PhysicsEngine
    {
        public static CVarFloat GravityPerSecond { get; private set; } = new CVarFloat("p_gravity", -16f);
        public static float PhysicsFrameDelta { get; private set; } = 1f / 120f;

        private const int MaxBodies = 65536;
        private const int MaxBodyPairs = 65536;
        private const int MaxContactConstraints = 65536;
        private const int NumBodyMutexes = 0;
        public static class Layers
        {
            public static readonly ObjectLayer NonMoving = 0;
            public static readonly ObjectLayer Moving = 1;
            public static readonly ObjectLayer Trigger = 2;
            public static readonly ObjectLayer Clip = 3;
            public static readonly ObjectLayer Ragdoll = 4;
            public static readonly ObjectLayer Ignored = 5;
        };
        public static class BroadPhaseLayers
        {
            public static readonly BroadPhaseLayer NonMoving = 0;
            public static readonly BroadPhaseLayer Moving = 1;
            public static readonly BroadPhaseLayer Trigger = 2;
            public static readonly BroadPhaseLayer Clip = 3;
            public static readonly BroadPhaseLayer Ragdoll = 4;
            public static readonly BroadPhaseLayer Ignored = 5;
        };
        private const int NumLayers = 2;
        private static PhysicsSystemSettings settings;

        public static JobSystemThreadPool JobSystem { get; private set; }
        public static PhysicsSystem PhysicsSystem { get; private set; }
        public static BodyInterface BodyInterface => PhysicsSystem.BodyInterface;
        public static BodyLockInterface BodyLockInterface => PhysicsSystem.BodyLockInterface;

        internal static Dictionary<BodyID, WorldEntity> BodyMapper = new();
        internal static Dictionary<BodyID, Shape> ShapeMapper = new();
        internal static ConcurrentDictionary<BodyID, ConcurrentDictionary<(SubShapeID, SubShapeID), (Vector3 normal, Vector3 point, BodyID other)>> ContactInfo = new();
        internal static HashSet<BodyID> IgnoredBodies = new HashSet<BodyID>();
        internal static HashSet<BodyID> AllBodies = new HashSet<BodyID>();
        internal static HashSet<Shape> AllShapes = new HashSet<Shape>();

        internal static ConcurrentQueue<Constraint> ConstraintsToRemove = new ConcurrentQueue<Constraint>();
        internal static ConcurrentQueue<Constraint> ConstraintsToAdd = new ConcurrentQueue<Constraint>();

        internal static Dictionary<BodyID, int> TriggerBodyMap = new();
        internal enum TriggerEventType { Enter, Exit }
        internal record struct TriggerEvent(int BrushIndex, BodyID EntityBodyID, TriggerEventType Type);
        internal static ConcurrentQueue<TriggerEvent> PendingTriggerEvents = new();

        internal static void Init()
        {
            settings = new PhysicsSystemSettings
            {
                MaxBodies = MaxBodies,
                MaxBodyPairs = MaxBodyPairs,
                MaxContactConstraints = MaxContactConstraints,
                NumBodyMutexes = NumBodyMutexes
            };
            JobSystem = new JobSystemThreadPool();

            SetupCollisionFiltering();
            PhysicsSystem = new(settings);
            var s = PhysicsSystem.Settings;
            s.PointVelocitySleepThreshold = 0.1f;
            s.DeterministicSimulation = false;
            PhysicsSystem.Settings = s;

            // ContactListener
            PhysicsSystem.OnContactValidate += OnContactValidate;
            PhysicsSystem.OnContactAdded += OnContactAdded;
            PhysicsSystem.OnContactPersisted += OnContactPersisted;
            PhysicsSystem.OnContactRemoved += OnContactRemoved;

            // BodyActivationListener
            PhysicsSystem.OnBodyActivated += OnBodyActivated;
            PhysicsSystem.OnBodyDeactivated += OnBodyDeactivated;

            PhysicsFilters.DefaultObjectLayerRayCastFilter = new DefaultObjectLayerRayCastFilter(false);
            PhysicsFilters.DefaultObjectLayerRayCastFilterIgnoreClip = new DefaultObjectLayerRayCastFilter(true);
            PhysicsFilters.DefaultBodyFilterRayCastFilter = new DefaultBodyFilterRayCastFilter();
            PhysicsFilters.DefaultBroadPhaseRayCastFilter = new DefaultBroadPhaseRayCastFilter(false);
            PhysicsFilters.DefaultBroadPhaseRayCastFilterIgnoreClip = new DefaultBroadPhaseRayCastFilter(true);
            PhysicsFilters.IgnoreEntitiesBodyFilterRayCastFilter = new IgnoreEntitiesBodyFilterRayCastFilter();
            PhysicsFilters.IgnoreRagdollBroadPhaseRayCastFilter = new IgnoreRagdollBroadPhaseRayCastFilter();
        }
        internal static void Shutdown()
        {
            Clear();
            JobSystem.Dispose();
            PhysicsSystem.Dispose();
        }
        internal static void Clear()
        {
            foreach (var body in AllBodies)
            {
                BodyInterface.RemoveAndDestroyBody(body);
            }

            foreach(var shape in AllShapes)
            {
                shape?.Dispose();
            }

            ContactInfo.Clear();

            AllShapes.Clear();
            AllBodies.Clear();
            BodyMapper.Clear();
            ShapeMapper.Clear();
            IgnoredBodies.Clear();
            TriggerBodyMap.Clear();
        }
        private static void SetupCollisionFiltering()
        {
            ObjectLayerPairFilterTable objectLayerPairFilter = new(5);
            objectLayerPairFilter.EnableCollision(Layers.NonMoving, Layers.Moving);

            objectLayerPairFilter.EnableCollision(Layers.NonMoving, Layers.Ragdoll);
            objectLayerPairFilter.EnableCollision(Layers.Ragdoll, Layers.Ragdoll);
            objectLayerPairFilter.EnableCollision(Layers.Moving, Layers.Ragdoll);

            objectLayerPairFilter.EnableCollision(Layers.Trigger, Layers.Moving);
            objectLayerPairFilter.EnableCollision(Layers.Clip, Layers.Moving);
            objectLayerPairFilter.EnableCollision(Layers.Moving, Layers.Moving);

            objectLayerPairFilter.EnableCollision(Layers.Ignored, Layers.Ignored);

            BroadPhaseLayerInterfaceTable broadPhaseLayerInterface = new(5, 5);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(Layers.NonMoving, BroadPhaseLayers.NonMoving);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(Layers.Moving, BroadPhaseLayers.Moving);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(Layers.Trigger, BroadPhaseLayers.Trigger);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(Layers.Clip, BroadPhaseLayers.Clip);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(Layers.Ragdoll, BroadPhaseLayers.Ragdoll);
            broadPhaseLayerInterface.MapObjectToBroadPhaseLayer(Layers.Ignored, BroadPhaseLayers.Ignored);

            ObjectVsBroadPhaseLayerFilterTable objectVsBroadPhaseLayerFilter = new(broadPhaseLayerInterface, 5, objectLayerPairFilter, 5);

            settings.ObjectLayerPairFilter = objectLayerPairFilter;
            settings.BroadPhaseLayerInterface = broadPhaseLayerInterface;
            settings.ObjectVsBroadPhaseLayerFilter = objectVsBroadPhaseLayerFilter;
        }
        private static uint GetBrushUserID(int brush)
        {
            return (uint)brush;
        }
        /// <summary>
        /// Builds one compound collision shape out of the given brush indices, one convex hull per
        /// brush, each positioned relative to <paramref name="anchor"/> (the entity's SpawnAnchor)
        /// so the compound lines up correctly under the owning body's own position/rotation. Returns
        /// null if there's no usable geometry (empty set, or every brush too degenerate to hull),
        /// letting the caller fall back to a generic shape rather than crashing.
        /// </summary>
        public static Shape BuildBrushSetShape(int[] brushSet, Vector3 anchor, float densityMultiplier)
        {
            if (brushSet == null || brushSet.Length == 0) return null;

            var brushes = GlobalMapData.ActiveMap.Brushes;
            if (brushes == null) return null;

            using var compoundSettings = new StaticCompoundShapeSettings();
            int added = 0;

            foreach (var brushIdx in brushSet)
            {
                if (brushIdx < 0 || brushIdx >= brushes.Length) continue;

                var brush = brushes[brushIdx];
                if (brush.Faces == null || brush.Vertices == null) continue;

                var hullVerts = brush.Faces
                    .SelectMany(f => f.Indices)
                    .Distinct()
                    .Select(idx => (brush.Vertices[idx] + brush.Position - anchor).ToNumerics())
                    .ToArray();

                // A valid convex hull needs at least 4 non-coplanar points; anything less is
                // degenerate (e.g. a brush with no drawn geometry) and would make Jolt unhappy.
                if (hullVerts.Length < 4) continue;

                using var hullSettings = new ConvexHullShapeSettings(hullVerts.AsSpan());
                var hullShape = (ConvexHullShape)hullSettings.Create();
                hullShape.Density *= densityMultiplier;

                compoundSettings.AddShape(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, hullShape);
                added++;
            }

            return added > 0 ? compoundSettings.Create() : null;
        }
        public static void OnMapLoaded()
        {
            if (GlobalMapData.ActiveMap.Brushes == null) return;

            var rawVerts = new List<Vector3>();
            var rawIndices = new List<int>();

            for (int i = 0; i < GlobalMapData.ActiveMap.Brushes.Length; i++)
            {
                var brush = GlobalMapData.ActiveMap.Brushes[i];

                if (brush.IsTrigger)
                {
                    // brushes are convex by definition, so we can always use a ConvexHullShape.
                    // Collect every unique vertex across all faces of this brush.
                    var hullVerts = brush.Faces
                        .SelectMany(f => f.Indices)
                        .Select(idx => brush.Vertices[idx] + brush.Position)
                        .Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z))
                        .ToArray();

                    using var hullSettings = new ConvexHullShapeSettings(hullVerts.AsSpan());
                    var shape = hullSettings.Create();
                    AllShapes.Add(shape);

                    using var tbodySettings = new BodyCreationSettings(
                        shape,
                        System.Numerics.Vector3.Zero,
                        System.Numerics.Quaternion.Identity,
                        MotionType.Static,
                        Layers.Trigger);

                    tbodySettings.IsSensor = true;
                    tbodySettings.UserData = GetBrushUserID(i) + 256;

                    var bodyID = BodyInterface.CreateAndAddBody(tbodySettings, Activation.Activate);
                    AllBodies.Add(bodyID);
                    ShapeMapper.Add(bodyID, shape);
                    TriggerBodyMap.Add(bodyID, i);
                    continue;
                }
                else if (brush.IsClip)
                {
                    var hullVerts = brush.Faces
                        .SelectMany(f => f.Indices)
                        .Distinct()
                        .Select(idx => brush.Vertices[idx] + brush.Position)
                        .Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z))
                        .ToArray();

                    if (hullVerts.Length < 4) continue;

                    using var hullSettings = new ConvexHullShapeSettings(hullVerts.AsSpan());
                    var shape = hullSettings.Create();
                    AllShapes.Add(shape);

                    using var cbodySettings = new BodyCreationSettings(
                        shape,
                        System.Numerics.Vector3.Zero,
                        System.Numerics.Quaternion.Identity,
                        MotionType.Static,
                        Layers.Clip);

                    cbodySettings.UserData = GetBrushUserID(i) + 256;

                    var bodyID = BodyInterface.CreateAndAddBody(cbodySettings, Activation.Activate);
                    AllBodies.Add(bodyID);
                    ShapeMapper.Add(bodyID, shape);
                    continue;
                }
                if (brush.IsEntity && !brush.IsDetail) continue;

                foreach (var face in brush.Faces)
                {
                    for (int k = 0; k < face.Indices.Length; k += 3)
                    {
                        var v0 = brush.Vertices[face.Indices[k + 0]] + brush.Position;
                        var v1 = brush.Vertices[face.Indices[k + 1]] + brush.Position;
                        var v2 = brush.Vertices[face.Indices[k + 2]] + brush.Position;

                        rawVerts.Add(new Vector3(v0.X, v0.Y, v0.Z));
                        rawVerts.Add(new Vector3(v1.X, v1.Y, v1.Z));
                        rawVerts.Add(new Vector3(v2.X, v2.Y, v2.Z));

                        int baseIndex = rawVerts.Count - 3;
                        rawIndices.Add(baseIndex + 0);
                        rawIndices.Add(baseIndex + 1);
                        rawIndices.Add(baseIndex + 2);
                    }
                }
            }
            if(GlobalMapData.ActiveMap.Terrains != null)
            {
                for (int i = 0; i < GlobalMapData.ActiveMap.Terrains.Length; i++)
                {
                    var terrain = GlobalMapData.ActiveMap.Terrains[i];

                    var baseIndex = rawVerts.Count;

                    rawVerts.AddRange(terrain.Vertices.Select(i => i.Position));
                    rawIndices.AddRange(terrain.Triangles.Select(i => (int)i + baseIndex));
                }
            }

            MeshPreprocessor.WeldAndReindexTriangles(rawVerts, rawIndices, weldEpsilon: 0.001f, out var meshVerts, out var meshTris);

            var meshSettings = new MeshShapeSettings(meshVerts.Select(v=>v.ToNumerics()).ToArray().AsSpan(), meshTris.AsSpan());

            meshSettings.ActiveEdgeCosThresholdAngle = MathF.Cos(MathHelper.ToRadians(50f));

            var meshShape = meshSettings.Create();


            using var bodySettings = new JoltPhysicsSharp.BodyCreationSettings(
                meshShape,
                System.Numerics.Vector3.Zero,
                Quaternion.Identity.ToNumerics(),
                MotionType.Static,
                PhysicsEngine.Layers.NonMoving);

            bodySettings.EnhancedInternalEdgeRemoval = true;
            bodySettings.UserData = (ulong)PhysicsUserData.WORLD;

            AllBodies.Add(BodyInterface.CreateAndAddBody(bodySettings, Activation.Activate));
            AllShapes.Add(meshShape);
        }
        public static Body GetFromBodyID(BodyID bodyID)
        {
            if (!bodyID.IsValid) return null;

            BodyLockInterface.LockRead(bodyID, out var bLock);

            try
            {
                if (bLock.Succeeded)
                {
                    return bLock.Body;
                }
                return null;
            }
            finally
            {
                BodyLockInterface.UnlockRead(bLock);
            }
        }
        public static void Update()
        {
            if (MainEngine.PreviousFrameDelta == 0) return;
            if (MainEngine.Instance.IsLoading) return;

            PhysicsSystem.Gravity = System.Numerics.Vector3.UnitY * GravityPerSecond;

            const int collisionSteps = 2;

            PhysicsUpdateError error = PhysicsSystem.Update(float.Min(MainEngine.PreviousFrameDelta,1/20f), collisionSteps, JobSystem);
            Debug.Assert(error == PhysicsUpdateError.None);

            while (PendingTriggerEvents.TryDequeue(out var evt))
            {
                var brushes = GlobalMapData.ActiveMap.Brushes;
                if (brushes == null || evt.BrushIndex >= brushes.Length) continue;

                var brush = brushes[evt.BrushIndex];
                if (brush.Entity is not BrushEntity brushEnt) continue;
                if (!BodyMapper.TryGetValue(evt.EntityBodyID, out var entity)) continue;
                if (entity is BrushEntity) continue;
                if (entity.IsDespawned) continue;

                if (evt.Type == TriggerEventType.Enter)
                {
                    if (!brushEnt.insideBrush.Contains(entity))
                    {
                        brushEnt.insideBrush.Add(entity);
                        brushEnt.OnEntityEnter(entity);
                    }
                }
                else
                {
                    if (brushEnt.insideBrush.Remove(entity))
                    {
                        brushEnt.OnEntityExit(entity);
                    }
                }
            }

            while (ConstraintsToAdd.Count > 0)
            {
                if (ConstraintsToAdd.TryDequeue(out var c))
                {
                    PhysicsSystem.AddConstraint(c);
                }
            }
            while (ConstraintsToRemove.Count > 0)
            {
                if (ConstraintsToRemove.TryDequeue(out var c))
                    PhysicsSystem.RemoveConstraint(c);
            }
        }
        static ValidateResult OnContactValidate(PhysicsSystem system, in Body body1, in Body body2, RVector3 baseOffset, in CollideShapeResult collisionResult)
        {
            if (MainEngine.Instance.IsLoading) return ValidateResult.AcceptAllContactsForThisBodyPair;
            //MainEngine.Instance.console.AppendLog("Contact validate callback");

            if (IgnoredBodies.Contains(body1.ID)) return ValidateResult.RejectContact;
            if (IgnoredBodies.Contains(body2.ID)) return ValidateResult.RejectContact;

            bool ent1 = BodyMapper.TryGetValue(body1.ID, out var entA);
            bool ent2 = BodyMapper.TryGetValue(body2.ID, out var entB);

            if (ent1 && (entA.IgnoreCollision || entA.IsDespawned))
                return ValidateResult.RejectContact;
            if (ent2 && (entB.IgnoreCollision || entB.IsDespawned))
                return ValidateResult.RejectContact;

            if (TriggerBodyMap.ContainsKey(body1.ID) || TriggerBodyMap.ContainsKey(body2.ID))
                return ValidateResult.AcceptAllContactsForThisBodyPair;

            if (ent1 && entA is BrushEntity brush1 && brush1.ignoreWorldCollision && !ent2)
                return body2.ObjectLayer == Layers.Ragdoll ? ValidateResult.AcceptContact : ValidateResult.RejectContact;
            if (ent2 && entB is BrushEntity brush2 && brush2.ignoreWorldCollision && !ent1)
                return body1.ObjectLayer == Layers.Ragdoll ? ValidateResult.AcceptContact : ValidateResult.RejectContact;

            if (ent1 && entA.IgnoreRagdolls && !ent2 && body2.ObjectLayer == Layers.Ragdoll)
                return ValidateResult.RejectContact;
            if (ent2 && entB.IgnoreRagdolls && !ent1 && body1.ObjectLayer == Layers.Ragdoll)
                return ValidateResult.RejectContact;
            if (ent1 && ent2)
            {
                if (entA.IgnoredEntities.Contains(entB) || entB.IgnoredEntities.Contains(entA))
                    return ValidateResult.RejectContact;

                if (Collision.collisionLayers.TryGetValue((entA.CollisionID, entB.CollisionID), out var collideType) ||
                    Collision.collisionLayers.TryGetValue((entB.CollisionID, entA.CollisionID), out collideType))
                {
                    switch (collideType)
                    {
                        default:
                            entA.Controller?.OnCollideWithEntity(entB);
                            entB.Controller?.OnCollideWithEntity(entA);

                            return ValidateResult.AcceptAllContactsForThisBodyPair;
                        case Collision.CollideType.Trigger:

                            entA.Controller?.OnTriggerCollideWithEntity(entB);
                            entB.Controller?.OnTriggerCollideWithEntity(entA);

                            return ValidateResult.RejectContact;
                        case Collision.CollideType.Ignore:
                            return ValidateResult.RejectAllContactsForThisBodyPair;
                    }
                }
                else // just a normal collision
                {
                    entA.Controller?.OnCollideWithEntity(entB);
                    entB.Controller?.OnCollideWithEntity(entA);
                }
            }
            if (ent1)
                entA.Controller?.OnCollision(entA.GetPreviousVelocity());
            if (ent2)
                entB.Controller?.OnCollision(entB.GetPreviousVelocity());

            if (ent1 && !ent2)
            {
                return CheckContactValidity(entA, body2, collisionResult.ContactPointOn2, collisionResult);
            }
            if (ent2 && !ent1)
            {
                return CheckContactValidity(entB, body1, collisionResult.ContactPointOn1, collisionResult);
            }

            // Allows you to ignore a contact before it is created (using layers to not make objects collide is cheaper!)
            return ValidateResult.AcceptAllContactsForThisBodyPair;
        }
        static ValidateResult CheckContactValidity(WorldEntity entity, Body bodyB, Vector3 point, CollideShapeResult collide)
        {
            if (MainEngine.Instance.IsLoading)
                return ValidateResult.AcceptAllContactsForThisBodyPair;

            //float penetrationDepth = collide.PenetrationDepth;
            //Vector3 penetrationNormal = Vector3.Normalize(collide.PenetrationAxis);
            //Vector3 entityCenter = (entity.bounds.Min + entity.bounds.Max) * 0.5f;

            var i = bodyB.GetUserData();

            if (i >= 256) i -= 256;
            else return ValidateResult.AcceptAllContactsForThisBodyPair;

            if (GlobalMapData.ActiveMap.Brushes[i].IsTrigger)
            {
                if (GlobalMapData.ActiveMap.Brushes[i].Entity != null && !GlobalMapData.ActiveMap.Brushes[i].Entity.insideBrush.Contains(entity))
                {
                    GlobalMapData.ActiveMap.Brushes[i].Entity.insideBrush.Add(entity);
                    GlobalMapData.ActiveMap.Brushes[i].Entity.OnEntityEnter(entity);
                }
                return ValidateResult.RejectContact;
            }

            return ValidateResult.AcceptAllContactsForThisBodyPair;
        }
        static void OnContactAdded(PhysicsSystem system, in Body body1, in Body body2, in ContactManifold manifold, in ContactSettings settings)
        {
            if (MainEngine.Instance.IsLoading) return;

            TryEnqueueTriggerEvent(body1.ID, body2.ID, TriggerEventType.Enter);
            TryEnqueueTriggerEvent(body2.ID, body1.ID, TriggerEventType.Enter);

            if (!TriggerBodyMap.ContainsKey(body1.ID) && !TriggerBodyMap.ContainsKey(body2.ID))
            {
                for (uint i = 0; i < uint.Min(manifold.PointCount, 4); i++)
                {
                    TryPlayPhysicsSound(body1, manifold.GetWorldSpaceContactPointOn1(i).ToXNA());
                    TryPlayPhysicsSound(body2, manifold.GetWorldSpaceContactPointOn2(i).ToXNA());
                }
                TrackContact(body1.ID, manifold, true, body2.ID);
                TrackContact(body2.ID, manifold, false, body1.ID);
            }
        }

        static void TryEnqueueTriggerEvent(BodyID triggerCandidate, BodyID entityCandidate, TriggerEventType type)
        {
            if (!TriggerBodyMap.TryGetValue(triggerCandidate, out int brushIdx)) return;
            if (!BodyMapper.ContainsKey(entityCandidate)) return;

            PendingTriggerEvents.Enqueue(new TriggerEvent(brushIdx, entityCandidate, type));
        }
        static void TrackContact(BodyID body, ContactManifold manifold, bool isOne, BodyID other)
        {
            if (MainEngine.Instance.IsLoading) return;

            var key = (manifold.SubShapeID1, manifold.SubShapeID2);
            var value = (
                manifold.WorldSpaceNormal.ToXNA(),
                isOne ? manifold.GetWorldSpaceContactPointOn1(0).ToXNA()
                      : manifold.GetWorldSpaceContactPointOn2(0).ToXNA(),
                other
            );

            var contacts = ContactInfo.GetOrAdd(body, _ => new());
            contacts[key] = value;
        }
        static void UntrackContact(BodyID body, SubShapeIDPair pair)
        {
            if (MainEngine.Instance.IsLoading) return;

            if (ContactInfo.TryGetValue(body, out var contacts))
                contacts.Remove((pair.SubShapeID1, pair.SubShapeID2), out _);
        }
        static void TryPlayPhysicsSound(Body body, Vector3 pos)
        {
            if(BodyMapper.TryGetValue(body.ID,out var entity))
            {
                PhysicsSounds.QueueImpactSound(entity.PhysicsType, body.ID, pos, body.GetPointVelocity(pos.ToNumerics()));
                entity.Controller?.OnContactCreated(pos);
            }
            else
            {
                if((body.GetUserData() & (ulong)PhysicsUserData.FLESH) != 0)
                {
                    PhysicsSounds.QueueImpactSound("flesh", body.ID, pos, body.GetPointVelocity(pos.ToNumerics())*0.15f);
                }
            }
        }

        static void OnContactPersisted(PhysicsSystem system, in Body body1, in Body body2, in ContactManifold manifold, in ContactSettings settings)
        {
            TrackContact(body1.ID, manifold, true, body2.ID);
            TrackContact(body2.ID, manifold, false, body1.ID);
        }

        static void OnContactRemoved(PhysicsSystem system, ref SubShapeIDPair subShapePair)
        {
            UntrackContact(subShapePair.Body1ID, subShapePair);
            UntrackContact(subShapePair.Body2ID, subShapePair);

            TryEnqueueTriggerEvent(subShapePair.Body1ID, subShapePair.Body2ID, TriggerEventType.Exit);
            TryEnqueueTriggerEvent(subShapePair.Body2ID, subShapePair.Body1ID, TriggerEventType.Exit);
        }

        static void OnBodyActivated(PhysicsSystem system, in BodyID bodyID, ulong bodyUserData)
        {
            //MainEngine.Instance.console.AppendLog("A body got activated");
        }

        static void OnBodyDeactivated(PhysicsSystem system, in BodyID bodyID, ulong bodyUserData)
        {
            //MainEngine.Instance.console.AppendLog("A body went to sleep");
        }

        internal static bool ShouldCollideBody(BodyID id)
        {
            if (!BodyMapper.TryGetValue(id, out var ent)) return true;

            return !ent.IgnoreCollision && !ent.IsDespawned;
        }
    }
    public static class PhysicsFilters
    {
        public static DefaultBroadPhaseRayCastFilter DefaultBroadPhaseRayCastFilter;
        public static DefaultBroadPhaseRayCastFilter DefaultBroadPhaseRayCastFilterIgnoreClip;
        public static DefaultObjectLayerRayCastFilter DefaultObjectLayerRayCastFilter;
        public static DefaultObjectLayerRayCastFilter DefaultObjectLayerRayCastFilterIgnoreClip;
        public static DefaultBodyFilterRayCastFilter DefaultBodyFilterRayCastFilter;
        public static IgnoreEntitiesBodyFilterRayCastFilter IgnoreEntitiesBodyFilterRayCastFilter;
        public static IgnoreRagdollBroadPhaseRayCastFilter IgnoreRagdollBroadPhaseRayCastFilter;
    }
    public class IgnoreSingleBodyFilter : BodyFilter
    {
        BodyID ignore;
        public IgnoreSingleBodyFilter(BodyID ignore)
        {
            this.ignore = ignore;
        }

        protected override bool ShouldCollide(BodyID bodyID)
        {
            return bodyID != ignore && !PhysicsEngine.IgnoredBodies.Contains(bodyID);
        }

        protected override bool ShouldCollideLocked(Body body)
        {
            return body.ID != ignore && !PhysicsEngine.IgnoredBodies.Contains(body.ID);
        }
    }
    public class IgnoreSeveralBodiesFilter : BodyFilter
    {
        private const int MaxIgnored = 16;

        private readonly BodyID[] ignoreBuffer = new BodyID[MaxIgnored];
        private int ignoreCount;

        private Microsoft.Xna.Framework.Ray ray;
        private bool checkRayCollide;

        public void SetIgnored(ReadOnlySpan<BodyID> ignored)
        {
            ignoreCount = Math.Min(ignored.Length, MaxIgnored);
            for (int i = 0; i < ignoreCount; i++)
            {
                ignoreBuffer[i] = ignored[i];
            }
        }
        public void SetIgnored(JoltPhysicsSharp.BodyID single)
        {
            ignoreCount = 1;
            ignoreBuffer[0] = single;
        }

        public void SetRay(Microsoft.Xna.Framework.Ray ray)
        {
            this.ray = ray;
            checkRayCollide = true;
        }
        public void ClearRay()
        {
            checkRayCollide = false;
        }

        private bool IsIgnored(BodyID bodyID)
        {
            for (int i = 0; i < ignoreCount; i++)
            {
                if (ignoreBuffer[i].Equals(bodyID)) return true;
            }
            return false;
        }

        private bool PassesControllerCheck(BodyID bodyID)
        {
            if (!checkRayCollide) return true;
            if (!PhysicsEngine.BodyMapper.TryGetValue(bodyID, out var entity)) return true;
            if (entity.Controller == null) return true;

            return entity.Controller.ShouldRayCollide(ray);
        }

        protected override bool ShouldCollide(BodyID bodyID)
        {
            return !IsIgnored(bodyID) && !PhysicsEngine.IgnoredBodies.Contains(bodyID) && PhysicsEngine.ShouldCollideBody(bodyID) && PassesControllerCheck(bodyID);
        }

        protected override bool ShouldCollideLocked(Body body)
        {
            return !IsIgnored(body.ID) && !PhysicsEngine.IgnoredBodies.Contains(body.ID) && PhysicsEngine.ShouldCollideBody(body.ID) && PassesControllerCheck(body.ID);
        }
    }
    public class IgnoreRagdollBroadPhaseRayCastFilter : BroadPhaseLayerFilter
    {
        protected override bool ShouldCollide(BroadPhaseLayer layer)
        {
            return layer != PhysicsEngine.BroadPhaseLayers.Trigger && 
                layer != PhysicsEngine.BroadPhaseLayers.Ignored && 
                layer != PhysicsEngine.BroadPhaseLayers.Ragdoll;
        }
    }
    public class DefaultBroadPhaseRayCastFilter(bool clip) : BroadPhaseLayerFilter
    {
        protected override bool ShouldCollide(BroadPhaseLayer layer)
        {
            return layer != PhysicsEngine.BroadPhaseLayers.Trigger && 
                layer != PhysicsEngine.BroadPhaseLayers.Ignored && (layer != PhysicsEngine.BroadPhaseLayers.Clip || !clip);
        }
    }
    public class DefaultObjectLayerRayCastFilter(bool clip) : ObjectLayerFilter
    {
        protected override bool ShouldCollide(ObjectLayer layer)
        {
            return layer != PhysicsEngine.Layers.Trigger &&
                layer != PhysicsEngine.Layers.Ignored && (layer != PhysicsEngine.Layers.Clip || !clip);
        }
    }
    public class DefaultBodyFilterRayCastFilter : BodyFilter
    {
        protected override bool ShouldCollide(BodyID bodyID)
        {
            return !PhysicsEngine.IgnoredBodies.Contains(bodyID) && PhysicsEngine.ShouldCollideBody(bodyID);
        }

        protected override bool ShouldCollideLocked(Body body)
        {
            return !PhysicsEngine.IgnoredBodies.Contains(body.ID) && PhysicsEngine.ShouldCollideBody(body.ID);
        }
    }
    public class IgnoreEntitiesBodyFilterRayCastFilter : BodyFilter
    {
        protected override bool ShouldCollide(BodyID bodyID)
        {
            return !PhysicsEngine.BodyMapper.ContainsKey(bodyID) && !PhysicsEngine.IgnoredBodies.Contains(bodyID);
        }

        protected override bool ShouldCollideLocked(Body body)
        {
            return !PhysicsEngine.BodyMapper.ContainsKey(body.ID) && !PhysicsEngine.IgnoredBodies.Contains(body.ID);
        }
    }
}

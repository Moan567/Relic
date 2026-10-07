using Chisel.Collision;
using Chisel.Utils;
using Engine.Physics;
using Engine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RenderingLibrary.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities.PhysicsEntities;

[EntityDescriptor()]
[ExposeEntityPropertyTarget("Entity A", "First entity to attach to.")]
[ExposeEntityPropertyTarget("Entity B", "Second entity to attach to.")]
[ExposeEntityProperty("Material", EntityPropertyType.String, "Material to use, defaults to 'Rope'.")]
//[ExposeEntityProperty("Segment Count", Rockwall.EntityPropertyType.Float, "How many sections this rope gets.")]
public class RopeObject : WorldEntity
{
    public RopeObject()
    {
        // This isnt the simulated object itself, more just a container.
        Controller = new RopeController();
        IgnoreCollision = true;
        IsSimulated = false;
    }

    private class RopeController : EntityController
    {
        private JoltPhysicsSharp.BodyID[] segmentIDs;
        private Vector3[] segmentPositions;
        private List<JoltPhysicsSharp.Constraint> constraints = new List<JoltPhysicsSharp.Constraint>();
        [SaveValue("rope_element_dist")]
        private float halfHeight;
        [SaveValue("rope_saved_element_dist")]
        private bool hasHalfHeight;

        private int materialID;

        ShaderHandle effect;

        public override void OnDespawn()
        {
            foreach (var id in segmentIDs)
            {
                PhysicsEngine.AllBodies.Remove(id);
                PhysicsEngine.BodyInterface.RemoveAndDestroyBody(id);
            }
            if (constraints != null)
            {
                foreach (var c in constraints)
                {
                    PhysicsEngine.PhysicsSystem.RemoveConstraint(c);
                }
            }
        }

        public override void OnRender(GameTime gameTime)
        {
            effect.Param("World").SetValue(Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up));
            effect.Param("View").SetValue(RenderEngine.ViewMatrix);
            effect.Param("Projection").SetValue(RenderEngine.ProjectionMatrix);
            effect.Param("AlphaClip").SetValue(true);
            effect.Param("MainTex").SetValue(GlobalMapData.LoadedMaterials[materialID].Texture);

            Vector3 cameraPos = RenderEngine.CameraPosition;
            float thickness = 0.15f;

            List<Vector3> centerline = new List<Vector3>();
            for (int i = 0; i < segmentPositions.Length; i++)
            {
                Vector3 current = segmentPositions[i];
                if (i == 0 || i == segmentPositions.Length - 1)
                {
                    centerline.Add(current);
                    continue;
                }

                Vector3 previous = segmentPositions[i - 1];
                Vector3 next = segmentPositions[i + 1];

                float bevelSize = halfHeight * 0.5f;

                Vector3 toPrevious = Vector3.Normalize(previous - current);
                Vector3 toNext = Vector3.Normalize(next - current);
                Vector3 beforeCorner = current + toPrevious * bevelSize;
                Vector3 afterCorner = current + toNext * bevelSize;

                centerline.Add(beforeCorner);
                centerline.Add(afterCorner);
            }

            if (centerline.Count < 2)
                return;

            Vector3[] offsets = new Vector3[centerline.Count];
            for (int i = 0; i < centerline.Count; i++)
            {
                Vector3 point = centerline[i];

                Vector3 tangent;
                if (i == 0)
                    tangent = centerline[1] - centerline[0];
                else if (i == centerline.Count - 1)
                    tangent = centerline[i] - centerline[i - 1];
                else
                    tangent = centerline[i + 1] - centerline[i - 1];

                if (tangent.LengthSquared() < 1e-8f)
                    tangent = Vector3.Forward;
                tangent.Normalize();

                Vector3 toCamera = cameraPos - point;
                if (toCamera.LengthSquared() < 1e-8f)
                    toCamera = Vector3.Forward;
                toCamera.Normalize();

                Vector3 right = Vector3.Cross(tangent, toCamera);
                if (right.LengthSquared() < 1e-8f)
                {
                    right = Vector3.Cross(tangent, Vector3.Up);
                    if (right.LengthSquared() < 1e-8f)
                        right = Vector3.Cross(tangent, Vector3.Right);
                }
                right.Normalize();

                offsets[i] = right * (thickness * 0.5f);
            }

            List<VertexPositionColorTexture> vertices = new List<VertexPositionColorTexture>((centerline.Count - 1) * 6);

            for (int i = 0; i < centerline.Count - 1; i++)
            {
                Vector3 p0 = centerline[i];
                Vector3 p1 = centerline[i + 1];
                Vector3 off0 = offsets[i];
                Vector3 off1 = offsets[i + 1];

                Vector3 a = p0 - off0; // bottom-left
                Vector3 b = p0 + off0; // top-left
                Vector3 c = p1 - off1; // bottom-right
                Vector3 d = p1 + off1; // top-right

                Vector2 uvA = new Vector2(0f, 0f);
                Vector2 uvB = new Vector2(1f, 0f);
                Vector2 uvC = new Vector2(0f, 1f);
                Vector2 uvD = new Vector2(1f, 1f);

                Color col = Color.White;

                // Triangle 1: a, b, c
                vertices.Add(new VertexPositionColorTexture(a, col, uvA));
                vertices.Add(new VertexPositionColorTexture(c, col, uvC));
                vertices.Add(new VertexPositionColorTexture(b, col, uvB));

                // Triangle 2: c, b, d
                vertices.Add(new VertexPositionColorTexture(c, col, uvC));
                vertices.Add(new VertexPositionColorTexture(d, col, uvD));
                vertices.Add(new VertexPositionColorTexture(b, col, uvB));
            }


            effect.ApplyPass(0);
            MainEngine.Instance.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices.ToArray(), 0, vertices.Count / 3);
        }

        public override void OnAllEntitiesSpawned()
        {
            effect = ShaderBuilder.BuildParticlesShader(MainEngine.Instance.GraphicsDevice);

            //int segments = (int)((float)(entity.ReadProperty("Segment Count", Rockwall.EntityPropertyType.Float) ?? 10f));
            int segments = 8;

            var entityA = EntityManager.FindEntityIndexByName((entity.ReadProperty("Entity A", EntityPropertyType.String) ?? "") as string);
            var entityB = EntityManager.FindEntityIndexByName((entity.ReadProperty("Entity B", EntityPropertyType.String) ?? "") as string);

            var entityRefA = !(entityA == null || entityA.Length == 0 || entityA[0] == -1) ? EntityManager.entities[entityA[0]] : null;
            var entityRefB = !(entityB == null || entityB.Length == 0 || entityB[0] == -1) ? EntityManager.entities[entityB[0]] : null;

            Vector3 pointA = !(entityA == null || entityA.Length == 0 || entityA[0] == -1) ? EntityManager.entities[entityA[0]].Position : entity.Position;
            Vector3 pointB = !(entityB == null || entityB.Length == 0 || entityB[0] == -1) ? EntityManager.entities[entityB[0]].Position : entity.Position + Vector3.Up * 4;

            segmentIDs = new JoltPhysicsSharp.BodyID[segments];
            segmentPositions = new Vector3[segments + 1];

            if (!hasHalfHeight)
            {
                float segmentLength = Vector3.Distance(pointA, pointB) / segments;

                halfHeight = segmentLength * 0.5f;
                hasHalfHeight = true;
            }

            Vector3 previousPos = pointA;

            for (int i = 0; i < segments; i++)
            {
                var shape = new JoltPhysicsSharp.CapsuleShape(halfHeight, 0.1f);
                shape.Density += 1000f;
                shape.Density *= 20f;

                var segmentPos = Vector3.Lerp(pointA, pointB, i / (float)segments);

                var lookRot = Quaternion.CreateFromRotationMatrix(Matrix.CreateLookAt(previousPos, segmentPos, Vector3.Up));

                if (float.IsNaN(lookRot.W)) lookRot = Quaternion.Identity;

                using var bodySettings = new JoltPhysicsSharp.BodyCreationSettings(
                    shape,
                    segmentPos.ToNumerics(),
                    lookRot.ToNumerics(),
                    JoltPhysicsSharp.MotionType.Dynamic,
                    PhysicsEngine.Layers.Ragdoll);

                previousPos = segmentPos;

                bodySettings.Restitution = 0.0f;
                bodySettings.LinearDamping = 0.2f;
                bodySettings.AngularDamping = 0.8f;
                bodySettings.Friction = 6;
                bodySettings.UserData = (ulong)(PhysicsUserData.RAYPASS);

                var physicsBodyID = PhysicsEngine.BodyInterface.CreateAndAddBody(bodySettings, JoltPhysicsSharp.Activation.DontActivate);
                PhysicsEngine.AllBodies.Add(physicsBodyID);
                PhysicsEngine.AllShapes.Add(shape);

                segmentIDs[i] = physicsBodyID;

                bodySettings.Dispose();
            }

            for (int i = 0; i < segments + 1; i++)
            {
                var bodyA = i == 0 ? (entityRefA?.PhysicsBodyID) : segmentIDs[i - 1];
                var bodyB = i >= segments ? (entityRefB?.PhysicsBodyID) : segmentIDs[i];

                if (bodyA == null || bodyB == null) continue;

                var constrainA = PhysicsEngine.BodyInterface.GetPosition(bodyA.Value);
                var constrainB = PhysicsEngine.BodyInterface.GetPosition(bodyB.Value);

                var settings = new JoltPhysicsSharp.DistanceConstraintSettings
                {
                    Enabled = true,
                    Space = JoltPhysicsSharp.ConstraintSpace.LocalToBodyCOM,
                    Point1 = new System.Numerics.Vector3(0, -halfHeight, 0),
                    Point2 = new System.Numerics.Vector3(0, halfHeight, 0),
                    MaxDistance = 0.05f,
                    MinDistance = -0.05f,
                };

                var constraint = new JoltPhysicsSharp.DistanceConstraint(settings, PhysicsEngine.GetFromBodyID(bodyA.Value), PhysicsEngine.GetFromBodyID(bodyB.Value));
                PhysicsEngine.ConstraintsToAdd.Enqueue(constraint);
                constraints.Add(constraint);

                if (i < segments) PhysicsEngine.BodyInterface.ActivateBody(segmentIDs[i]);
            }
        }
        public override void OnSpawn()
        {
            string mat = (string)(entity.ReadProperty("Material", EntityPropertyType.String) ?? "Rope");

            GlobalMapData.MaterialNameToIndex.TryGetValue(mat, out materialID);
        }

        public override void OnUpdate(GameTime gameTime)
        {
            for (int i = 0; i < segmentIDs.Length; i++)
            {
                var pos = PhysicsEngine.BodyInterface.GetPosition(segmentIDs[i]).ToXNA();
                PhysicsEngine.GetFromBodyID(segmentIDs[i]).GetRotation(out var rotation);

                var up = Vector3.TransformNormal(Vector3.Up, System.Numerics.Matrix4x4.CreateFromQuaternion(rotation).ToXNA());
                segmentPositions[i] = pos + up * halfHeight;
                if (i == segmentIDs.Length - 1) segmentPositions[i + 1] = pos + up * -halfHeight;
            }
        }
    }
}

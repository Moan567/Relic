using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Xna.Framework;
using Engine;
using static Engine.MainEngine;
using Engine.Utils;
using Engine.Rendering;
using Engine.Sound;

namespace Engine.Entities
{
    public class CameraController : EntityController
    {
        protected Matrix WorldMatrix;
        protected Matrix ViewMatrix;
        protected Matrix ProjectionMatrix;

        private float fieldOfView = 80f; 
        private float zNear = 0.01f; 
        private float zFar = 1024;

        public float FOV
        {
            get
            {
                return fieldOfView;
            }
            set
            {
                fieldOfView = value;
                RebuildMatrix();
            }
        }
        public float ZNear
        {
            get
            {
                return zNear;
            }
            set
            {
                zNear = value;
                RebuildMatrix();
            }
        }
        public float ZFar
        {
            get
            {
                return zFar;
            }
            set
            {
                zFar = value;
                RebuildMatrix();
            }
        }

        public void RebuildMatrix()
        {
            ProjectionMatrix = Matrix.CreatePerspectiveFieldOfView(
                fieldOfView * (MathF.PI / 180f),
                Instance.GraphicsDevice.Viewport.AspectRatio,
                zNear,
                zFar);
        }

        public override void OnSpawn()
        {
            RebuildMatrix();
        }

        public override void OnDespawn()
        {
        }

        public override void OnUpdate(GameTime gameTime)
        {
            ViewMatrix = Matrix.CreateLookAt(entity.Position, entity.Position - entity.WorldTransformMatrix.Forward, entity.WorldTransformMatrix.Up);
            WorldMatrix = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        protected void RequestCameraControl(uint priority)
        {
            CameraControl.RequestCameraFrameControl(WorldMatrix, ViewMatrix, ProjectionMatrix, entity.Position, entity.Velocity, ZNear, ZFar, priority);
        }
    }
}

using Chisel.Utils;
using Engine.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities
{
    public abstract class AIEntityTemplate : AIAgent
    {
        private object target;
        private TargetType targetType;

        protected float aiCurThink;
        protected float aiLookAngle;
        protected float aiSteerSpeed = 200f;

        bool wasGrounded;

        private enum TargetType
        {
            Point,
            Entity
        }

        public void SetTarget(object target)
        {
            if(target is WorldEntity entityTarget)
            {
                this.target = target;
                targetType = TargetType.Entity;

                Vector3 targPos = entityTarget.Position;
                FindNewPath(targPos);
            }
            if(target is Vector3 pointTarget)
            {
                this.target = target;
                targetType = TargetType.Point;

                Vector3 targPos = pointTarget;
                FindNewPath(targPos);
            }
            if(target is null)
            {
                this.target = target;
            }
        }

        private void MoveToTarget()
        {
            if (pathCompleted)
            {
                if (aiCurThink <= GetThinkTimeAfterPathCompleted())
                {
                    aiCurThink += MainEngine.PreviousFrameDelta;
                }
                else
                {
                    aiCurThink = 0;
                }
            }
            else if(!wasGrounded && entity.IsOnGround && target is not null)
            {
                //There's a chance we just fell off some edge, so lets figure out some other way to get where we want.
                FindNewPath(targetType == TargetType.Point?(Vector3)target:((WorldEntity)target).Position);
            }
            
            if (target is WorldEntity entity1)
            {
                Vector3 targPos = entity1.Position;
                var hit = BSPRoot.TraceRay(new Ray(targPos, Vector3.Down), 100f, false);

                if (hit.Hit)
                {
                    targPos = hit.Point;
                }

                if(Vector3.Distance(targPos,currentTarget) > 0.8f)FindNewPath(targPos);
            }

            Vector3 dirToPath = Vector3.Zero;
            Vector3 wishDir = Vector3.Zero;
            if (!pathCompleted)
            {
                dirToPath = GetMoveTarget() - entity.Position;
                dirToPath.Y = 0;
                float dist;
                if (AINodeUtils.TestStepWalkable(dirToPath, ref entity, out dist))
                {
                    Vector3 originalDir = dirToPath;
                    Vector3 sideDir = Vector3.Cross(dirToPath,Vector3.Up);
                    dirToPath = entity.Position + originalDir * dist - entity.Position;

                    if (AINodeUtils.TestStepWalkable(dirToPath, ref entity, out dist))
                    {
                        dirToPath = entity.Position + originalDir * dist - entity.Position;
                    }
                }
            }

            if (dirToPath.Length() > 0.5f)
            {
                dirToPath.Normalize();
                float lookAngle = MathHelper.ToDegrees(MathF.Atan2(dirToPath.X, dirToPath.Z));

                float angleDelta = float.Abs(CMath.DeltaAngle(aiLookAngle, lookAngle));

                aiLookAngle = CMath.MoveTowardsAngle(aiLookAngle, lookAngle, MainEngine.PreviousFrameDelta * aiSteerSpeed);
                wishDir = dirToPath;

                //float multi = 1-float.Min(angleDelta / 100f, 1);
                //wishDir *= multi;
            }

            applyVelocity(wishDir);
            wasGrounded = entity.IsOnGround;
        }
        void applyVelocity(Vector3 wishDir)
        {
            float speed = new Vector2(entity.Velocity.X, entity.Velocity.Z).Length();
            if (speed != 0 && entity.IsOnGround)
            {
                float drop = speed * 10f * MainEngine.PreviousFrameDelta;
                entity.Velocity *= MathF.Max(speed - drop, 0) / speed;
            }

            float multiplier = 1;
            float len = new Vector2(wishDir.X, wishDir.Z).Length();
            if (len > 1)
            {
                multiplier = 1 / len;
            }
            wishDir *= multiplier;

            float curSpeed = Vector3.Dot(wishDir, entity.Velocity);
            float addSpeed = CMath.Clamp((entity.IsOnGround ? GetAISpeed() : 1) - curSpeed, 0, GetAIAccel() * MainEngine.PreviousFrameDelta);

            Vector3 initWish = addSpeed * wishDir;
            entity.Velocity += initWish;
        }

        public override void OnUpdate(GameTime gameTime)
        {
            MoveToTarget();
        }
        public override void OnSpawn()
        {
        }

        public abstract float GetThinkTimeAfterPathCompleted();
        public abstract float GetAISpeed();
        public abstract float GetAIAccel();
    }
}

using Chisel.Models;
using Engine;
using Engine.Physics;
using Engine.Rendering;
using Engine.Utils;
using Engine.Utils;
using Microsoft.Xna.Framework;
using RenderingLibrary.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using static Chisel.Models.CModel;

namespace Chisel.Utils.Animation
{
    public enum RootMotionMode
    {
        Give,
        Receive
    }

    public class BipedAnimator
    {
        public const string MoveYawParam = "move_yaw";
        public const string AimYawParam = "aim_yaw";
        public const string AimPitchParam = "aim_pitch";
        public const string LookYawParam = "look_yaw";
        public const string LookPitchParam = "look_pitch";

        public LayerConfig IdleLayer { get; }
        public LayerConfig LocomotionLayer { get; }
        public LayerConfig AimLayer { get; }
        public LayerConfig LookLayer { get; }

        public RootMotionMode Mode { get; set; } = RootMotionMode.Give;

        public float RunSpeed { get; set; }
        public float LocomotionFadeTime { get; set; } = 0.2f;
        public float InfluenceBlendRate { get; set; } = 6f;
        public float MoveYawTurnRate { get; set; } = 360f;
        public float AimTurnRate { get; set; } = 5f;
        public float LookTurnRate { get; set; } = 3f;

        public float CurrentLookYaw => curLookYaw;
        public float CurrentLookPitch => curLookPitch;

        public float YawCutThresholdDegrees { get; set; } = 100f;
        public float YawCutFadeSpeed { get; set; } = 8f;

        /// <summary>
        /// How far the chest/aim layer alone is allowed to turn before the rest of a
        /// look request has to come from the head instead.
        /// </summary>
        public float MaxAimYaw { get; set; } = 180f;
        public float MaxAimPitch { get; set; } = 90f;

        /// <summary>Extra range the head-only look layer is allowed to cover on top of
        /// whatever the chest already achieved. Only relevant once a "look" sequence exists.</summary>
        public float MaxLookYaw { get; set; } = 80f;
        public float MaxLookPitch { get; set; } = 50f;

        /// <summary>Attachment used to measure the chest's actual achieved facing each
        /// frame.</summary>
        public string ChestAttachmentName { get; set; } = "chest";

        public string LeftFootChainName { get; set; } = "lfoot";
        public string RightFootChainName { get; set; } = "rfoot";
        public bool EnableFootIK { get; set; } = true;
        public float FootTraceUpOffset { get; set; } = 0.6f;
        public float FootTraceDownDistance { get; set; } = 1f;
        public float PelvisShiftBlendRate { get; set; } = 12f;
        public float PelvisMaxShift { get; set; } = 0.6f;
        public float PelvisSlopeShiftFactor { get; set; } = 0.9f;
        public float PelvisStanceWidthFactor { get; set; } = 0.45f;
        public float PelvisTraceUpOffset { get; set; } = 0.4f;
        public float PelvisTraceDownDistance { get; set; } = 2f;
        public float FootRelatchDriftThreshold { get; set; } = 0.03f;
        public float FootRelatchBlendDuration { get; set; } = 0.15f;
        public float FootRelatchWindowThreshold { get; set; } = 0.8f;
        public float PredictiveMaxGroundDelta { get; set; } = 0.5f;

        private readonly List<CPelvisPlacement.Sample> pelvisSampleScratch = new(3);

        private class FootContactState
        {
            public bool LockedHasGround;
            public Vector3 LockedGroundPointWorld;
            public Vector3 LockedGroundNormalWorld;

            public Vector3 RelatchFromPointWorld;
            public Vector3 RelatchFromNormalWorld;
            public float RelatchElapsed = 999f;
        }

        private readonly Dictionary<string, FootContactState> footContacts = new();
        private bool footChainsResolved;
        private CIKChain leftFootChain;
        private CIKChain rightFootChain;
        private readonly List<CFootPlacement.FootInput> footInput = new(2);
        public JoltPhysicsSharp.BodyID[] FootTraceIgnoredBodies { get; set; } = Array.Empty<JoltPhysicsSharp.BodyID>();

        private float currentPelvisShift;


        private readonly CModelDisplay display;
        private readonly string walkSequenceName;
        private readonly string runSequenceName;
        private readonly bool lookSequenceAvailable;

        private enum Gait { Walk, Run }
        private Gait currentGait = Gait.Walk;

        private enum YawCutState { Idle, FadingOut, FadingIn }
        private YawCutState yawCutState = YawCutState.Idle;
        private float yawCutMultiplier = 1f;
        private float pendingTargetYaw;

        private float curMoveYaw;
        private float curAimYaw;
        private float curAimPitch;
        private float curLookYaw;
        private float curLookPitch;

        public Vector3 RootMotionDelta => LocomotionLayer.RootMotionDelta * LocomotionLayer.Influence;

        public BipedAnimator(CModelDisplay display,
            string idleLayerName = "idle",
            string locomotionLayerName = "locomotion",
            string aimLayerName = "aim",
            string lookLayerName = "look",
            string idleSequence = "idle",
            string walkSequence = "walk",
            string runSequence = "run",
            string aimSequence = "aim",
            string lookSequence = "look",
            float runSpeed = 5f,
            int idleOrder = 0,
            int locomotionOrder = 10,
            int aimOrder = 20,
            int lookOrder = 30)
        {
            this.display = display;
            walkSequenceName = walkSequence;
            runSequenceName = runSequence;
            RunSpeed = runSpeed;

            var animator = display.EnsureAnimator();
            IdleLayer = animator.GetOrAddLayer(idleLayerName, order: idleOrder);
            LocomotionLayer = animator.GetOrAddLayer(locomotionLayerName, order: locomotionOrder);
            AimLayer = animator.GetOrAddLayer(aimLayerName, LayerBlendType.Additive, order: aimOrder);
            LookLayer = animator.GetOrAddLayer(lookLayerName, LayerBlendType.Additive, order: lookOrder);

            IdleLayer.Influence = 1f;
            AimLayer.Influence = 1f;
            LookLayer.Influence = 1f;
            LocomotionLayer.Influence = 0f;

            var idleSeq = display.Model.FindSequence(idleSequence);
            if (idleSeq != null) IdleLayer.ForceReplay(idleSeq);

            var aimSeq = display.Model.FindSequence(aimSequence);
            lookSequenceAvailable = display.Model.FindSequence(lookSequence) != null;
            if (aimSeq != null) AimLayer.Play(aimSeq);

            var lookSeq = display.Model.FindSequence(lookSequence);
            if (lookSequenceAvailable) LookLayer.Play(lookSeq);

            PlayGait(Gait.Walk, force: true);
        }

        public void UpdateLocomotion(float speed, float moveYawDegrees, float dt)
        {
            Gait target = ComputeGait(speed);
            if (target != currentGait)
            {
                PlayGait(target, force: false);
            }

            UpdateMoveYaw(moveYawDegrees, dt);
            LocomotionLayer.SetParam(MoveYawParam, curMoveYaw);

            float baseInfluence;
            if (Mode == RootMotionMode.Receive)
            {
                baseInfluence = ApplyReceivedMotion(speed);
            }
            else
            {
                float targetInfluence = speed > 0.05f ? 1f : 0f;
                baseInfluence = CMath.MoveTowards(LocomotionLayer.Influence / MathF.Max(yawCutMultiplier, 0.0001f), targetInfluence, dt * InfluenceBlendRate);
                baseInfluence = Math.Clamp(baseInfluence, 0f, 1f);
                LocomotionLayer.PlaybackSpeedMultiplier = 1f;
            }

            LocomotionLayer.Influence = baseInfluence * yawCutMultiplier;
        }

        /// <summary>
        /// Weapon/torso aim
        /// </summary>
        public void UpdateAim(float targetYaw, float targetPitch, float dt)
        {
            float clampedYaw = Math.Clamp(targetYaw, -MaxAimYaw, MaxAimYaw);
            float clampedPitch = Math.Clamp(targetPitch, -MaxAimPitch, MaxAimPitch);

            curAimYaw = EaseAngle(curAimYaw, clampedYaw, AimTurnRate, dt);
            curAimPitch = EaseAngle(curAimPitch, clampedPitch, AimTurnRate, dt);

            AimLayer.SetParam(AimYawParam, curAimYaw);
            AimLayer.SetParam(AimPitchParam, curAimPitch);
        }

        /// <summary>
        /// Head/eye gaze.
        /// Advances CurrentLookYaw/Pitch, even with no "look" sequence authored, so eyes
        /// have a smooth, correct direction to track; only drives the LookLayer itself
        /// when a look sequence actually exists.
        /// </summary>
        public void UpdateLook(float targetYaw, float targetPitch, float dt)
        {
            float residualYaw, residualPitch;

            if (TryGetChestFacing(out float achievedYaw, out float achievedPitch))
            {
                residualYaw = Math.Clamp(NormalizeAngle(targetYaw - achievedYaw), -MaxLookYaw, MaxLookYaw);
                residualPitch = Math.Clamp(NormalizeAngle(targetPitch - achievedPitch), -MaxLookPitch, MaxLookPitch);
            }
            else
            {
                residualYaw = Math.Clamp(targetYaw, -MaxLookYaw, MaxLookYaw);
                residualPitch = Math.Clamp(targetPitch, -MaxLookPitch, MaxLookPitch);
            }

            curLookYaw = EaseAngle(curLookYaw, residualYaw, LookTurnRate, dt);
            curLookPitch = EaseAngle(curLookPitch, residualPitch, LookTurnRate, dt);

            if (!lookSequenceAvailable) return;

            LookLayer.SetParam(LookYawParam, curLookYaw);
            LookLayer.SetParam(LookPitchParam, curLookPitch);
        }

        private bool TryGetChestFacing(out float yaw, out float pitch)
        {
            yaw = 0f;
            pitch = 0f;

            var attachment = display.Model.FindAttachment(ChestAttachmentName);
            if (attachment == null) return false;

            var transforms = display.Model.ModelTransforms;
            if (transforms == null) return false;

            Matrix world = attachment.GetTransform(transforms);
            Vector3 fwd = -world.Forward;
            if (fwd.LengthSquared() < 0.0001f) return false;

            fwd = Vector3.Normalize(fwd);
            yaw = MathF.Atan2(fwd.X, fwd.Z) * (180f / MathF.PI);
            pitch = MathF.Asin(Math.Clamp(fwd.Y, -1f, 1f)) * (180f / MathF.PI);
            return true;
        }

        private void UpdateMoveYaw(float targetYaw, float dt)
        {
            if (LocomotionLayer.Influence < 0.01f && yawCutState == YawCutState.Idle)
            {
                curMoveYaw = targetYaw;
                return;
            }

            switch (yawCutState)
            {
                case YawCutState.Idle:
                    float diff = NormalizeAngle(targetYaw - curMoveYaw);
                    if (MathF.Abs(diff) > YawCutThresholdDegrees)
                    {
                        pendingTargetYaw = targetYaw;
                        yawCutState = YawCutState.FadingOut;
                    }
                    else
                    {
                        curMoveYaw = CMath.MoveTowardsAngle(curMoveYaw, targetYaw, dt * MoveYawTurnRate);
                    }
                    break;

                case YawCutState.FadingOut:
                    pendingTargetYaw = targetYaw;
                    yawCutMultiplier = CMath.MoveTowards(yawCutMultiplier, 0f, dt * YawCutFadeSpeed);
                    if (yawCutMultiplier <= 0.001f)
                    {
                        curMoveYaw = pendingTargetYaw;
                        yawCutState = YawCutState.FadingIn;
                    }
                    break;

                case YawCutState.FadingIn:
                    curMoveYaw = CMath.MoveTowardsAngle(curMoveYaw, targetYaw, dt * MoveYawTurnRate);
                    yawCutMultiplier = CMath.MoveTowards(yawCutMultiplier, 1f, dt * YawCutFadeSpeed);
                    if (yawCutMultiplier >= 0.999f)
                    {
                        yawCutState = YawCutState.Idle;
                    }
                    break;
            }
        }

        private bool TraceGround(Vector3 worldPos, out Vector3 groundPoint, out Vector3 groundNormal)
        {
            Vector3 traceStart = worldPos + Vector3.Up * FootTraceUpOffset;
            float traceDistance = FootTraceUpOffset + FootTraceDownDistance;
            Ray ray = new Ray(traceStart, Vector3.Down);

            bool didHit = Engine.Utils.Collision.CastPhysicsWorld(ray, traceDistance, out JoltPhysicsSharp.RayCastResult result, PhysicsFilters.IgnoreRagdollBroadPhaseRayCastFilter, FootTraceIgnoredBodies);

            groundPoint = Vector3.Zero;
            groundNormal = Vector3.Up;
            if (!didHit) return false;

            groundPoint = traceStart + Vector3.Down * (traceDistance * result.Fraction);
            groundNormal = PhysicsEngine.BodyInterface
                .GetShape(result.BodyID)
                .GetSurfaceNormal(result.subShapeID2, groundPoint.ToNumerics())
                .ToXNA();

            if (RenderEngine.ShowFootIK)
            {
                DebugDraw.Line(traceStart, traceStart + Vector3.Down * traceDistance, Color.Yellow);
            }

            return true;
        }

        public void UpdateFootPlacement(float dt, Matrix entityWorldTransform, BoundingBox localBounds)
        {
            if (!EnableFootIK) return;

            var animator = display.EnsureAnimator();
            var model = display.Model;

            if (!footChainsResolved)
            {
                leftFootChain = model.FindIKChain(LeftFootChainName);
                rightFootChain = model.FindIKChain(RightFootChainName);
                footChainsResolved = true;
            }

            footInput.Clear();
            TryBuildFootInput(animator, model, leftFootChain, entityWorldTransform, dt, footInput);
            TryBuildFootInput(animator, model, rightFootChain, entityWorldTransform, dt, footInput);

            var legResults = CFootPlacement.SolveFeet(footInput);
            foreach (var r in legResults)
            {
                animator.SetProceduralIKTarget(r.ChainName, r.Target, r.Weight);
            }

            Vector3 origin = entityWorldTransform.Translation;
            float halfWidthLocal = (localBounds.Max.X - localBounds.Min.X) * 0.5f * PelvisStanceWidthFactor;
            Vector3 stanceOffsetWorld = Vector3.TransformNormal(new Vector3(halfWidthLocal, 0f, 0f), entityWorldTransform);

            pelvisSampleScratch.Clear();
            if (TracePelvisSample(origin, out var centerSample)) pelvisSampleScratch.Add(centerSample);
            if (TracePelvisSample(origin + stanceOffsetWorld, out var rightSample)) pelvisSampleScratch.Add(rightSample);
            if (TracePelvisSample(origin - stanceOffsetWorld, out var leftSample)) pelvisSampleScratch.Add(leftSample);

            float desiredPelvisShift = CPelvisPlacement.ComputeShift(origin, pelvisSampleScratch, PelvisMaxShift, PelvisSlopeShiftFactor);

            currentPelvisShift = CMath.MoveTowards(currentPelvisShift, desiredPelvisShift, dt * PelvisShiftBlendRate);
            animator.SetPelvisShift(currentPelvisShift);
        }

        private const float DebugSideOffset = 0.18f;

        private void TryBuildFootInput(CModelAnimator animator, CModel model, CIKChain chain, Matrix entityWorldTransform, float dt, List<CFootPlacement.FootInput> outputList)
        {
            if (chain == null || chain.TopBoneIndex < 0) return;

            if (!footContacts.TryGetValue(chain.ChainName, out var state))
            {
                footContacts[chain.ChainName] = state = new FootContactState();
            }

            float windowWeight = animator.GetIKWindowWeight(chain.ChainName);

            Matrix rawBoneModel = animator.GetPreIKBoneTransform(chain.EndBoneIndex);
            Matrix effectorModel = CIKSolver.GetEffectorTransform(chain, rawBoneModel);
            effectorModel.Decompose(out _, out Quaternion effectorRotModel, out Vector3 effectorModelPos);

            Matrix worldToModel = Matrix.Invert(entityWorldTransform);

            Vector3 assumedGroundLocal = new Vector3(effectorModelPos.X, currentPelvisShift, effectorModelPos.Z);
            Vector3 assumedGroundWorld = Vector3.Transform(assumedGroundLocal, entityWorldTransform);

            bool groundHit = TraceGround(assumedGroundWorld, out Vector3 realGroundWorld, out Vector3 realGroundNormalWorld);

            Vector3 debugRightWorld = Vector3.TransformNormal(chain.ChainName == LeftFootChainName ? Vector3.Left : Vector3.Right, entityWorldTransform);
            debugRightWorld = debugRightWorld.LengthSquared() > 0.0001f ? Vector3.Normalize(debugRightWorld) : Vector3.Right;

            bool hasPredictedTarget = false;
            Matrix predictedTargetModel = default;
            Vector3 predictedWorldPosForDebug = default;

            if (groundHit)
            {
                Vector3 hitPointModel = Vector3.Transform(realGroundWorld, worldToModel);
                Vector3 hitNormalModel = Vector3.Normalize(Vector3.TransformNormal(realGroundNormalWorld, worldToModel));

                Vector3 groundDeviationModel = hitPointModel - assumedGroundLocal;
                float deviationLen = groundDeviationModel.Length();
                if (deviationLen > PredictiveMaxGroundDelta)
                {
                    groundDeviationModel = groundDeviationModel / deviationLen * PredictiveMaxGroundDelta;
                }
                Vector3 clampedHitPointModel = assumedGroundLocal + groundDeviationModel;

                Quaternion planeRotation = CIKSolver.ShortestRotation(Vector3.Up, hitNormalModel);

                Vector3 offsetFromReference = effectorModelPos - assumedGroundLocal;
                Vector3 predictedPosModel = clampedHitPointModel + Vector3.Transform(offsetFromReference, planeRotation);
                Quaternion predictedRotModel = Quaternion.Normalize(planeRotation * effectorRotModel);

                predictedTargetModel = Matrix.CreateFromQuaternion(predictedRotModel);
                predictedTargetModel.Translation = predictedPosModel;
                hasPredictedTarget = true;

                predictedWorldPosForDebug = Vector3.Transform(predictedPosModel, entityWorldTransform);
            }

            state.RelatchElapsed += dt;

            if (windowWeight <= 0f)
            {
                state.LockedHasGround = false;
            }
            else
            {
                bool needsFreshCapture = !state.LockedHasGround
                    || (windowWeight >= FootRelatchWindowThreshold && groundHit &&
                        Vector3.Distance(realGroundWorld, state.LockedGroundPointWorld) > FootRelatchDriftThreshold);

                if (needsFreshCapture && groundHit)
                {
                    bool isFirstEverCapture = !state.LockedHasGround;
                    float blendT = Math.Clamp(state.RelatchElapsed / MathF.Max(FootRelatchBlendDuration, 0.0001f), 0f, 1f);

                    if (isFirstEverCapture)
                    {
                        state.RelatchFromPointWorld = realGroundWorld;
                        state.RelatchFromNormalWorld = realGroundNormalWorld;
                    }
                    else
                    {
                        state.RelatchFromPointWorld = Vector3.Lerp(state.RelatchFromPointWorld, state.LockedGroundPointWorld, blendT);
                        state.RelatchFromNormalWorld = Vector3.Normalize(Vector3.Lerp(state.RelatchFromNormalWorld, state.LockedGroundNormalWorld, blendT));
                    }

                    state.LockedGroundPointWorld = realGroundWorld;
                    state.LockedGroundNormalWorld = realGroundNormalWorld;
                    state.LockedHasGround = true;
                    state.RelatchElapsed = isFirstEverCapture ? FootRelatchBlendDuration : 0f;
                }
            }

            float relatchT = Math.Clamp(state.RelatchElapsed / MathF.Max(FootRelatchBlendDuration, 0.0001f), 0f, 1f);
            Vector3 effectiveLockedPoint = Vector3.Lerp(state.RelatchFromPointWorld, state.LockedGroundPointWorld, relatchT);
            Vector3 effectiveLockedNormal = state.LockedHasGround
                ? Vector3.Normalize(Vector3.Lerp(state.RelatchFromNormalWorld, state.LockedGroundNormalWorld, relatchT))
                : Vector3.Up;

            if (RenderEngine.ShowFootIK)
            {
                Vector3 rawWorldPos = Vector3.Transform(effectorModelPos, entityWorldTransform);
                Vector3 rawDebugPos = rawWorldPos + debugRightWorld * DebugSideOffset;

                DebugDraw.Point(rawDebugPos, Color.Orange, 0.05f);
                DebugDraw.Text(rawDebugPos + Vector3.Up * 0.12f, $"{chain.ChainName} w={windowWeight:0.00}", Color.White);

                if (hasPredictedTarget)
                {
                    Vector3 predictedDebugPos = predictedWorldPosForDebug + debugRightWorld * DebugSideOffset;
                    DebugDraw.Point(predictedDebugPos, Color.LimeGreen, 0.045f);
                    DebugDraw.Line(rawDebugPos, predictedDebugPos, Color.LimeGreen);
                }

                if (state.LockedHasGround)
                {
                    Vector3 lockedDebugPos = effectiveLockedPoint + debugRightWorld * DebugSideOffset;
                    DebugDraw.Sphere(lockedDebugPos, 0.06f, Color.Cyan, 0f);
                }
            }

            outputList.Add(new CFootPlacement.FootInput
            {
                ChainName = chain.ChainName,
                AnimatedAnklePos = effectorModelPos,
                AnimatedAnkleRot = effectorRotModel,
                LocalOrientationOffset = chain.LocalOrientationOffset,
                WindowWeight = windowWeight,
                HasPredictedTarget = hasPredictedTarget,
                PredictedTarget = predictedTargetModel,
                LockedGroundHit = state.LockedHasGround,
                LockedGroundPoint = state.LockedHasGround ? Vector3.Transform(effectiveLockedPoint, worldToModel) : Vector3.Zero,
                LockedGroundNormal = state.LockedHasGround ? Vector3.Normalize(Vector3.TransformNormal(effectiveLockedNormal, worldToModel)) : Vector3.Up
            });
        }

        private bool TracePelvisSample(Vector3 worldOrigin, out CPelvisPlacement.Sample sample)
        {
            Vector3 traceStart = worldOrigin + Vector3.Up * PelvisTraceUpOffset;
            float traceDistance = PelvisTraceUpOffset + PelvisTraceDownDistance;
            Ray ray = new Ray(traceStart, Vector3.Down);

            bool didHit = Engine.Utils.Collision.CastPhysicsWorld(ray, traceDistance, out JoltPhysicsSharp.RayCastResult result, PhysicsFilters.IgnoreRagdollBroadPhaseRayCastFilter, FootTraceIgnoredBodies);

            sample = default;
            if (!didHit) return false;

            Vector3 hitPos = traceStart + Vector3.Down * (traceDistance * result.Fraction);
            Vector3 hitNormal = PhysicsEngine.BodyInterface
                .GetShape(result.BodyID)
                .GetSurfaceNormal(result.subShapeID2, hitPos.ToNumerics())
                .ToXNA();

            sample = new CPelvisPlacement.Sample { Hit = true, GroundPoint = hitPos, GroundNormal = hitNormal };

            if (RenderEngine.ShowFootIK)
            {
                DebugDraw.Line(traceStart, traceStart + Vector3.Down * traceDistance, Color.Yellow);
                DebugDraw.Sphere(hitPos, 0.05f, Color.Magenta, 0f);
            }

            return true;
        }

        private float ApplyReceivedMotion(float requestedSpeed)
        {
            float naturalSpeed = LocomotionLayer.GetAggregateNaturalSpeed();

            if (naturalSpeed < 0.0001f)
            {
                LocomotionLayer.PlaybackSpeedMultiplier = 1f;
                return requestedSpeed > 0.0001f ? 1f : 0f;
            }

            float dividend = requestedSpeed / naturalSpeed;

            if (dividend <= 1f)
            {
                LocomotionLayer.PlaybackSpeedMultiplier = 1f;
                return dividend;
            }

            LocomotionLayer.PlaybackSpeedMultiplier = dividend;
            return 1f;
        }

        private Gait ComputeGait(float speed)
        {
            if (currentGait == Gait.Run)
            {
                return speed >= RunSpeed * 0.85f ? Gait.Run : Gait.Walk;
            }
            return speed >= RunSpeed ? Gait.Run : Gait.Walk;
        }

        private void PlayGait(Gait gait, bool force)
        {
            string name = gait == Gait.Run ? runSequenceName : walkSequenceName;
            var sequence = display.Model.FindSequence(name);
            if (sequence == null) return;

            if (force) LocomotionLayer.ForceReplay(sequence);
            else LocomotionLayer.Play(sequence, LocomotionFadeTime);

            currentGait = gait;
        }

        private static float EaseAngle(float current, float target, float decayRate, float dt)
        {
            float diff = NormalizeAngle(target - current);
            float t = 1f - MathF.Exp(-decayRate * dt);
            return current + diff * t;
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
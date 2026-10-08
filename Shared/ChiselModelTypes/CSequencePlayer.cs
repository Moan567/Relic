using Chisel.Models;
using Liru3D.Animations;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using static Chisel.Models.CModel;

namespace Chisel.Utils.Animation
{
    public class CAnimationPlayer
    {
        private readonly Matrix[] modelSpaceTransforms;
        private readonly Matrix[] boneSpaceTransforms;
        private readonly Matrix[] localSpaceTransforms;
        private readonly BoneSRT[] localSpaceSRTs;
        public Matrix[] LocalSpaceTransforms => localSpaceTransforms;
        public BoneSRT[] LocalSpaceSRTs => localSpaceSRTs;
        private CModel model;
        public bool SRTOnly { get; set; }

        private readonly CBoneChannelPlayer[] channelPlayers;

        private CModel.CAnimDef animDef;

        private float currentTime;
        private float currentTick;
        private bool isPlaying = false;

        public CModel Model
        {
            get => model;
            set
            {
                if (value == model) return;
                if (value == null) throw new Exception("Cannot set an animation's model to null.");
                if (model != null && value.Bones.Count != model.Bones.Count) throw new Exception("Cannot switch the model of an animation player to a model with a different number of bones.");
                model = value;
            }
        }

        public CModel.CAnimDef AnimDef
        {
            get => animDef;
            set
            {
                if (AnimDef == value) return;

                animDef = value;

                for (int i = 0; i < Model.Bones.Count; i++)
                {
                    CBoneChannel channel = null;
                    animDef?.Animation?.ChannelsByBoneName?.TryGetValue(Model.Bones[i].Name, out channel);
                    channelPlayers[i].Channel = channel;
                }

                currentTime = 0f;
                currentTick = 0f;
                isPlaying = false;

                if (animDef != null)
                {
                    updateTransforms();
                }
            }
        }

        public float CurrentTime
        {
            get => currentTime;
            set
            {
                currentTime = value;
                currentTick = AnimDef.Animation.TicksPerSecond * CurrentTime;
            }
        }

        public int CurrentWholeTick => (int)(PlaybackDirection == 1 ? Math.Floor(CurrentTick) : Math.Ceiling(CurrentTick));

        public float CurrentTick
        {
            get => currentTick;
            set
            {
                currentTick = value;
                currentTime = currentTick / AnimDef.Animation.DurationInTicks;
            }
        }

        public int PlaybackDirection => Math.Sign(PlaybackSpeed);
        public float PlaybackSpeed { get; set; } = 1f;

        public bool IsPlaying
        {
            get => isPlaying;
            set
            {
                if (isPlaying == value) return;
                if (AnimDef == null) return;

                isPlaying = value;

                if (isPlaying && (CurrentTime >= animDef.Animation.DurationInSeconds || CurrentTime <= 0f))
                {
                    CurrentTime = PlaybackDirection == 1 ? 0f : AnimDef.Animation.DurationInSeconds;
                }
            }
        }

        public bool IsLooping { get; set; }

        public IReadOnlyList<Matrix> ModelSpaceTransforms => modelSpaceTransforms;
        public IReadOnlyList<Matrix> BoneSpaceTransforms => boneSpaceTransforms;

        public CAnimationPlayer(CModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));

            modelSpaceTransforms = new Matrix[model.Bones.Count];
            boneSpaceTransforms = new Matrix[model.Bones.Count];
            localSpaceTransforms = new Matrix[model.Bones.Count];
            localSpaceSRTs = new BoneSRT[model.Bones.Count];

            channelPlayers = new CBoneChannelPlayer[model.Bones.Count];
            for (int i = 0; i < Model.Bones.Count; i++)
            {
                channelPlayers[i] = new CBoneChannelPlayer(this);
            }
        }
        public CAnimationPlayer() { }

        public void Update(float dt)
        {
            if (!IsPlaying || AnimDef == null) return;

            for (int i = 0; i < Model.Bones.Count; i++)
            {
                channelPlayers[i].Update();
            }

            updateTransforms();

            CurrentTime += dt * PlaybackSpeed * animDef.Speed;

            if (CurrentTime >= animDef.Animation.DurationInSeconds || CurrentTime <= 0f)
            {
                if (IsLooping) CurrentTime -= AnimDef.Animation.DurationInSeconds * PlaybackDirection;
                else IsPlaying = false;
            }
        }
        public void UpdateRootBoneOnly(float dt)
        {
            if (!IsPlaying || AnimDef == null) return;

            channelPlayers[0].Update();
            LocalSpaceSRTs[0] = channelPlayers[0].InterpolatedSRT;

            CurrentTime += dt * PlaybackSpeed * animDef.Speed;

            if (CurrentTime >= animDef.Animation.DurationInSeconds || CurrentTime <= 0f)
            {
                if (IsLooping) CurrentTime -= animDef.Animation.DurationInSeconds * PlaybackDirection;
                else IsPlaying = false;
            }
        }

        public void ForceUpdateTransforms()
        {
            for (int i = 0; i < Model.Bones.Count; i++)
            {
                channelPlayers[i].Update();
            }
            updateTransforms();
        }

        public void SetEffectBones(SkinnedEffect skinnedEffect) => skinnedEffect?.SetBoneTransforms(boneSpaceTransforms);
        public void SetEffectBones(Action<Matrix[]> setFunction) => setFunction?.Invoke(boneSpaceTransforms);

        private void updateTransforms()
        {
            if (SRTOnly)
            {
                for (int i = 0; i < Model.Bones.Count; i++)
                {
                    localSpaceSRTs[i] = channelPlayers[i].InterpolatedSRT;
                }
                return;
            }

            for (int i = 0; i < Model.Bones.Count; i++)
            {
                CBone bone = Model.Bones[i];
                Matrix boneLocalTransform = channelPlayers[i].InterpolatedSRT.ToMatrix();

                localSpaceSRTs[i] = channelPlayers[i].InterpolatedSRT;
                localSpaceTransforms[i] = boneLocalTransform;

                modelSpaceTransforms[i] = bone.HasParent
                    ? boneLocalTransform * modelSpaceTransforms[bone.Parent.Index]
                    : boneLocalTransform;

                boneSpaceTransforms[i] = bone.Offset * modelSpaceTransforms[i];
            }
        }
    }

    public class CBoneChannelPlayer
    {
        private readonly CAnimationPlayer animationPlayer;

        private Keyframe<Vector3> currentScaleFrame;
        private Keyframe<Quaternion> currentRotationFrame;
        private Keyframe<Vector3> currentPositionFrame;

        private CBoneChannel channel;

        public Vector3 InterpolatedScale { get; private set; } = Vector3.One;
        public Quaternion InterpolatedRotation { get; private set; } = Quaternion.Identity;
        public Vector3 InterpolatedPosition { get; private set; } = Vector3.Zero;
        public BoneSRT InterpolatedSRT { get; private set; }

        public CBoneChannel Channel
        {
            get => channel;
            set
            {
                channel = value;
                ResetToFirstFrame();
            }
        }

        public CBoneChannelPlayer(CAnimationPlayer animationPlayer) => this.animationPlayer = animationPlayer ?? throw new ArgumentNullException(nameof(animationPlayer));
        public CBoneChannelPlayer() { }

        public void Update()
        {
            if (animationPlayer.PlaybackDirection == 0) return;
            if (Channel == null) return;

            if (Channel.Scales.Count > 1)
            {
                channel.Scales.CalculateInterpolatedFrameData(animationPlayer, ref currentScaleFrame, out Vector3 nextValue, out float tweenScalar);
                InterpolatedScale = Vector3.SmoothStep(currentScaleFrame.Value, nextValue, tweenScalar);
            }

            if (Channel.Rotations.Count > 1)
            {
                channel.Rotations.CalculateInterpolatedFrameData(animationPlayer, ref currentRotationFrame, out Quaternion nextValue, out float tweenScalar);
                InterpolatedRotation = Quaternion.Slerp(currentRotationFrame.Value, nextValue, tweenScalar);
            }

            if (Channel.Positions.Count > 1)
            {
                channel.Positions.CalculateInterpolatedFrameData(animationPlayer, ref currentPositionFrame, out Vector3 nextValue, out float tweenScalar);
                InterpolatedPosition = Vector3.SmoothStep(currentPositionFrame.Value, nextValue, tweenScalar);
            }

            InterpolatedSRT = new BoneSRT
            {
                Scale = InterpolatedScale,
                Rotation = InterpolatedRotation,
                Translation = InterpolatedPosition
            };
        }

        public void ResetToFirstFrame()
        {
            currentScaleFrame = Channel == null ? new Keyframe<Vector3>(0, 0, Vector3.One) : Channel.Scales.Keyframes[0];
            currentRotationFrame = Channel == null ? new Keyframe<Quaternion>(0, 0, Quaternion.Identity) : Channel.Rotations.Keyframes[0];
            currentPositionFrame = Channel == null ? new Keyframe<Vector3>(0, 0, Vector3.Zero) : Channel.Positions.Keyframes[0];

            InterpolatedScale = currentScaleFrame.Value;
            InterpolatedRotation = currentRotationFrame.Value;
            InterpolatedPosition = currentPositionFrame.Value;

            InterpolatedSRT = new BoneSRT
            {
                Scale = InterpolatedScale,
                Rotation = InterpolatedRotation,
                Translation = InterpolatedPosition
            };
        }
    }
    [DebuggerDisplay("Channel for {BoneName} bone with {Scales.Count} scales, {Rotations.Count} rotations, and {Positions.Count} positions")]
    public class CBoneChannel
    {
        #region Properties
        /// <summary> The name of the bone that this channel is for. </summary>
        public string BoneName { get; set; }

        /// <summary> The scales channel for the bone. </summary>
        public CChannelComponent<Vector3> Scales { get; set; }

        /// <summary> The rotations channel for the bone. </summary>
        public CChannelComponent<Quaternion> Rotations { get; set; }

        /// <summary> The positions channel for the bone. </summary>
        public CChannelComponent<Vector3> Positions { get; set; }
        #endregion

        #region Constructors
        /// <summary> Creates a new channel with the given animation parameters. </summary>
        /// <param name="boneName"> The name of the bone. </param>
        /// <param name="scaleFrames"> The scale frames. </param>
        /// <param name="rotationFrames"> The rotation frames. </param>
        /// <param name="positionFrames"> The position frames. </param>
        public CBoneChannel(string boneName, IReadOnlyList<Keyframe<Vector3>> scaleFrames, IReadOnlyList<Keyframe<Quaternion>> rotationFrames, IReadOnlyList<Keyframe<Vector3>> positionFrames)
        {
            // Set the name.
            BoneName = boneName;

            // Create the channels.
            Scales = new CChannelComponent<Vector3>(scaleFrames);
            Rotations = new CChannelComponent<Quaternion>(rotationFrames);
            Positions = new CChannelComponent<Vector3>(positionFrames);
        }
        public CBoneChannel() { }
        #endregion
    }
    /// <summary> Holds a collection of <see cref="Keyframe{T}"/>s and handles their retrieval in a performant way suited to interpolation. </summary>
    /// <typeparam name="T"> The type of value that is stored in each keyframe. </typeparam>
    public class CChannelComponent<T> where T : struct
    {
        #region Properties
        public IReadOnlyList<Keyframe<T>> Keyframes { get; }

        public int Count => Keyframes.Count;
        #endregion

        #region Constructors
        public CChannelComponent(IReadOnlyList<Keyframe<T>> keyframes) => Keyframes = keyframes ?? throw new ArgumentNullException(nameof(keyframes));
        public CChannelComponent() { }
        #endregion

        #region Frame Functions
        /// <summary> Calculates the current frame of a playback along with some tweening data. </summary>
        /// <param name="animationPlayer"> The player that is currently playing this channel. </param>
        /// <param name="currentFrame"> The reference to the current frame of the playback. This will be automatically updated to the current frame of playback based on the <see cref="AnimationPlayer.CurrentTime"/>. </param>
        /// <param name="nextValue"> The value of the frame directly after the current one, used for tweening. </param>
        /// <param name="tweenScalar"> The value between <c>0</c> and <c>1</c> which is used to lerp between the <paramref name="currentFrame"/> and <paramref name="nextValue"/>. </param>
        public void CalculateInterpolatedFrameData(CAnimationPlayer animationPlayer, ref Keyframe<T> currentFrame, out T nextValue, out float tweenScalar)
        {
            if (Count <= 1) throw new Exception("Cannot lerp with only one frame!");

            int playbackDirection = animationPlayer.PlaybackDirection;
            int durationTicks = animationPlayer.AnimDef.Animation.DurationInTicks;
            int currentWholeTick = animationPlayer.CurrentWholeTick;
            float currentTick = animationPlayer.CurrentTick;

            int tickDistance = measureFrameDistance(playbackDirection, durationTicks, currentFrame.TickTime, currentWholeTick);

            Keyframe<T> nextFrame = getFrameWrapped(currentFrame.Index, playbackDirection);
            int currentDistance = measureFrameDistance(playbackDirection, durationTicks, currentFrame.TickTime, nextFrame.TickTime);

            while (tickDistance >= currentDistance)
            {
                currentFrame = nextFrame;
                nextFrame = getFrameWrapped(currentFrame.Index, playbackDirection);
                currentDistance += measureFrameDistance(playbackDirection, durationTicks, currentFrame.TickTime, nextFrame.TickTime);
            }

            float wrappedCurrentTime = currentTick;
            if (playbackDirection == -1 && currentTick == 0) wrappedCurrentTime = durationTicks;
            else if (playbackDirection == 1 && currentTick == durationTicks) wrappedCurrentTime = 0;

            tweenScalar = measureFrameDistance(playbackDirection, durationTicks, currentFrame.TickTime, wrappedCurrentTime)
                        / measureFrameDistance(playbackDirection, durationTicks, currentFrame.TickTime, nextFrame.TickTime);

            nextValue = nextFrame.Value;
        }

        /// <summary> Gets the next frame in the sequence based on the <paramref name="playbackDirection"/> and handles wrapping so that a valid frame is always returned. </summary>
        /// <param name="index"> The index of the frame from which the next frame should be found. </param>
        /// <param name="playbackDirection"> The direction of the animation's playback. </param>
        /// <returns> The next frame in the sequence. </returns>
        private Keyframe<T> getFrameWrapped(int index, int playbackDirection)
            => playbackDirection == -1
                ? index - 1 < 0 ? Keyframes[Keyframes.Count - 1] : Keyframes[index - 1]
                : index + 1 >= Count ? Keyframes[0] : Keyframes[index + 1];

        private static float measureFrameDistance(int playbackDirection, int durationTicks, float firstTick, float secondTick)
            => playbackDirection == -1
                ? secondTick > firstTick ? durationTicks - secondTick + firstTick : firstTick - secondTick
                : secondTick < firstTick ? durationTicks - firstTick + secondTick : secondTick - firstTick;
        private static int measureFrameDistance(int playbackDirection, int durationTicks, int firstTick, int secondTick)
            => (int)measureFrameDistance(playbackDirection, durationTicks, (float)firstTick, secondTick);

        #endregion
    }
    [DebuggerDisplay("{Name} with {ChannelCount} channels taking {DurationInSeconds} seconds.")]
    public class CAnimation
    {
        #region Properties
        /// <summary> The name of this animation. </summary>
        public string Name { get; set; }

        /// <summary> How many ticks (frames) long this animation is. </summary>
        public int DurationInTicks { get; set; }

        /// <summary> How many seconds long this animation is. </summary>
        public float DurationInSeconds => (float)DurationInTicks / TicksPerSecond;

        /// <summary> How many ticks (frames) per second this animation plays at at 100% speed. </summary>
        public int TicksPerSecond { get; set; }

        /// <summary> The collection of bone channels keyed by bone name. </summary>
        public IReadOnlyDictionary<string, CBoneChannel> ChannelsByBoneName { get; set; }

        /// <summary> The number of bone channels in this animation. </summary>
        public int ChannelCount => ChannelsByBoneName.Count;
        #endregion

        #region Constructors
        /// <summary> Creates a new animation with the given data. </summary>
        /// <param name="name"> The name of the animation. </param>
        /// <param name="ticksPerSecond"> The playback speed of the animation in ticks. </param>
        /// <param name="durationInTicks"> How long the animation is in ticks. </param>
        /// <param name="channelsByBoneName"> The collection of bone channels. </param>
        public CAnimation(string name, int ticksPerSecond, int durationInTicks, IReadOnlyDictionary<string, CBoneChannel> channelsByBoneName)
        {
            TicksPerSecond = ticksPerSecond;
            ChannelsByBoneName = channelsByBoneName;
            Name = name;
            DurationInTicks = durationInTicks;
        }
        public CAnimation() { }
        #endregion
    }
}

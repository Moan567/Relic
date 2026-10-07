using Chisel;
using Chisel.Collision;
using Chisel.Models;
using Chisel.Models.Data;
using Chisel.Models.Morph;
using Chisel.Utils.Animation;
using Engine.Console;
using Engine.Physics;
using Engine.Rendering;
using Engine.SaveSystem;
using Engine.Scripting.Sound;
using Engine.Sound;
using JoltPhysicsSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using RenderingLibrary.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ToolsUtilitiesStandard.Helpers;
using static Chisel.Models.CModel;
using static Microsoft.Xna.Framework.MathHelper;
using BoundingBox = Microsoft.Xna.Framework.BoundingBox;

namespace Engine.Utils
{
    public class CModelDisplay : Displayable
    {
        public static int LODOffset = 0;

        public static ShaderHandle DecalShader;
        public static Matrix LightViewMatrix, LightProjectionMatrix;

        public CModel Model;
        public CModelAnimator Animator
        {
            get => animator;
            set
            {
                animator = value;
                if (animator != null)
                    animator.OnAnimEvent = HandleAnimEvent;
            }
        }
        public CMorphAnimator MorphAnimator
        {
            get
            {
                return morphAnimator;
            }
        }
        private CModelAnimator animator;
        private CMorphState morphState;
        private CMorphAnimator morphAnimator;

        private SoundInstance morphSoundID;

        private OrientedBoundingBox meshBounds;

        private Quaternion previousProjectedAngle = new Quaternion(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue);
        private RenderTarget2D shadow;
        private BlendState transparentPrepass;
        private bool noLight = false;
        private bool skinned = false;
        int frame = 10;
        public bool IsRagdollAsleep => ragdollAsleep;
        public bool IsRagdoll { get; private set; } = false;
        public bool TracksPreviousBoneState = true;

        private ShaderHandle EyeballShader;

        public float MaxEyeRotationDegrees = 45f;

        public float EyeTurnRate = 18f;

        private const int MaxShaderEyes = 4;
        private static readonly Vector4[] eyeCenterRadiusScratch = new Vector4[MaxShaderEyes];
        private static readonly Vector3[] eyeForwardScratch = new Vector3[MaxShaderEyes];
        private static readonly Vector3[] eyeRightScratch = new Vector3[MaxShaderEyes];
        private static readonly Vector3[] eyeUpScratch = new Vector3[MaxShaderEyes];
        private static readonly float[] eyeThetaFOVScratch = new float[MaxShaderEyes];

        private static ShaderHandle eyeTextureGenShader;
        private static readonly Dictionary<EyeTextureRecipe, (Texture2D Color, Texture2D Data)> eyeTextureCache = new();

        private Texture2D eyeColorTexture;
        private Texture2D eyeDataTexture;
        private bool hasEyes;

        private Vector3? eyeLookTargetWorld;
        private Vector3[] eyeCurrentLocalDir;

        private int currentLodLevel = 0;
        private int previousLodLevel = -1;

        private readonly Dictionary<string, (Matrix Target, float Weight)> ikOverrides = new();
        private readonly HashSet<string> ikOverridesSetThisTick = new();


        public ImmutableDictionary<int, BodyID> RagdollBones;
        private ImmutableDictionary<int, int> ragdollPhysicsParents;
        private List<Constraint> physicsConstraints;
        private Matrix[] ragdollCustomTransforms;
        private Vector3[] ragdollBoneScales;
        private GroupFilterTable ragdollGroupFilter;

        private Matrix[] boneOffsetCache;
        private Matrix[] boneInverseOffsetCache;
        private Matrix[] childLocalFromParentCache;
        private int[] physicsBoneIndices;
        private BodyID[] physicsBoneIDs;
        public OrientedBoundingBox[] BoneBounds { get; private set; }

        private bool[] isPhysicsBone;
        private int[] boneToPhysicsParent;

        private Matrix[] previousBoneTransforms;
        private Matrix[] currentBoneTransforms;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private Vector3 ragdollOrigin;

        private bool ragdollAsleep;

        // slots
        private readonly Dictionary<string, CModelDisplay> slots = new();
        private CModelDisplay attachParent;
        private string attachmentName;

        // bone maps, for submodels that need to be animated
        private class BoneMap
        {
            public int LocalBoneIndex;
            public CModelDisplay TargetDisplay;
            public int TargetBoneIndex;
        }

        private readonly List<BoneMap> boneMaps = new();

        // visibility
        public bool Visible = true;
        private readonly HashSet<string> hiddenBodygroups = new();

        // custom events
        public Action<string, string> OnCustomEvent;  // slot, options

        public CModelDisplay(string path) : base(null, 0)
        {
            path = Path.ChangeExtension(Path.Combine(MainEngine.Instance.Content.RootDirectory, path), ".ccmdl");

            Model = AssetManager.GetModelInstance(path, path);

            shadow = new RenderTarget2D(MainEngine.Instance.GraphicsDevice, 64, 64, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PlatformContents);

            skinned = Model.Bodygroups.Any(s => s.IsSkinned);
            var shader = skinned ? (ShaderHandle)AssetManager.GetAsset("skinnedModelDefaultShader") : (ShaderHandle)AssetManager.GetAsset("modelDefaultShader");
            this.Shader = shader;

            transparentPrepass = new BlendState
            {
                ColorWriteChannels = ColorWriteChannels.None,
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.One,
                AlphaDestinationBlend = Blend.One,
            };


            if (!skinned)
            {
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);

                foreach (var vert in Model.Bodygroups[0].MeshData.Vertices)
                {
                    min = Vector3.Min(min, vert.Position);
                    max = Vector3.Max(max, vert.Position);
                }
                meshBounds = new OrientedBoundingBox(min - Vector3.One * 0.5f, max + Vector3.One * 0.5f);
            }

            // This allows morphs to work
            CMorphApplicatorFactory.EnsureMorphApplicators(Model, MainEngine.Instance.GraphicsDevice);

            morphState ??= new CMorphState();
            morphState.Initialize(Model);

            morphAnimator = new CMorphAnimator(morphState);

            if (skinned)
            {
                BoneBounds = new OrientedBoundingBox[Model.Bones.Count];

                for(int i = 0; i < Model.Bones.Count; i++)
                {
                    var min = Model.Bones[i].BoundsCenter - Model.Bones[i].BoundsSize;
                    var max = Model.Bones[i].BoundsCenter + Model.Bones[i].BoundsSize;

                    BoneBounds[i] = new OrientedBoundingBox(min, max);
                }    
            }

            hasEyes = skinned && Model.EyeDefs != null && Model.EyeDefs.Count > 0 && Model.Bodygroups.Any(b => b.IsEye);
            if (hasEyes)
            {
                EyeballShader = (ShaderHandle)AssetManager.GetAsset("eyeShader");
                eyeTextureGenShader ??= (ShaderHandle)AssetManager.GetAsset("eyeGenerationShader");
                EnsureEyeTextures();

                eyeCurrentLocalDir = new Vector3[Model.EyeDefs.Count];
                for (int i = 0; i < eyeCurrentLocalDir.Length; i++) eyeCurrentLocalDir[i] = Vector3.Forward;
            }

            for (int i = 0; i < Model.Bodygroups.Count; i++)
            {
                TextureMipGenerator.ReserveMaterial(Model.Bodygroups[i].MaterialID);
            }
        }
        private void EnsureEyeTextures()
        {
            var recipe = Model.EyeTexture;
            if (eyeTextureCache.TryGetValue(recipe, out var cached)
                && !cached.Color.IsDisposed && !cached.Data.IsDisposed
                && cached.Color.GraphicsDevice == MainEngine.Instance.GraphicsDevice)
            {
                eyeColorTexture = cached.Color;
                eyeDataTexture = cached.Data;
                return;
            }

            var result = EyeTextureGenerator.Generate(MainEngine.Instance.GraphicsDevice, eyeTextureGenShader, recipe);
            eyeTextureCache[recipe] = (result.Color, result.Data);
            eyeColorTexture = result.Color;
            eyeDataTexture = result.Data;
        }
        public CModelAnimator EnsureAnimator()
        {
            Animator ??= new CModelAnimator(Model);
            return animator;
        }

        private void SetBodygroupShaderParams(
            ShaderHandle fx,
            Matrix worldMatrix,
            Matrix viewMatrix,
            Matrix projectionMatrix,
            Matrix bodygroupWorld,
            Texture2D mainTex,
            Texture2D specTex,
            Texture2D normalTex,
            float shine,
            bool transparent)
        {
            fx.Param("World").SetValue(bodygroupWorld);
            fx.Param("WorldInverseTranspose").SetValue(Matrix.CreateWorld(Vector3.Zero, bodygroupWorld.Forward, bodygroupWorld.Up));
            fx.Param("View").SetValue(viewMatrix);
            fx.Param("Projection").SetValue(projectionMatrix);

            fx.Param("cameraPos").SetValue(RenderEngine.CameraPosition);

            if (skinned && Model.BoneTransforms != null)
                fx.Param("Bones").SetValue(Model.BoneTransforms);

            fx.Param("MainTex").SetValue(mainTex);
            fx.Param("SpecTex").SetValue(specTex);
            fx.Param("NormalTex").SetValue(normalTex);
            fx.Param("shine").SetValue(shine);
            fx.Param("Transparent").SetValue(transparent);

            fx.Param("realtimeLightCount").SetValue(realtimeLightCountCache);
            fx.Param("realtimeLightPositions").SetValue(realtimeLightPositionsCache);
            fx.Param("realtimeLightColors").SetValue(realtimeLightColorsCache);
            fx.Param("realtimeLightSpotData").SetValue(realtimeLightSpotDataCache);

            fx.Param("fogColor").SetValue(MainEngine.Instance.FogColor.ToVector4());
            fx.Param("fogStart").SetValue(MainEngine.Instance.FogBeginDepth);
            fx.Param("fogEnd").SetValue(MainEngine.Instance.FogEndDepth);
            fx.Param("fogIntensity").SetValue(MainEngine.Instance.FogStrength);

            fx.Param("Cubemap").SetValue((noLight ? RenderEngine.BlackTexture : Skybox.GetSkyTexture()));
        }
        private ShaderHandle ResolveBodygroupShader(int materialID)
        {
            if (materialID >= 0 && materialID < GlobalMapData.LoadedMaterials.Length)
            {
                var mat = GlobalMapData.LoadedMaterials[materialID];
                var matShader = (ShaderHandle)mat.Shader;
                if (matShader != null && matShader != MainEngine.Instance.WorldShader)
                {
                    return matShader;
                }
            }
            return this.Shader;
        }
        public void TrackBoneState()
        {
            if (Model.ModelTransforms == null) return;

            previousBoneTransforms ??= new Matrix[Model.Bones.Count];
            currentBoneTransforms ??= new Matrix[Model.Bones.Count];

            previousPosition = Transform.Translation;
            previousRotation = Quaternion.CreateFromRotationMatrix(Transform);

            Array.Copy(Model.ModelTransforms, previousBoneTransforms, previousBoneTransforms.Length);

            for (int i = 0; i < Model.Bones.Count; i++)
            {
                BoneBounds[i].Transformation = previousBoneTransforms[i] * Transform;
            }
        }
        public void BecomeRagdoll(Vector3? entityVelocityAdd = null)
        {
            IsRagdoll = true;

            var ragdollBones = new Dictionary<int, BodyID>();
            var ragdollPhysicsParents = new Dictionary<int, int>();

            var physicsBoneIndexList = new List<int>();
            var physicsBoneIDList = new List<BodyID>();

            physicsConstraints = new List<Constraint>();

            ragdollCustomTransforms = (Matrix[])Model.BoneTransforms?.Clone();

            if(ragdollCustomTransforms == null)
            {
                ragdollCustomTransforms = new Matrix[Model.Bones.Count];
                Array.Fill(ragdollCustomTransforms, Matrix.Identity);
            }

            isPhysicsBone = new bool[Model.Bones.Count];
            boneToPhysicsParent = new int[Model.Bones.Count];

            for (int i = 0; i < boneToPhysicsParent.Length; i++)
            {
                boneToPhysicsParent[i] = -1;
            }

            CModel.CAnimDef bindpose = Model.Animations.Find(a => a.Name.ToLower().Equals("bindpose"));
            Matrix[] bindposeTransforms;

            if (bindpose != null)
            {
                CAnimationPlayer bindposePlayer = new CAnimationPlayer(Model);
                bindposePlayer.AnimDef = bindpose;
                bindposePlayer.IsPlaying = false;
                bindposePlayer.Update(1 / 60f);
                bindposeTransforms = bindposePlayer.ModelSpaceTransforms.ToArray();
            }
            else
            {
                bindposeTransforms = new Matrix[Model.Bones.Count];
                for (int i = 0; i < bindposeTransforms.Length; i++)
                {
                    bindposeTransforms[i] = Matrix.Identity;
                }
            }

            Vector3 entityVelocity = Vector3.Zero;
            Vector3 entityAngularVelocity = Vector3.Zero;

            if (previousBoneTransforms != null)
            {
                entityVelocity = (Transform.Translation - previousPosition) / MainEngine.PreviousFrameDelta;

                Quaternion currentRotation = Quaternion.CreateFromRotationMatrix(Transform);
                Quaternion deltaRotation = currentRotation * Quaternion.Inverse(previousRotation);

                if (Math.Abs(deltaRotation.W) < 1.0f)
                {
                    float angle = 2.0f * (float)Math.Acos(deltaRotation.W);
                    float sinHalfAngle = (float)Math.Sqrt(1.0f - deltaRotation.W * deltaRotation.W);

                    if (sinHalfAngle > 0.001f)
                    {
                        Vector3 axis = new Vector3(
                            deltaRotation.X / sinHalfAngle,
                            deltaRotation.Y / sinHalfAngle,
                            deltaRotation.Z / sinHalfAngle
                        );
                        entityAngularVelocity = axis * (angle / MainEngine.PreviousFrameDelta);
                    }
                }
            }

            var currentTransforms = Model.ModelTransforms;

            ragdollBoneScales = new Vector3[Model.Bones.Count];
            for (int i = 0; i < Model.Bones.Count; i++)
            {
                if (Model.Bones[i].BoundsSize.LengthSquared() > 0)
                {
                    currentTransforms[i].Decompose(out Vector3 s, out _, out _);
                    ragdollBoneScales[i] = s;
                }
                else
                {
                    ragdollBoneScales[i] = Vector3.One;
                }
            }

            ragdollGroupFilter = new GroupFilterTable((uint)Model.Bones.Count);
            uint ragdollGroupID = (uint)this.GetHashCode();

            foreach (var bone in Model.Bones)
            {
                if (bone.BoundsSize.LengthSquared() <= 0) continue;

                var bindTransform = bindposeTransforms[bone.Index] * this.Transform;
                bindTransform.Decompose(out _, out Quaternion bindRotation, out _);

                const float baseDensity = 32000f;
                const float minBoneMass = 8f;
                const float maxBoneMass = 30f;

                Vector3 halfExtents = (bone.BoundsSize / 2) * ragdollBoneScales[bone.Index];
                float boneVolume = halfExtents.X * halfExtents.Y * halfExtents.Z * 8f;

                var shape = new JoltPhysicsSharp.BoxShape(halfExtents.ToNumerics());

                float targetMass = float.Clamp(boneVolume * baseDensity, minBoneMass, maxBoneMass);
                shape.Density = targetMass / boneVolume;

                using var bodySettings = new JoltPhysicsSharp.BodyCreationSettings(
                    shape,
                    Vector3.Transform(bone.BoundsCenter * ragdollBoneScales[bone.Index], bindTransform).ToNumerics(),
                    bindRotation.ToNumerics(),
                    JoltPhysicsSharp.MotionType.Dynamic,
                    PhysicsEngine.Layers.Ragdoll);

                bodySettings.Restitution = 0.1f;
                bodySettings.LinearDamping = 0.04f;
                bodySettings.AngularDamping = 0.2f; 
                bodySettings.Friction = 6;
                bodySettings.UserData = (ulong)(PhysicsUserData.RAYPASS | PhysicsUserData.INTERACTABLE | PhysicsUserData.FLESH);
                bodySettings.CollisionGroup = new CollisionGroup(ragdollGroupFilter, ragdollGroupID, (uint)bone.Index);

                var physicsBodyID = PhysicsEngine.BodyInterface.CreateAndAddBody(bodySettings, JoltPhysicsSharp.Activation.DontActivate);
                PhysicsEngine.AllBodies.Add(physicsBodyID);
                PhysicsEngine.AllShapes.Add(shape);

                ragdollBones.Add(bone.Index, physicsBodyID);
                isPhysicsBone[bone.Index] = true;

                physicsBoneIndexList.Add(bone.Index);
                physicsBoneIDList.Add(physicsBodyID);

                bodySettings.Dispose();
            }

            CBone FindPhysicsParent(CBone bone)
            {
                if (!bone.HasParent) return null;
                if (bone.PhysicsParentOverride != null && ragdollBones.TryGetValue(bone.PhysicsParentOverride.Index, out _))
                    return bone.PhysicsParentOverride;
                if (ragdollBones.TryGetValue(bone.Parent.Index, out _))
                    return bone.Parent;
                return FindPhysicsParent(bone.Parent);
            }

            foreach (var bone in Model.Bones)
            {
                var parent = FindPhysicsParent(bone);
                if (parent == null) continue;

                ragdollGroupFilter.DisableCollision((uint)bone.Index, (uint)parent.Index);

                ragdollPhysicsParents.Add(bone.Index, parent.Index);
                boneToPhysicsParent[bone.Index] = parent.Index;

                if (!ragdollBones.TryGetValue(bone.Index, out var bodyIDA))
                    continue;
                if (!ragdollBones.TryGetValue(parent.Index, out var bodyIDB))
                    continue;

                var childBoneWorldMatrix = bindposeTransforms[bone.Index] * this.Transform;
                var parentBoneWorldMatrix = bindposeTransforms[parent.Index] * this.Transform;

                var jointPositionWorld = childBoneWorldMatrix.Translation.ToNumerics();
                var boneDirection = Vector3.Normalize(childBoneWorldMatrix.Translation - parentBoneWorldMatrix.Translation);
                var twistAxis = boneDirection.ToNumerics();

                var worldFwd = new System.Numerics.Vector3(0, 0, -1f);
                var planeAxisRaw = worldFwd - twistAxis * System.Numerics.Vector3.Dot(twistAxis, worldFwd);
                if (planeAxisRaw.Length() < 0.001f)
                {
                    var worldUp = new System.Numerics.Vector3(0, 1, 0);
                    planeAxisRaw = worldUp - twistAxis * System.Numerics.Vector3.Dot(twistAxis, worldUp);
                }
                var planeAxis = System.Numerics.Vector3.Normalize(planeAxisRaw);

                float boneVolume = ((bone.BoundsSize / 2) * ragdollBoneScales[bone.Index]).X *
                                    ((bone.BoundsSize / 2) * ragdollBoneScales[bone.Index]).Y *
                                    ((bone.BoundsSize / 2) * ragdollBoneScales[bone.Index]).Z * 8f;

                const float RagdollJointFriction = 5000f;

                var settings = new SwingTwistConstraintSettings
                {
                    Space = ConstraintSpace.WorldSpace,
                    Position1 = jointPositionWorld,
                    Position2 = jointPositionWorld,
                    TwistAxis1 = twistAxis,
                    TwistAxis2 = twistAxis,
                    PlaneAxis1 = planeAxis,
                    PlaneAxis2 = planeAxis,
                    NormalHalfConeAngle = bone.JointNormalHalfCone,
                    PlaneHalfConeAngle = bone.JointPlaneHalfCone,
                    TwistMinAngle = bone.JointTwistMin,
                    TwistMaxAngle = bone.JointTwistMax,
                    MaxFrictionTorque = boneVolume * RagdollJointFriction,
                };

                var constraint = new SwingTwistConstraint(settings, PhysicsEngine.GetFromBodyID(bodyIDA), PhysicsEngine.GetFromBodyID(bodyIDB));
                PhysicsEngine.ConstraintsToAdd.Enqueue(constraint);
                physicsConstraints.Add(constraint);
            }

            boneOffsetCache = new Matrix[Model.Bones.Count];
            boneInverseOffsetCache = new Matrix[Model.Bones.Count];
            for (int i = 0; i < Model.Bones.Count; i++)
            {
                boneOffsetCache[i] = Model.Bones[i].Offset;
                boneInverseOffsetCache[i] = Matrix.Invert(boneOffsetCache[i]);
            }

            childLocalFromParentCache = new Matrix[Model.Bones.Count];
            for (int i = 0; i < Model.Bones.Count; i++)
            {
                if (!isPhysicsBone[i] && boneToPhysicsParent[i] != -1)
                {
                    childLocalFromParentCache[i] = boneInverseOffsetCache[i] * boneOffsetCache[boneToPhysicsParent[i]];
                }
            }

            foreach (var bone in Model.Bones)
            {
                if (!ragdollBones.TryGetValue(bone.Index, out var bodyID))
                    continue;

                var worldTransform = currentTransforms[bone.Index] * this.Transform;
                worldTransform.Decompose(out _, out Quaternion currentRotation, out _);

                var worldPosition = Vector3.Transform(bone.BoundsCenter, worldTransform);

                Vector3 boneVelocity = entityVelocity;
                Vector3 boneAngularVelocity = entityAngularVelocity;

                if (previousBoneTransforms != null)
                {
                    var prevWorldTransform = previousBoneTransforms[bone.Index] *
                                            Matrix.CreateFromQuaternion(previousRotation) *
                                            Matrix.CreateTranslation(previousPosition);
                    var prevWorldPosition = Vector3.Transform(bone.BoundsCenter, prevWorldTransform);

                    boneVelocity = (worldPosition - prevWorldPosition) / MainEngine.PreviousFrameDelta;

                    prevWorldTransform.Decompose(out _, out Quaternion prevRotation, out _);
                    Quaternion boneDeltaRotation = currentRotation * Quaternion.Inverse(prevRotation);

                    if (Math.Abs(boneDeltaRotation.W) < 1.0f)
                    {
                        float angle = 2.0f * (float)Math.Acos(boneDeltaRotation.W);
                        float sinHalfAngle = (float)Math.Sqrt(1.0f - boneDeltaRotation.W * boneDeltaRotation.W);

                        if (sinHalfAngle > 0.001f)
                        {
                            Vector3 axis = new Vector3(
                                boneDeltaRotation.X / sinHalfAngle,
                                boneDeltaRotation.Y / sinHalfAngle,
                                boneDeltaRotation.Z / sinHalfAngle
                            );
                            boneAngularVelocity = axis * (angle / MainEngine.PreviousFrameDelta);
                        }
                    }
                }
                boneVelocity += entityVelocityAdd ?? Vector3.Zero;

                PhysicsEngine.BodyInterface.ActivateBody(bodyID);

                PhysicsEngine.BodyInterface.SetPositionRotationAndVelocity(
                    bodyID,
                    worldPosition.ToNumerics(),
                    currentRotation.ToNumerics(),
                    boneVelocity.ToNumerics(),
                    boneAngularVelocity.ToNumerics()
                );
            }

            ragdollAsleep = false;

            physicsBoneIndices = physicsBoneIndexList.ToArray();
            physicsBoneIDs = physicsBoneIDList.ToArray();

            this.ragdollPhysicsParents = ragdollPhysicsParents.ToImmutableDictionary();
            this.RagdollBones = ragdollBones.ToImmutableDictionary();

            Update();
        }
        public Matrix[] GetRagdollMatrices()
        {
            if (!IsRagdoll || ragdollCustomTransforms == null)
                return null;

            var result = new Matrix[ragdollCustomTransforms.Length];
            Array.Copy(ragdollCustomTransforms, result, result.Length);
            return result;
        }
        public void SetRagdollMatrices(Matrix[] saved)
        {
            if (saved == null || saved.Length != Model.Bones.Count)
            {
                Logger.AppendError("Invalid ragdoll matrix array");
                return;
            }

            if (!IsRagdoll || RagdollBones == null)
                return;

            var entityTransform = Transform;

            for (int i = 0; i < Model.Bones.Count; i++)
            {
                if (!RagdollBones.TryGetValue(i, out var bodyID))
                    continue;

                var bone = Model.Bones[i];

                var boneWorld =
                    Matrix.Invert(bone.Offset) *
                    saved[i] *
                    entityTransform;

                boneWorld.Decompose(out var scale, out var rot, out var pos);

                var boneScale = ragdollBoneScales[i];
                var offset = Vector3.Transform(bone.BoundsCenter * boneScale,
                                               Matrix.CreateFromQuaternion(rot));

                var bodyPos = pos + offset;

                PhysicsEngine.BodyInterface.SetPositionAndRotation(
                    bodyID,
                    bodyPos.ToNumerics(),
                    rot.ToNumerics(),
                    Activation.Activate
                );

                PhysicsEngine.BodyInterface.SetLinearVelocity(bodyID, System.Numerics.Vector3.Zero);
                PhysicsEngine.BodyInterface.SetAngularVelocity(bodyID, System.Numerics.Vector3.Zero);
            }

            ragdollAsleep = false;
            Update();
        }
        public new void Dispose()
        {
            foreach (var slot in slots)
            {
                slot.Value.Dispose();
            }
            slots.Clear();

            IsRagdoll = false;
            if (physicsConstraints != null)
            {
                foreach (var c in physicsConstraints)
                {
                    PhysicsEngine.PhysicsSystem.RemoveConstraint(c);
                }
            }
            if (RagdollBones != null)
            {
                foreach (var bone in RagdollBones)
                {
                    PhysicsEngine.AllBodies.Remove(bone.Value);
                    PhysicsEngine.BodyInterface.RemoveAndDestroyBody(bone.Value);
                }
            }

            ragdollGroupFilter?.Dispose();
           
            base.Dispose();
        }
        public override void CheckForLights(Vector3 position, float ambientIntensity = 1)
        {
            if (!IsRagdoll) base.CheckForLights(position, ambientIntensity);
            else base.CheckForLights(ragdollOrigin, ambientIntensity);
        }
        public override void Update(Vector3? overrideShadowProjectionPoint = null)
        {
            base.Update(IsRagdoll ? ragdollOrigin : overrideShadowProjectionPoint);

            if (Model?.LODLevels != null && Model.LODLevels.Count > 0)
            {
                float distSq = Vector3.DistanceSquared(RenderEngine.CameraPosition, IsRagdoll ? ragdollOrigin : Transform.Translation);
                int level = 0;
                for (int i = 0; i < Model.LODLevels.Count; i++)
                {
                    float d = Model.LODLevels[i].Distance;
                    if (distSq >= d * d) level = i + 1;
                    else break;
                }
                currentLodLevel = level;
            }
            else
            {
                currentLodLevel = 0;
            }

            // Can't be lower than the LOD bias
            currentLodLevel = int.Max(currentLodLevel, LODOffset);

            bool lodChanged = currentLodLevel != previousLodLevel;
            previousLodLevel = currentLodLevel;

            if (skinned && !IsRagdoll) animator?.Update(MainEngine.PreviousFrameDelta);
            if (skinned && IsRagdoll)
            {
                var invTransform = Matrix.Invert(Transform);
                ragdollAsleep = true;
                bool originSet = false;

                for (int idx = 0; idx < physicsBoneIndices.Length; idx++)
                {
                    int boneIndex = physicsBoneIndices[idx];
                    var bodyID = physicsBoneIDs[idx];

                    var pos = PhysicsEngine.BodyInterface.GetPosition(bodyID).ToXNA();
                    var rot = PhysicsEngine.BodyInterface.GetRotation(bodyID);

                    if (PhysicsEngine.BodyInterface.IsActive(bodyID)) ragdollAsleep = false;

                    if (!originSet)
                    {
                        ragdollOrigin = pos;
                        originSet = true;
                    }

                    var boneScale = ragdollBoneScales[boneIndex];
                    var rotMatrix = Matrix.CreateFromQuaternion(rot);
                    pos -= Vector3.Transform(Model.Bones[boneIndex].BoundsCenter * boneScale, rotMatrix);

                    var matrix = Matrix.CreateScale(boneScale) * rotMatrix * Matrix.CreateTranslation(pos) * invTransform;
                    ragdollCustomTransforms[boneIndex] = Model.Bones[boneIndex].Offset * matrix;
                }

                if (!ragdollAsleep)
                {
                    int boneCount = Model.Bones.Count;
                    unsafe
                    {
                        fixed (bool* pIsPhysicsBone = isPhysicsBone)
                        fixed (int* pBoneToParent = boneToPhysicsParent)
                        fixed (Matrix* pOffset = boneOffsetCache)
                        fixed (Matrix* pInverseOffset = boneInverseOffsetCache)
                        fixed (Matrix* pChildLocal = childLocalFromParentCache)
                        fixed (Matrix* pCustomTransforms = ragdollCustomTransforms)
                        {
                            for (int i = 0; i < boneCount; i++)
                            {
                                if (pIsPhysicsBone[i]) continue;

                                int parent = pBoneToParent[i];
                                if (parent == -1) continue;

                                Matrix parentWorldMatrix = pInverseOffset[parent] * pCustomTransforms[parent];
                                Matrix childWorldMatrix = pChildLocal[i] * parentWorldMatrix;

                                pCustomTransforms[i] = pOffset[i] * childWorldMatrix;
                            }
                        }
                    }
                }
            }

            if (skinned && TracksPreviousBoneState) TrackBoneState();

            if (attachParent != null && !string.IsNullOrEmpty(attachmentName)
                && attachParent.Model?.ModelTransforms != null)
            {
                var points = attachParent.Model.AttachmentPoints;
                if (points != null)
                {
                    for (int i = 0; i < points.Count; i++)
                    {
                        if (points[i].Name == attachmentName)
                        {
                            Transform = points[i].GetTransform(attachParent.Model.ModelTransforms) * attachParent.Transform;
                            break;
                        }
                    }
                }
            }

            foreach (var slot in slots.Values)
                slot.Update();

            ApplyBoneMaps();
            if(!IsRagdoll) ApplyIK();

            if ((!IsRagdoll || lodChanged) && Model != null && (morphState?.IsDirty == true || lodChanged))
            {
                foreach (var bg in Model.Bodygroups)
                {
                    var (_, lodMeshData, lodMorphTargets, lodHidden) = CLodResolver.Resolve(bg, currentLodLevel);
                    if (lodHidden || lodMeshData.Vertices == null) continue;

                    if (bg.MorphApplicator != null && lodMorphTargets?.Count > 0)
                        bg.MorphApplicator.Apply(lodMeshData.Vertices, lodMorphTargets, morphState);
                }
                morphState.ClearDirty();
            }
            if (!IsRagdoll)
            {
                morphAnimator.Update(MainEngine.PreviousFrameDelta);
            }

            if (skinned && meshBounds.Size == Vector3.Zero)
            {
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);

                foreach (var vert in Model.Bodygroups[0].MeshData.Vertices)
                {
                    min = Vector3.Min(min, vert.Position);
                    max = Vector3.Max(max, vert.Position);
                }
                meshBounds = new OrientedBoundingBox(min - Vector3.One * 0.5f, max + Vector3.One * 0.5f);
            }
        }
        public void PlayMorphAnimation(string path, Vector3 audioOrigin, bool playSound = true)
        {
            var morphPath = Path.Combine(MainEngine.FullPath, path);
            var anim = CMorphAnimData.LoadFromFile(morphPath);
            var audioPath = CMorphAnimData.ResolveAudioPath(morphPath, anim.AudioPath);

            if (playSound) morphSoundID = SoundScriptManager.PlaySound(audioPath, audioOrigin);

            morphAnimator.PlayAnimation(anim);
        }
        public void UpdatePlayingSound(Vector3 audioOrigin)
        {
            if (morphSoundID == null) return;

            morphSoundID.SourcePosition = audioOrigin;
        }
        private void UpdateEyeAim()
        {
            if (Model.ModelTransforms == null || Model.EyeDefs == null || eyeCurrentLocalDir == null) return;

            float dt = MainEngine.PreviousFrameDelta;

            for (int i = 0; i < Model.EyeDefs.Count; i++)
            {
                var eye = Model.EyeDefs[i];
                if (eye.HeadBoneID < 0 || eye.HeadBoneID >= Model.ModelTransforms.Length)
                    continue;

                Matrix headBoneWorld = Model.ModelTransforms[eye.HeadBoneID] * Transform;
                Matrix invHeadBone = Matrix.Invert(headBoneWorld);

                Vector3 targetLocalDir = Vector3.Forward;

                if (eyeLookTargetWorld != null)
                {
                    Matrix restWorld = eye.HeadOffsetMatrix * headBoneWorld;
                    Vector3 dirToTarget = eyeLookTargetWorld.Value - restWorld.Translation;
                    if (dirToTarget.LengthSquared() > 0.0001f)
                    {
                        dirToTarget.Normalize();
                        targetLocalDir = Vector3.Normalize(Vector3.TransformNormal(dirToTarget, invHeadBone));
                    }
                }

                Vector3 restForwardLocal = -Vector3.Forward;
                float maxAngleRad = Microsoft.Xna.Framework.MathHelper.ToRadians(MaxEyeRotationDegrees);

                float cosAngle = Math.Clamp(Vector3.Dot(restForwardLocal, targetLocalDir), -1f, 1f);
                Vector3 perp = targetLocalDir - restForwardLocal * cosAngle;
                float perpLen = perp.Length();
                float angle = MathF.Atan2(perpLen, cosAngle);

                if (angle > maxAngleRad)
                {
                    Vector3 perpDir = perpLen > 1e-5f
                        ? perp / perpLen
                        : Vector3.Normalize(Vector3.Cross(restForwardLocal,
                            MathF.Abs(restForwardLocal.Y) < 0.99f ? Vector3.Up : Vector3.Right));

                    targetLocalDir = restForwardLocal * MathF.Cos(maxAngleRad) + perpDir * MathF.Sin(maxAngleRad);
                }

                float t = 1f - MathF.Exp(-EyeTurnRate * dt);
                eyeCurrentLocalDir[i] = Vector3.Normalize(Vector3.Lerp(eyeCurrentLocalDir[i], targetLocalDir, t));

                Vector3 localDir = eyeCurrentLocalDir[i];

                Vector3 localUp = Vector3.Normalize(Vector3.TransformNormal(Vector3.Up, invHeadBone));
                if (MathF.Abs(Vector3.Dot(localDir, localUp)) > 0.999f)
                    localUp = Vector3.Normalize(Vector3.TransformNormal(Vector3.Right, invHeadBone));

                if (!IsFiniteDirection(localDir) || localDir.LengthSquared() < 1e-8f
                    || !IsFiniteDirection(localUp) || localUp.LengthSquared() < 1e-8f)
                {
                    localDir = Vector3.Forward;
                    localUp = Vector3.Up;
                    eyeCurrentLocalDir[i] = localDir;
                }

                Matrix candidate = Matrix.CreateWorld(Vector3.Zero, localDir, localUp);

                if (IsFiniteMatrix(candidate))
                    eye.EyeMatrix = candidate;
            }
        }
        private static bool IsFiniteDirection(Vector3 v)
        {
            return !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsNaN(v.Z)
                && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y) && !float.IsInfinity(v.Z);
        }

        private static bool IsFiniteMatrix(Matrix m)
        {
            return !float.IsNaN(m.M11) && !float.IsNaN(m.M12) && !float.IsNaN(m.M13) && !float.IsNaN(m.M14)
                && !float.IsNaN(m.M21) && !float.IsNaN(m.M22) && !float.IsNaN(m.M23) && !float.IsNaN(m.M24)
                && !float.IsNaN(m.M31) && !float.IsNaN(m.M32) && !float.IsNaN(m.M33) && !float.IsNaN(m.M34)
                && !float.IsNaN(m.M41) && !float.IsNaN(m.M42) && !float.IsNaN(m.M43) && !float.IsNaN(m.M44);
        }
        public static Vector3 GetBoundsSizeProjected(OrientedBoundingBox bounds)
        {
            var corners = bounds.GetCorners();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var c in corners)
            {
                var transformed = Vector3.Transform(c, LightViewMatrix);

                min = Vector3.Min(min, transformed);
                max = Vector3.Max(max, transformed);
            }

            return max - min;
        }
        public static bool QuaternionsTooClose(Quaternion a, Quaternion b) => (float.Abs(a.X - b.X) < 0.01f) && (float.Abs(a.Y - b.Y) < 0.01f) &&
                                                                              (float.Abs(a.Z - b.Z) < 0.01f) && (float.Abs(a.W - b.W) < 0.01f);
        public void RenderShadowTexture(WorldEntity entityFrom, OrientedBoundingBox? overrideBounds = null)
        {

            if (shadowDecalIndex != -1)
            {
                float min = float.Min(MainEngine.Instance.AmbientSkylightStrength, MainEngine.Instance.DirectionalLightStrength);
                float max = float.Max(MainEngine.Instance.AmbientSkylightStrength, MainEngine.Instance.DirectionalLightStrength);
                DecalShader.Param("ShadowColor").SetValue(MainEngine.Instance.AmbientSkyColor.ToVector3() * (min / max));
                DecalManager.RealtimeDecals[shadowDecalIndex].customShader = DecalShader;
                DecalManager.RealtimeDecals[shadowDecalIndex].permanent = true;
            }

            if (frame > 0)
            {
                frame--;
                ForceShadowReprojection = true;
                goto renderShadow;
            }

            if (!CurrentShadowQuality.rendertarget)
            {
                if (ShadowTexture != RenderEngine.BlobShadowTexture)
                {
                    ShadowTexture = RenderEngine.BlobShadowTexture;
                    Reproject();
                }
                return;
            }

            if (ForceShadowReprojection) goto renderShadow;

            if (IsRagdoll && ragdollAsleep && !ForceShadowReprojection) return;

            if (QuaternionsTooClose(previousProjectedAngle, entityFrom.Rotation) && !skinned) return;

            renderShadow:

            LightViewMatrix = (Matrix.CreateLookAt(Vector3.Zero, GetLightDir(), Vector3.Up));
            var entitySize = GetBoundsSizeProjected(overrideBounds ?? entityFrom.OrientedBounds);

            float width = (entitySize.X + 1);
            float height = (entitySize.Y + 1);

            if (IsRagdoll)
            {
                width = 3f;
                height = 3f;
            }

            shadowBounds.Extents.X = width / 2;
            shadowBounds.Extents.Y = height / 2;

            LightProjectionMatrix = Matrix.CreateOrthographic(width, height, -25, 25);

            if (shadow.Width != CurrentShadowQuality.res || ForceShadowReprojection)
            {
                shadow = new RenderTarget2D(MainEngine.Instance.GraphicsDevice, CurrentShadowQuality.res, CurrentShadowQuality.res, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PlatformContents);
            }

            MainEngine.Instance.GraphicsDevice.SetRenderTarget(shadow);
            MainEngine.Instance.GraphicsDevice.Clear(Color.White);

            noLight = true;

            if (IsRagdoll) Transform.Translation = Transform.Translation - shadowBounds.Transformation.Translation;
            else Transform.Translation = Vector3.Zero;

            DrawModelBlack();
            foreach (var slot in slots.Values)
            {
                slot.DrawModelBlack();
            }

            if (shadow.Width != PreviousShadowQuality.res || ForceShadowReprojection)
            {
                Reproject();
            }

            ShadowTexture = shadow;

            previousProjectedAngle = entityFrom.Rotation;
            ForceShadowReprojection = false;
        }

        private void DrawModelBlack()
        {
            if (!Visible) return;
            if (attachParent != null && !string.IsNullOrEmpty(attachmentName)
                && attachParent.Model?.ModelTransforms != null)
            {
                var att = attachParent.Model.AttachmentPoints
                              ?.Find(a => a.Name == attachmentName);
                if (att != null)
                {

                    Transform = att.GetTransform(attachParent.Model.ModelTransforms)
                                * attachParent.Transform;
                }
            }

            Shader.Param("MainTex").SetValue(RenderEngine.BlackTexture);
            Shader.Param("SpecTex").SetValue(RenderEngine.BlankSpecTexture);
            DrawModel(LightViewMatrix, LightProjectionMatrix, Matrix.Identity, true);
        }

        /// <summary>
        /// Draws the model with the current shader and transforms.
        /// </summary>
        /// <param name="view"></param>
        /// <param name="projection"></param>
        /// <param name="world"></param>
        public void Draw(Matrix? view = null, Matrix? projection = null, Matrix? world = null, bool ignoreFrustum = false)
        {
            if (!Visible) return;
            noLight = false;

            // We have to do this BEFORE rendering, because the shader is shared.
            if (hasEyes) UpdateEyeAim();

            if (attachParent != null && !string.IsNullOrEmpty(attachmentName)
                && attachParent.Model?.ModelTransforms != null)
            {
                var att = attachParent.Model.AttachmentPoints
                              ?.Find(a => a.Name == attachmentName);
                if (att != null)
                {
                    Transform = att.GetTransform(attachParent.Model.ModelTransforms)
                                * attachParent.Transform;
                    CheckForLights(Transform.Translation);
                }
            }

            meshBounds.Transformation = Transform;
            if (RenderEngine.CameraBoundingFrustum.Contains(meshBounds.GetBoundingBox()) == ContainmentType.Disjoint && !ignoreFrustum && !skinned) return;

            DrawModel(view, projection, world);

            foreach (var slot in slots.Values)
            {
                slot.Draw(view, projection, world);
            }

            // Eyes can corrupt this because they define theirs in their shader
            MainEngine.Instance.GraphicsDevice.SamplerStates[0] = RenderEngine.WorldTextureSamplerState;
        }

        void DrawModel(Matrix? view = null, Matrix? projection = null, Matrix? world = null, bool ignoreTextures = false)
        {
            if (!view.HasValue) view = RenderEngine.ViewMatrix;
            if (!world.HasValue) world = RenderEngine.WorldMatrix;
            if (!projection.HasValue) projection = RenderEngine.ProjectionMatrix;

            // Prepare default shader params (needed for shadow pass and non-custom bodygroups)
            PrepareShaderParamsForRendering(world.Value, view.Value, projection.Value, ignoreTextures);

            var old = MainEngine.Instance.GraphicsDevice.RasterizerState;
            var oldDepth = MainEngine.Instance.GraphicsDevice.DepthStencilState;

            MainEngine.Instance.GraphicsDevice.RasterizerState = FlipWinding ? rasterizerStateFlipped : rasterizerState;

            if (Model != null)
            {
                if (skinned)
                {
                    Model.BoneTransforms = IsRagdoll
                        ? ragdollCustomTransforms
                        : animator?.GetFinalTransforms() ?? Model.BoneTransforms;
                }

                if (skinned) Shader.Param("Bones").SetValue(Model.BoneTransforms);

                foreach (var bodygroup in CollectionsMarshal.AsSpan(Model.Bodygroups))
                {
                    if (hiddenBodygroups.Contains(bodygroup.Name)) continue;

                    var (lodMesh, _, lodMorphTargets, lodHidden) = CLodResolver.Resolve(bodygroup, currentLodLevel);
                    if (lodHidden || lodMesh == null) continue;

                    bool useMorph = bodygroup.MorphApplicator != null && lodMorphTargets?.Count > 0;

                    Matrix bodygroupWorld = bodygroup.Offset * Transform * world.Value;
                    bool transparent = GlobalMapData.LoadedMaterials[bodygroup.MaterialID].Transparent;

                    if (transparent)
                    {
                        TransparentRenderQueue.RegisterFreeform(bodygroupWorld.Translation, () =>
                        {
                            MainEngine.Instance.GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                            var oldBS = MainEngine.Instance.GraphicsDevice.BlendState;
                            MainEngine.Instance.GraphicsDevice.BlendState = transparentPrepass;

                            DrawBodygroup(bodygroup, lodMesh, useMorph, bodygroupWorld, world.Value, view.Value, projection.Value, ignoreTextures, false);

                            MainEngine.Instance.GraphicsDevice.BlendState = oldBS;
                            MainEngine.Instance.GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;

                            DrawBodygroup(bodygroup, lodMesh, useMorph, bodygroupWorld, world.Value, view.Value, projection.Value, ignoreTextures, true);
                        });
                    }
                    else
                    {
                        DrawBodygroup(bodygroup, lodMesh, useMorph, bodygroupWorld, world.Value, view.Value, projection.Value, ignoreTextures, false);
                    }
                }
            }

            MainEngine.Instance.GraphicsDevice.RasterizerState = old;
            MainEngine.Instance.GraphicsDevice.DepthStencilState = oldDepth;
        }
        private void SetEyeShaderParams(ShaderHandle fx, Matrix? view = null, Matrix? projection = null, Matrix? world = null)
        {
            if (Model.ModelTransforms == null) return;

            for (int i = 0; i < MaxShaderEyes; i++)
            {
                bool valid = Model.EyeDefs != null && i < Model.EyeDefs.Count
                    && Model.EyeDefs[i].HeadBoneID >= 0 && Model.EyeDefs[i].HeadBoneID < Model.ModelTransforms.Length;

                if (valid)
                {
                    var eyeDef = Model.EyeDefs[i];
                    Matrix headBoneWorld = Model.ModelTransforms[eyeDef.HeadBoneID] * Transform;
                    Matrix eyeWorld = eyeDef.FinalMatrixForEye * headBoneWorld;

                    eyeCenterRadiusScratch[i] = new Vector4(eyeWorld.Translation, eyeDef.EyeRadius);
                    eyeForwardScratch[i] = eyeWorld.Forward;
                    eyeRightScratch[i] = eyeWorld.Right;
                    eyeUpScratch[i] = eyeWorld.Up;
                    eyeThetaFOVScratch[i] = eyeDef.ThetaFOV;
                }
                else
                {
                    eyeCenterRadiusScratch[i] = Vector4.Zero;
                    eyeForwardScratch[i] = Vector3.Forward;
                    eyeRightScratch[i] = Vector3.Right;
                    eyeUpScratch[i] = Vector3.Up;
                    eyeThetaFOVScratch[i] = 0f;
                }
            }

            fx.Param("MainTex").SetValue(eyeColorTexture);
            fx.Param("DataTex").SetValue(eyeDataTexture);
            fx.Param("EyeCenterRadius").SetValue(eyeCenterRadiusScratch);
            fx.Param("EyeForward").SetValue(eyeForwardScratch);
            fx.Param("EyeRight").SetValue(eyeRightScratch);
            fx.Param("EyeUp").SetValue(eyeUpScratch);
            fx.Param("ThetaFOV").SetValue(eyeThetaFOVScratch);
            fx.Param("IrisSize").SetValue(Model.EyeTexture.IrisSize);
            fx.Param("shine").SetValue(25);
        }
        void DrawBodygroup(
            CBodyGroup bodygroup,
            CSkinnedMesh mesh,
            bool useMorph,
            Matrix bodygroupWorld,
            Matrix world,
            Matrix view,
            Matrix projection,
            bool ignoreTextures,
            bool transparent)
        {
            var mat = GlobalMapData.LoadedMaterials[bodygroup.MaterialID];

            bool isEyeBodygroup = bodygroup.IsEye && hasEyes;

            ShaderHandle fx = ignoreTextures
                ? this.Shader
                : isEyeBodygroup
                    ? EyeballShader
                    : ResolveBodygroupShader(bodygroup.MaterialID);

            bool usingCustomShader = (fx != this.Shader);

            bool ignoreCull = mat.NoCull;

            if (ignoreCull)
                MainEngine.Instance.GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            if (isEyeBodygroup)
                fx.SetTechnique("High"); // no eye technique switching yet
            else
                RenderEngine.ApplyMaterialTechnique(mat, fx);

            if (ignoreTextures) fx.SetTechnique("ShadowBlack");

            if (usingCustomShader)
            {
                if(isEyeBodygroup)
                    PrepareShaderParamsForRendering(world,view,projection, fx, true);

                SetBodygroupShaderParams(
                    fx,
                    world,
                    view,
                    projection,
                    bodygroupWorld,
                    ignoreTextures ? RenderEngine.BlackTexture : (RenderEngine.ShowBlankTexture ? RenderEngine.DimTexture : mat.Texture ?? RenderEngine.ErrorTexture),
                    ignoreTextures ? RenderEngine.BlackTexture : (mat.Specular ?? RenderEngine.WhiteTexture),
                    ignoreTextures ? RenderEngine.WhiteTexture : (mat.Normal ?? RenderEngine.WhiteTexture),
                    mat.Reflectivity,
                    transparent
                );

                if (isEyeBodygroup)
                    SetEyeShaderParams(fx, view, projection, world);
            }
            else
            {
                Shader.Param("World").SetValue(bodygroupWorld);
                Shader.Param("WorldInverseTranspose").SetValue(Matrix.CreateWorld(Vector3.Zero, bodygroupWorld.Forward, bodygroupWorld.Up));

                if (!ignoreTextures)
                {
                    Shader.Param("MainTex").SetValue(RenderEngine.ShowBlankTexture
                        ? RenderEngine.DimTexture
                        : mat.Texture);
                    Shader.Param("SpecTex").SetValue(mat.Specular);
                    Shader.Param("NormalTex").SetValue(mat.Normal);
                    Shader.Param("shine").SetValue(mat.Reflectivity);
                }
                Shader.Param("Transparent").SetValue(transparent);
            }

            fx.RenderEachPass(() =>
            {
                if (useMorph)
                    bodygroup.MorphApplicator.Draw(MainEngine.Instance.GraphicsDevice, mesh.IndexBuffer);
                else
                    mesh.Draw();
            });

            if (ignoreCull)
                MainEngine.Instance.GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        }
        public void ApplyImpulseAtPoint(Vector3 worldPoint, Vector3 impulse, float falloff = 2.5f)
        {
            if (!IsRagdoll || RagdollBones == null) return;

            foreach (var kv in RagdollBones)
            {
                var bonePos = PhysicsEngine.BodyInterface.GetPosition(kv.Value).ToXNA();
                float dist = Vector3.Distance(worldPoint, bonePos);
                float strength = 1f / (1f + dist * falloff);

                float mass = PhysicsEngine.BodyInterface.GetShape(kv.Value).MassProperties.Mass;
                float massRatio = float.Clamp(mass / 10f, 0.1f, 1f);

                var scaledImpulse = impulse * strength * massRatio;
                PhysicsEngine.BodyInterface.AddImpulse(kv.Value, scaledImpulse.ToNumerics());

                var randomAxis = Vector3.Normalize(new Vector3(
                    (float)(System.Random.Shared.NextDouble() * 2 - 1),
                    (float)(System.Random.Shared.NextDouble() * 2 - 1),
                    (float)(System.Random.Shared.NextDouble() * 2 - 1)));
                PhysicsEngine.BodyInterface.AddAngularImpulse(kv.Value, (randomAxis * impulse.Length() * strength * massRatio * 3f).ToNumerics());
            }
        }


        /// <summary>
        /// Makes a named bone on THIS model's skeleton continuously match a named bone
        /// on another CModelDisplay's skeleton.
        /// </summary>
        public void MapBoneToBone(string localBoneName, CModelDisplay targetDisplay, string targetBoneName)
        {
            int localIndex = Model.Bones.FindIndex(b => b.Name == localBoneName);
            if (localIndex < 0) return;

            int targetIndex = targetDisplay.Model.Bones.FindIndex(b => b.Name == targetBoneName);
            if (targetIndex < 0) return;

            boneMaps.Add(new BoneMap
            {
                LocalBoneIndex = localIndex,
                TargetDisplay = targetDisplay,
                TargetBoneIndex = targetIndex
            });
        }

        /// <summary>Maps the bone with 'boneName' to an identically named one on the other model.</summary>
        public void MapBoneToBone(string boneName, CModelDisplay targetDisplay) => MapBoneToBone(boneName, targetDisplay, boneName);

        public void UnmapBone(string localBoneName)
        {
            int localIndex = Model.Bones.FindIndex(b => b.Name == localBoneName);
            boneMaps.RemoveAll(m => m.LocalBoneIndex == localIndex);
        }

        /// <summary>
        /// Applies all registered bone maps, overriding those bones' resolved pose to
        /// exactly match their target bone's current world transform.
        /// </summary>
        public void ApplyBoneMaps()
        {
            if (boneMaps.Count == 0 || Model.ModelTransforms == null) return;

            Matrix inverseWorld = Matrix.Invert(Transform);

            foreach (var map in boneMaps)
            {
                var targetModel = map.TargetDisplay.Model;
                if (targetModel.ModelTransforms == null || map.TargetBoneIndex >= targetModel.ModelTransforms.Length) continue;

                Matrix targetBoneWorld = targetModel.ModelTransforms[map.TargetBoneIndex] * map.TargetDisplay.Transform;
                Model.ModelTransforms[map.LocalBoneIndex] = targetBoneWorld * inverseWorld;
            }
        }

        public void SetSlot(string name, CModelDisplay prop) => slots[name] = prop;
        public void ClearSlot(string name) => slots.Remove(name);
        public CModelDisplay GetSlot(string name)
            => slots.TryGetValue(name, out var m) ? m : null;
        public void AttachTo(CModelDisplay parent, string attachmentName)
        {
            this.attachParent = parent;
            this.attachmentName = attachmentName;
        }
        public void DetachFrom()
        {
            attachParent = null;
            attachmentName = null;
        }
        public void SetBodygroupVisible(string name, bool visible)
        {
            if (visible) hiddenBodygroups.Remove(name);
            else hiddenBodygroups.Add(name);
        }
        public void SetBodygroupMaterial(int bodygroupID, string materialName)
        {
            int newIndex = GlobalMapData.MaterialNameToIndex[materialName];
            Model.Bodygroups[bodygroupID].MaterialID = newIndex;
            TextureMipGenerator.ReserveMaterial(newIndex);
        }
        public void SetBodygroupMaterial(string bodygroupName, string materialName)
        {
            var bodygroup = Model.FindBodygroup(bodygroupName);
            int bodygroupID = Model.Bodygroups.IndexOf(bodygroup);
            SetBodygroupMaterial(bodygroupID, materialName);
        }
        public void SetEyeLookTarget(Vector3? worldTarget) => eyeLookTargetWorld = worldTarget;
        private void HandleAnimEvent(CAnimEvent ev)
        {
            if (ev.Type == AnimEventType.Custom)
            {
                OnCustomEvent?.Invoke(ev.Slot ?? "", ev.Options ?? "");
                return;
            }
            // Default to ourselves of there is no slot provided
            CModelDisplay prop;
            if (string.IsNullOrEmpty(ev.Slot)) { prop = this; }
            else
            if (!slots.TryGetValue(ev.Slot, out prop) || prop == null) { prop = this; }

            switch (ev.Type)
            {
                case AnimEventType.Attach:
                    var attachTarget = string.IsNullOrEmpty(ev.TargetSlot)
                        ? this
                        : GetSlot(ev.TargetSlot);
                    if (attachTarget != null)
                        prop.AttachTo(attachTarget, ev.Attachment ?? "");
                    break;

                case AnimEventType.Detach:
                    prop.DetachFrom();
                    break;

                case AnimEventType.SetVisible:
                    prop.Visible = true;
                    break;

                case AnimEventType.SetHidden:
                    prop.Visible = false;
                    break;

                case AnimEventType.SetBodygroupVisible:
                    prop.SetBodygroupVisible(ev.Bodygroup ?? "", true);
                    break;

                case AnimEventType.SetBodygroupHidden:
                    prop.SetBodygroupVisible(ev.Bodygroup ?? "", false);
                    break;

                case AnimEventType.PlaySequence:
                    if (prop?.Animator == null || string.IsNullOrEmpty(ev.Options)) break;

                    var targetSeq = prop.Model.Sequences?.Find(s =>
                        string.Equals(s.Name, ev.Options, StringComparison.OrdinalIgnoreCase));

                    if (targetSeq == null) break;

                    var firstLayer = prop.Animator.Layers.Values.FirstOrDefault();
                    firstLayer?.Play(targetSeq);
                    break;
            }
        }

        public void SetIKTargetOverride(string chainName, Matrix targetModelSpace, float weight = 1f)
        {
            ikOverrides[chainName] = (targetModelSpace, weight);
            ikOverridesSetThisTick.Add(chainName);
        }

        private void ApplyIK()
        {
            if (animator == null || Model.IKChains.Count == 0)
            {
                ikOverridesSetThisTick.Clear();
                return;
            }

            Dictionary<string, (Matrix Target, float Weight)> activeOverrides = null;
            if (ikOverridesSetThisTick.Count > 0)
            {
                activeOverrides = new Dictionary<string, (Matrix, float)>();
                foreach (var name in ikOverridesSetThisTick)
                {
                    activeOverrides[name] = ikOverrides[name];
                }
            }

            CIKSolver.ApplyChains(Model, animator, activeOverrides);

            var staleOverrides = new List<string>();
            foreach (var key in ikOverrides.Keys)
            {
                if (!ikOverridesSetThisTick.Contains(key))
                {
                    staleOverrides.Add(key);
                }
            }
            foreach (var key in staleOverrides)
            {
                ikOverrides.Remove(key);
            }
            ikOverridesSetThisTick.Clear();
        }
    }
}
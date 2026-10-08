using Chisel.Models.Data;
using Chisel.Models.Morph;
using Chisel.Utils.Animation;
using Liru3D.Animations;
using Liru3D.Models;
using Liru3D.Models.Data;
using MessagePack;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chisel.Models
{
    public enum AnimEventType
    {
        Attach,               // parent slot model to an attachment on this model (or TargetSlot)
        Detach,               // unparent slot model, leaves it at its current world position
        SetVisible,           // show entire slot model
        SetHidden,            // hide entire slot model
        SetBodygroupVisible,  // show one bodygroup on a slot model
        SetBodygroupHidden,   // hide one bodygroup on a slot model
        PlaySequence,
        Custom                // freeform, resolved entirely by game code
    }
    public class CModel
    {
        public string Name { get; set; }
        public class CAttachmentPoint
        {
            public string Name { get; set; }
            public int BoneID { get; set; }
            public Matrix Offset { get; set; }
            public Matrix GetTransform(Matrix[] boneTransforms)
            {
                return Offset * boneTransforms[BoneID];
            }
        }
        public class CAnimEvent
        {
            // Which frame of the sequence this fires on

            [EditorField("Frame")]
            public int Frame { get; set; }

            [EditorField("Type")]
            public AnimEventType Type { get; set; }

            // Which slot model this event acts on
            [EditorField("Slot")]
            public string Slot { get; set; }

            // Name of the attachment point to attach to.
            // If TargetSlot is empty, the attachment is looked up on THIS model.
            // If TargetSlot is set, the attachment is looked up on that slot model instead.
            public string Attachment { get; set; }

            [EditorField("On Slot")]
            public string TargetSlot { get; set; }

            // Name of the bodygroup on the slot model to show or hide

            [EditorField("Bodygroup")]
            public string Bodygroup { get; set; }


            [EditorField("Options")]
            public string Options { get; set; }
        }
        public class CAnimDef
        {
            [EditorField("Name")]
            public string Name { get; set; }

            [EditorField("Speed")]
            public float Speed { get; set; } = 1f;
            public CAnimation Animation { get; set; }
            public List<CAnimEvent> Events { get; set; } = new();
            public List<CIKAnimEvent> IKEvents { get; set; } = new();
            public List<string> BoneMask { get; set; } = new();
            public RootMotionConfig RootMotion { get; set; } = new();
        }
        public class RootMotionConfig
        {
            [EditorField("Extract X")]
            public bool ExtractTranslationX { get; set; }

            [EditorField("Extract Y")]
            public bool ExtractTranslationY { get; set; }

            [EditorField("Extract Z")]
            public bool ExtractTranslationZ { get; set; }
        }
        public class CSequence
        {
            public string Name { get; set; }
            public BlendSource Root { get; set; }
        }
        public class CBodyGroup
        {
            public string Name { get; set; }
            public string SourceMeshName { get; set; }
            public CSkinnedMesh Mesh { get; set; }
            public List<CLODMeshEntry> LODMeshes { get; set; } = new();
            public CMeshData MeshData { get; set; }
            public Matrix Offset { get; set; }
            public bool IsSkinned { get; set; }
            public bool IsEye { get; set; }
            public int MaterialID { get; set; }
            public CMorphApplicator MorphApplicator { get; set; }
            public List<CMorphTarget> MorphTargets { get; set; } = new();
        }
        public class CEyeDef
        {
            public string Name { get; set; }
            public float EyeRadius { get; set; }
            public int HeadBoneID { get; set; }
            public Matrix HeadOffsetMatrix { get; set; }
            [IgnoreMember] public Matrix EyeMatrix { get; set; }
            public int SetupBodygroupIndex { get; set; } = -1;
            public int SetupVertexIndex { get; set; } = -1;
            public Vector3 SetupLocalPosition { get; set; }
            public Vector3 SetupLocalNormal { get; set; }

            // Optics
            public float ThetaFOV { get; set; }

            public Matrix FinalMatrixForEye => EyeMatrix * HeadOffsetMatrix;

            public Matrix GetTransform(Matrix[] modelTransforms) => FinalMatrixForEye * modelTransforms[HeadBoneID];
        }
        public class CLODLevel
        {
            [EditorField("Distance")]
            public float Distance { get; set; }
        }
        public class CLODMeshEntry
        {
            public bool Hidden { get; set; }
            public CSkinnedMesh Mesh { get; set; }
            public CMeshData MeshData { get; set; }
            public string SourceMeshName { get; set; }
            public List<CMorphTarget> MorphTargets { get; set; } = new();
        }
        public enum CIKChainRole
        {
            Generic,
            Foot
        }
        public class CIKChain
        {
            public string ChainName;
            public string EndBoneName;
            public CIKChainRole Role;
            public Vector3 EndEffectorOffset;
            public Quaternion LocalOrientationOffset = Quaternion.Identity;
            public Vector3 LocalOrientationOffsetEuler;

            [NonSerialized] public int TopBoneIndex = -1;
            [NonSerialized] public int MidBoneIndex = -1;
            [NonSerialized] public int EndBoneIndex = -1;
            [NonSerialized] public float UpperLength;
            [NonSerialized] public float LowerLength;
        }
        public class CIKAnimEvent
        {
            public string ChainName { get; set; }

            [EditorField("Lock Frame")]
            public int LockFrame { get; set; }

            [EditorField("Free Frame (-1 = end of clip)")]
            public int FreeFrame { get; set; } = -1;

            [EditorField("Fade In (frames)")]
            public int FadeInFrames { get; set; }

            [EditorField("Fade Out (frames)")]
            public int FadeOutFrames { get; set; }

            public string TargetReference { get; set; }

            [EditorField("Use Source Pose")]
            public bool UseSource { get; set; } = true;
        }

        public List<CBodyGroup> Bodygroups { get; set; } = new List<CBodyGroup>();
        public List<CEyeDef> EyeDefs { get; set; } = new List<CEyeDef>();
        public List<CLODLevel> LODLevels { get; set; } = new();
        public EyeTextureRecipe EyeTexture { get; set; } = EyeTextureRecipe.Default;
        public Matrix[] BoneTransforms { get; set; }
        public Matrix[] ModelTransforms { get; set; }
        public List<CBone> Bones { get; set; }
        public List<CBoneData> BoneData { get; set; } = new List<CBoneData>();
        public List<CAttachmentPoint> AttachmentPoints { get; set; } = new List<CAttachmentPoint>();
        public List<CAnimDef> Animations { get; set; } = new List<CAnimDef>();
        public List<CSequence> Sequences { get; set; } = new List<CSequence>();
        public List<CIKChain> IKChains { get; set; } = new();
        public CAnimDef FindAnimation(string name) => Animations?.Find(a => a.Name == name);
        public CSequence FindSequence(string name) => Sequences?.Find(s => s.Name == name);
        public CAttachmentPoint FindAttachment(string name) => AttachmentPoints?.FirstOrDefault(s => s.Name == name);
        public CBodyGroup FindBodygroup(string name) => Bodygroups?.FirstOrDefault(s => s.Name == name);
        public int FindBodygroupID(string name) => Bodygroups?.FindIndex(s => s.Name == name) ?? -1;
        public CEyeDef FindEyeDef(string name) => EyeDefs?.FirstOrDefault(e => e.Name == name);
        public int FindEyeDefID(string name) => EyeDefs?.FindIndex(e => e.Name == name) ?? -1;
        public CIKChain FindIKChain(string name) => IKChains?.Find(c => c.ChainName == name);

        private BoneSRT[] cachedBindPoseLocalSRT;

        /// <summary>
        /// Local-space SRT for every bone in this model's bind pose, cached after the
        /// first call. Used as the fallback for any bone a clip's BoneMask excludes.
        /// </summary>
        public BoneSRT[] GetBindPoseLocalSRTs()
        {
            if (cachedBindPoseLocalSRT != null) return cachedBindPoseLocalSRT;

            int boneCount = Bones.Count;
            var modelSpaceSRT = new BoneSRT[boneCount];

            var bindposePlayer = new CAnimationPlayer(this)
            {
                AnimDef = FindAnimation("bindpose"),
                IsPlaying = false
            };
            bindposePlayer.Update(1f / 60f);

            for (int b = 0; b < boneCount; b++)
            {
                modelSpaceSRT[b] = BoneSRT.FromMatrix(bindposePlayer.ModelSpaceTransforms[b]);
            }

            var result = new BoneSRT[boneCount];
            for (int b = 0; b < boneCount; b++)
            {
                var bone = Bones[b];
                result[b] = bone.HasParent
                    ? BoneSRT.LocalFromModelSpace(modelSpaceSRT[b], modelSpaceSRT[bone.Parent.Index])
                    : modelSpaceSRT[b];
            }

            cachedBindPoseLocalSRT = result;
            return result;
        }

        public void ResolveIKChains()
        {
            var bindPose = GetBindPoseLocalSRTs();

            foreach (var chain in IKChains)
            {
                int endIndex = Bones.FindIndex(b => b.Name == chain.EndBoneName);
                if (endIndex < 0)
                {
                    chain.TopBoneIndex = chain.MidBoneIndex = chain.EndBoneIndex = -1;
                    continue;
                }

                var endBone = Bones[endIndex];
                var midBone = endBone.Parent;
                var topBone = midBone?.Parent;

                if (midBone == null || topBone == null)
                {
                    chain.TopBoneIndex = chain.MidBoneIndex = chain.EndBoneIndex = -1;
                    continue;
                }

                chain.EndBoneIndex = endIndex;
                chain.MidBoneIndex = midBone.Index;
                chain.TopBoneIndex = topBone.Index;
                chain.LowerLength = bindPose[chain.EndBoneIndex].Translation.Length();
                chain.UpperLength = bindPose[chain.MidBoneIndex].Translation.Length();
            }
        }
    }
    public class CSkinnedMesh
    {
        #region Dependencies
        private readonly GraphicsDevice graphicsDevice;
        #endregion

        #region Properties
        /// <summary> The name of the mesh. </summary>
        public string Name { get; private set; }

        /// <summary> The vertex buffer object that contains the vertex data of this mesh. </summary>
        public VertexBuffer VertexBuffer { get; }

        /// <summary> The index buffer object that contains the index data of this mesh. </summary>
        public IndexBuffer IndexBuffer { get; }

        /// <summary> The bounding sphere of the mesh without any animations applied. </summary>
        public BoundingSphere BoundingSphere { get; private set; }
        #endregion

        #region Constructors
        private CSkinnedMesh(GraphicsDevice graphicsDevice, VertexBuffer vertexBuffer, IndexBuffer indexBuffer, BoundingSphere boundingSphere, string name)
        {
            this.graphicsDevice = graphicsDevice ?? throw new System.ArgumentNullException(nameof(graphicsDevice));
            VertexBuffer = vertexBuffer ?? throw new System.ArgumentNullException(nameof(vertexBuffer));
            IndexBuffer = indexBuffer ?? throw new System.ArgumentNullException(nameof(indexBuffer));
            BoundingSphere = boundingSphere;
            Name = name;
        }
        #endregion

        #region Data Functions
        /// <summary>
        /// Updates this mesh's data from the given data.
        /// </summary>
        /// <param name="data"> The data object holding the new data to use. </param>
        public void UpdateDataFrom(SkinnedMeshData data)
        {
            Name = data.Name ?? Name;
            if (data.VertexCount > 0)
            {
                VertexBuffer.SetData(data.Vertices);
                BoundingSphere = data.CalculateBoundingSphere();
            }
            if (data.IndexCount > 0) IndexBuffer.SetData(data.Indices);
        }
        #endregion

        #region Creation Functions
        /// <summary> Creates and returns a new skinned mesh from the given <paramref name="data"/>, uploaded onto the given <paramref name="graphicsDevice"/>. </summary>
        /// <param name="graphicsDevice"> The graphics device onto which the mesh will be uploaded. </param>
        /// <param name="data"> The mesh data. </param>
        /// <returns> The created skinned mesh. </returns>
        public static CSkinnedMesh CreateFrom(GraphicsDevice graphicsDevice, CMeshData data)
        {
            VertexBuffer vertexBuffer = new VertexBuffer(graphicsDevice, CSkinnedVertex.VertexDeclaration, data.Vertices.Length * CSkinnedVertex.VertexDeclaration.VertexStride, BufferUsage.WriteOnly);
            vertexBuffer.SetData(0, data.Vertices, 0, data.Vertices.Length, CSkinnedVertex.VertexDeclaration.VertexStride);

            IndexBuffer indexBuffer = new IndexBuffer(graphicsDevice, IndexElementSize.ThirtyTwoBits, data.Indices.Length, BufferUsage.WriteOnly);
            indexBuffer.SetData(data.Indices);

            CSkinnedMesh skinnedMesh = new CSkinnedMesh(graphicsDevice, vertexBuffer, indexBuffer, data.CalculateBoundingSphere(), data.Name);

            return skinnedMesh;
        }
        #endregion

        #region Draw Functions
        /// <summary> Draws this mesh. </summary>
        public void Draw()
        {
            graphicsDevice.SetVertexBuffer(VertexBuffer);
            graphicsDevice.Indices = IndexBuffer; 
            //Liru apparently accidentally passed vertex count here.. maybe I was losing some FPS to that.
            graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, IndexBuffer.IndexCount/3);
        }
        #endregion
    }
    [DebuggerDisplay("{Name} ({Index})")]
    public class CBone
    {
        #region Properties
        /// <summary> The name of this bone. </summary>
        public string Name { get; }

        /// <summary> The index of this bone. </summary>
        public int Index { get; }

        /// <summary> The parent of this bone, or <c>null</c> if this is the root bone. </summary>
        public CBone Parent { get; }

        /// <summary> The parent of this bone for physics, or <c>null</c> to use the bone's parent. </summary>
        public CBone PhysicsParentOverride { get; set; }

        /// <summary> <c>true</c> if <see cref="Parent"/> is <c>null</c>, otherwise; <c>false</c>. </summary>
        public bool HasParent => Parent != null;

        /// <summary> Converts model-space orientations into bone-space orientations. </summary>
        /// <remarks> 
        /// Basically, as the bone moves around, this helps keep track of how much the bone has moved from its default position. 
        /// If the bone is at its default position, this will be <see cref="Matrix.Identity"/> (or pretty close to it).
        /// </remarks>
        public Matrix Offset { get; }

        /// <summary> The transform of the bone relative to its parent. If this bone has no parent, then it is relative to the model. </summary>
        public Matrix LocalTransform { get; }

        /// <summary> The extents of the bounding box for this bone. </summary> 
        public Vector3 BoundsSize { get; set; }

        /// <summary> The center of the bounding box for this bone. </summary> 
        public Vector3 BoundsCenter { get; set; }
        /// <summary> Half-angle (radians) of the normal swing cone. </summary>
        public float JointNormalHalfCone { get; set; }

        /// <summary> Half-angle (radians) of the plane swing cone. </summary>
        public float JointPlaneHalfCone { get; set; }

        /// <summary> Minimum twist angle (radians). </summary>
        public float JointTwistMin { get; set; }

        /// <summary> Maximum twist angle (radians). </summary>
        public float JointTwistMax { get; set; }
        #endregion

        #region Constructors
        private CBone(string name, int index, CBone parent, CBone physparent, Matrix offset, Matrix localTransform,
                      Vector3 bsize, Vector3 bcenter,
                      float jointNormalHalfCone, float jointPlaneHalfCone, float jointTwistMin, float jointTwistMax)
        {
            Name = name;
            Index = index;
            Parent = parent;
            PhysicsParentOverride = physparent;
            Offset = offset;
            LocalTransform = localTransform;
            BoundsSize = bsize;
            BoundsCenter = bcenter;
            JointNormalHalfCone = jointNormalHalfCone;
            JointPlaneHalfCone = jointPlaneHalfCone;
            JointTwistMin = jointTwistMin;
            JointTwistMax = jointTwistMax;
        }
        #endregion

        #region Creation Functions
        /// <summary> Creates and returns a bone created for the given <paramref name="model"/> and from the given <paramref name="data"/>. </summary>
        /// <param name="model"> The model that this bone belongs to. </param>
        /// <param name="data"> The bone's data. </param>
        /// <returns> The created bone. </returns>
        public static CBone CreateFrom(CModel model, CBoneData data)
        {
            CBone parentBone = data.ParentIndex >= 0 && data.ParentIndex < model.Bones.Count ? model.Bones[data.ParentIndex] : null;
            CBone parentPhysicsBone = data.PhysicsParentOverrideIndex.HasValue && data.PhysicsParentOverrideIndex >= 0 && data.PhysicsParentOverrideIndex < model.Bones.Count ? model.Bones[data.PhysicsParentOverrideIndex.Value] : null;

            if (data.Index < 0)
                throw new ArgumentException($"Bone {data.Name} has an index of {data.Index}, when there are only {model.Bones.Count} bones total.");

            return new CBone(data.Name, data.Index, parentBone, parentPhysicsBone, data.Offset, data.LocalTransform,
                             data.BoundsSize, data.BoundsCenter,
                             data.JointNormalHalfCone, data.JointPlaneHalfCone, data.JointTwistMin, data.JointTwistMax);
        }
        #endregion
    }

    public static class CLodResolver
    {
        public static (CSkinnedMesh mesh, CMeshData meshData, List<CMorphTarget> morphTargets, bool hidden) Resolve(
            CModel.CBodyGroup bodygroup, int level)
        {
            for (int L = level; L >= 1; L--)
            {
                if (L - 1 >= bodygroup.LODMeshes.Count) continue;

                var entry = bodygroup.LODMeshes[L - 1];
                if (entry == null) continue;

                if (entry.Hidden) return (null, default, null, true);
                if (entry.Mesh != null) return (entry.Mesh, entry.MeshData, entry.MorphTargets, false);
            }

            return (bodygroup.Mesh, bodygroup.MeshData, bodygroup.MorphTargets, false);
        }
    }
}

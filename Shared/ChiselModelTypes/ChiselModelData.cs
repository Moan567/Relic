using Liru3D.Animations;
using Liru3D.Models;
using Liru3D.Models.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MessagePack.Formatters;
using MessagePack;
using Chisel.Utils.Animation;

namespace Chisel.Models.Data
{
    [DebuggerDisplay("{Name}")]
    public struct CBoneData
    {
        #region Properties
        /// <summary> The name of the bone. </summary>
        public string Name { get; set; }

        /// <summary> the index of this bone within the model. </summary>
        public int Index { get; set; }

        /// <summary> The index of this bone's parent bone within the model. </summary>
        public int ParentIndex { get; set; }

        /// <summary> The index of this bone's parent bone within the model, for physics. </summary>
        public int? PhysicsParentOverrideIndex { get; set; }

        /// <summary> The offset of the bone, used when rendering. </summary>
        public Matrix Offset { get; set; }

        /// <summary> The transform of the bone relative to its parent. If this bone has no parent, then it is relative to the model. </summary>
        public Matrix LocalTransform { get; set; }

        /// <summary> The extents of the bounding box for this bone. </summary> 
        public Vector3 BoundsSize { get; set; }

        /// <summary> The center of the bounding box for this bone. </summary> 
        public Vector3 BoundsCenter { get; set; }

        /// <summary> Half-angle (radians) of the normal swing cone. Default 0.8. </summary>
        public float JointNormalHalfCone { get; set; } = 0.8f;

        /// <summary> Half-angle (radians) of the plane swing cone. Default 0.1. </summary>
        public float JointPlaneHalfCone { get; set; } = 0.1f;

        /// <summary> Minimum twist angle (radians). Default -0.8. </summary>
        public float JointTwistMin { get; set; } = -0.8f;

        /// <summary> Maximum twist angle (radians). Default 0.8. </summary>
        public float JointTwistMax { get; set; } = 0.8f;
        #endregion

        public CBoneData() { }
    }
    public struct CMeshData
    {
        #region Properties
        /// <summary> The name of the mesh. </summary>
        public string Name { get; set; }

        /// <summary> The collection of vertices. Each vertex within this collection holds multiple pieces of data, see <see cref="SkinnedVertex"/> for more. </summary>
        public CSkinnedVertex[] Vertices { get; set; }

        /// <summary> The number of vertices in this data. </summary>
        public int VertexCount => Vertices == null ? 0 : Vertices.Length;

        /// <summary> The collection of indices. </summary>
        public int[] Indices { get; set; }

        /// <summary> The number of indices in this data. </summary>
        public int IndexCount => Indices == null ? 0 : Indices.Length;
        #endregion

        #region Constructors
        /// <summary> Creates a new data with the given name and collections. </summary>
        /// <param name="name"> The name of the mesh. </param>
        /// <param name="vertices"> The collection of vertices. </param>
        /// <param name="indices"> The collection of indices. </param>
        public CMeshData(string name, CSkinnedVertex[] vertices, int[] indices)
        {
            Name = name;
            Vertices = vertices;
            Indices = indices;
        }
        public CMeshData() { }
        #endregion

        #region Bounding Functions
        /// <summary> Calculates a bounding sphere for the data's vertices. </summary>
        /// <returns> The calculated bounding sphere. </returns>
        public BoundingSphere CalculateBoundingSphere() => VertexCount == 0 ? new BoundingSphere() : BoundingSphere.CreateFromPoints(Vertices.Select(v => v.Position));
        #endregion
    }
    public struct CModelData
    {
        #region Properties
        /// <summary> The collection of mesh data. </summary>
        public IReadOnlyList<CMeshData> Meshes { get; set; }

        /// <summary> The number of meshes in this data. </summary>
        public int MeshCount => Meshes.Count;

        /// <summary> The collection of animations. </summary>
        public List<CAnimation> Animations { get; set; }
        public List<CModel.CSequence> Sequences { get; set; }

        /// <summary> The number of animations in this data. </summary>
        public int AnimationCount => Animations.Count;

        /// <summary> The collection of bone data. </summary>
        public IReadOnlyList<CBoneData> Bones { get; set; }

        /// <summary> The number of bones in this data. </summary>
        public int BoneCount => Bones.Count;
        #endregion

        #region Constructors
        /// <summary> Creates a new model data with the given collections. </summary>
        /// <param name="meshes"> The collection of mesh data. </param>
        /// <param name="animations"> The collection of animations. </param>
        /// <param name="bones"> The collection of bones. </param>
        public CModelData(IReadOnlyList<CMeshData> meshes, List<CAnimation> animations, IReadOnlyList<CBoneData> bones)
        {
            Meshes = meshes ?? throw new System.ArgumentNullException(nameof(meshes));
            Animations = animations ?? throw new System.ArgumentNullException(nameof(animations));
            Bones = bones;
        }
        public CModelData() { }
        #endregion
    }
    public struct CSkinnedVertex : IVertexType
    {
        #region Backing Fields
        /// <summary> The declaration of a single vertex used when uploading vertex data to the GPU. </summary>
        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Byte4, VertexElementUsage.BlendIndices, 0),
            new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0),
            new VertexElement(32, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(44, VertexElementFormat.Vector3, VertexElementUsage.Tangent, 0),
            new VertexElement(56, VertexElementFormat.Vector3, VertexElementUsage.Binormal, 0),
            new VertexElement(68, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(76, VertexElementFormat.Single, VertexElementUsage.BlendIndices, 1)
            );
        #endregion

        #region Properties
        /// <summary> The layout of this vertex data, compatible with the default MonoGame SkinnedEffect. </summary>
        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

        /// <summary> The position of the vertex itself. </summary>
        public Vector3 Position { get; set; }

        /// <summary> The packed bone indices that affect this vertex's position. </summary>
        public Byte4 BlendIndices { get; set; }

        /// <summary> The amount that each bone affects the final position of this vertex. </summary>
        public Vector4 BlendWeights { get; set; }

        /// <summary> The normal direction of this vertex. </summary>
        public Vector3 Normal { get; set; }
        public Vector3 Tangent { get; set; }
        public Vector3 Binormal { get; set; }

        /// <summary> The Texture co-ordinate. </summary>
        public Vector2 UV { get; set; }

        /// <summary> The index to the eyes, if any.</summary>
        public float EyeIndex { get; set; }
        #endregion

        #region Weight Functions
        /// <summary> Calculates the total number of non-zero weights of the <see cref="BlendWeights"/>, which is the total number of bones influencing this vertex. </summary>
        /// <returns> The calculated number of bones which influence this vertex. </returns>
        public int CalculateBoneCount()
        {
            // Count the number of non-zero weights and return the result.
            int boneCount = 0;
            if (BlendWeights.X != 0) boneCount++;
            if (BlendWeights.Y != 0) boneCount++;
            if (BlendWeights.Z != 0) boneCount++;
            if (BlendWeights.W != 0) boneCount++;
            return boneCount;
        }

        /// <summary> Sets the next value of <see cref="BlendIndices"/> and <see cref="BlendWeights"/> to the given values. </summary>
        /// <param name="boneIndex"> The index of the next bone influence. </param>
        /// <param name="weight"> The weight of the next bone influence. </param>
        public void SetNextWeight(int boneIndex, float weight)
        {
            // Unpack the indices and copy the weights.
            Vector4 boneIndices = BlendIndices.ToVector4();
            Vector4 boneWeights = BlendWeights;

            // Calculate the bone count.
            int boneCount = CalculateBoneCount();

            // Set the index and weight.
            switch (boneCount)
            {
                case 0: boneIndices.X = boneIndex; boneWeights.X = weight; break;
                case 1: boneIndices.Y = boneIndex; boneWeights.Y = weight; break;
                case 2: boneIndices.Z = boneIndex; boneWeights.Z = weight; break;
                case 3: boneIndices.W = boneIndex; boneWeights.W = weight; break;
                default: throw new System.Exception("Cannot use more than 4 bones per vertex.");
            }

            // Set the blend indices and weights.
            BlendIndices = new Byte4(boneIndices);
            BlendWeights = boneWeights;
        }

        public CSkinnedVertex() { }
        #endregion
    }
    public struct CMorphTarget
    {
        public string Name { get; set; }
        /// <summary> Indices of the vertices this morph actually moves. </summary>
        public int[] Indices { get; set; }
        /// <summary> Delta positions, parallel to Indices. </summary>
        public Vector3[] DeltaPositions { get; set; }
        /// <summary> Delta normals, parallel to Indices. </summary>
        public Vector3[] DeltaNormals { get; set; }

        public int AffectedCount => Indices?.Length ?? 0;
    }
    public class Byte4Formatter : IMessagePackFormatter<Byte4>
    {
        public void Serialize(ref MessagePackWriter writer, Byte4 value, MessagePackSerializerOptions options)
        {
            writer.WriteUInt32(value.PackedValue);
        }

        public Byte4 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            uint packed = reader.ReadUInt32();
            Byte4 result = default;
            result.PackedValue = packed;
            return result;
        }
    }
}

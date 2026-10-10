using Assimp;
using Assimp.Configs;
using Relic.Models;
using Relic.Models.Data;
using Relic.Models.Morph;
using Relic.Utils.Animation;
using Liru3D.Animations;
using Liru3D.Models.Data;
using Microsoft.Xna.Framework;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Relic.Models.CModel;

namespace Rockwall2.Editor.Model.Utils;
public static class ModelEditorData
{
    public static int ViewingBone { get; set; } = -1;
    public static int ViewingAttachment { get; set; } = -1;
    public static int ViewingEye { get; set; } = -1;
    public static string ActivePath { get; set; }
    public static CModel ActiveModel { get; set; }
    public static CAnimationPlayer SequencePlayer { get; set; }
    public static CModelAnimator PreviewAnimator { get; set; }
    public static Vector3 PreviewRootMotionAccum { get; set; }
    public static CMorphState MorphState { get; set; } = new();

    private static readonly System.Text.RegularExpressions.Regex LodSuffixPattern =
    new(@"^(.*?)_lod(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static void NewModelFile()
    {
        SequencePlayer = null;
        ActiveModel = new CModel();
        PreviewRootMotionAccum = Vector3.Zero;
    }

    public static void ImportFBXFile(string path)
    {
        SequencePlayer = null;

        if (ActiveModel is null) NewModelFile();

        Scene scene = Import(path);

        for (int i = 0; i < scene.MeshCount; i++)
        {
            if (scene.Meshes[i].Bones.Count != 0) continue;
            scene.Meshes[i].Bones.Add(new Assimp.Bone("STATICMODEL_ROOT_BONE_REPLACE", new(), Array.Empty<VertexWeight>()));
        }

        CModelData meshData = Process(scene);

        ActiveModel.Animations = new List<CModel.CAnimDef>();
        ActiveModel.Sequences = new List<CSequence>();

        if (meshData.AnimationCount != 0)
        {
            foreach (var anim in meshData.Animations)
            {
                ActiveModel.Animations.Add(new CModel.CAnimDef
                {
                    Name = anim.Name,
                    Animation = anim,
                    Speed = 1f
                });
            }
        }
        ActiveModel.Bones = new List<CBone>();
        ActiveModel.BoneData = new List<CBoneData>();

        if (meshData.BoneCount != 0)
        {
            foreach (var bone in meshData.Bones)
            {
                ActiveModel.BoneData.Add(bone);
            }
        }
        ActiveModel.Bodygroups = new List<CModel.CBodyGroup>();

        if (meshData.MeshCount != 0)
        {
            var assignment = ComputeDefaultLodAssignment(meshData.Meshes.Select(m => m.Name));
            EnsureLodLevelCount(ActiveModel, assignment.Count > 0 ? assignment.Max(a => a.LodLevel) : 0);
            ActiveModel.Bodygroups = BuildBodygroupsFromAssignment(meshData, assignment, new(), meshData.BoneCount);

            foreach (var bone in meshData.Bones)
                ActiveModel.Bones.Add(CBone.CreateFrom(ActiveModel, bone));
        }
        ActiveModel.AttachmentPoints = new List<CModel.CAttachmentPoint>();
        ActiveModel.ModelTransforms = new Matrix[ActiveModel.Bones.Count];
        Array.Fill(ActiveModel.ModelTransforms, Matrix.Identity);

        SequencePlayer = null;
        if (ActiveModel.Animations != null && ActiveModel.Animations.Count > 0 && ActiveModel.Animations.Any(a => a.Name.ToLower().Equals("bindpose")))
        {
            CModel.CAnimDef bindpose = ActiveModel.Animations.Find(a => a.Name.ToLower().Equals("bindpose"));
            CAnimationPlayer bindposePlayer = new CAnimationPlayer(ActiveModel);
            bindposePlayer.AnimDef = bindpose;
            bindposePlayer.IsPlaying = false;
            bindposePlayer.Update(0f);
            ActiveModel.BoneTransforms = bindposePlayer.BoneSpaceTransforms.ToArray();

            SequencePlayer = new CAnimationPlayer(ActiveModel);
            SequencePlayer.AnimDef = ActiveModel.Animations[0];
        }

        CMorphApplicatorFactory.EnsureMorphApplicators(ActiveModel, EditorHost.Instance.GraphicsDevice);
    }

    public static List<string> PeekFBXAnimationNames(string path)
    {
        Scene scene = Import(path);
        return scene.Animations
            .Select(a => a.Name.Remove(0, a.Name.IndexOf('|') + 1))
            .ToList();
    }

    public static void ReimportFBXAnimations(string path, List<string> selectedNames)
    {
        if (ActiveModel is null) return;

        Scene scene = Import(path);
        for (int i = 0; i < scene.MeshCount; i++)
        {
            if (scene.Meshes[i].Bones.Count != 0) continue;
            scene.Meshes[i].Bones.Add(new Assimp.Bone("STATICMODEL_ROOT_BONE_REPLACE", new(), Array.Empty<VertexWeight>()));
        }

        CModelData meshData = Process(scene);
        var existingByName = ActiveModel.Animations.ToDictionary(a => a.Name, a => a);

        foreach (var anim in meshData.Animations)
        {
            if (!selectedNames.Contains(anim.Name)) continue;

            if (existingByName.TryGetValue(anim.Name, out var existing))
            {
                existing.Animation = anim;
            }
            else
            {
                ActiveModel.Animations.Add(new CModel.CAnimDef
                {
                    Name = anim.Name,
                    Animation = anim,
                    Speed = 1f,
                    Events = new(),
                    BoneMask = new()
                });
            }
        }

        RebuildSequencePlayer();
    }

    public static void ImportFromBlendFull(string meshFbx,
        List<(string name, string path)> animFbxes,
        List<(string meshName, string key, string path)> shapeKeys)
    {
        ImportFBXFile(meshFbx);
        ImportMorphTargets(shapeKeys, meshFbx);
        if (animFbxes != null) MergeAnimFbxes(animFbxes, replace: true);
    }

    public static void ImportFromBlendMesh(string meshFbx,
        List<(string meshName, string key, string path)> shapeKeys)
    {
        ReimportFBXModel(meshFbx);
        ImportMorphTargets(shapeKeys, meshFbx);
    }
    public static List<string> PeekBlendAnimationNames(List<(string name, string path)> animFbxes)
        => animFbxes.Select(a => a.name).ToList();

    public static void ImportFromBlendAnimations(List<(string name, string path)> animFbxes, List<string> selectedNames)
        => MergeAnimFbxes(animFbxes.Where(a => selectedNames.Contains(a.name)).ToList(), replace: false);

    private static void MergeAnimFbxes(List<(string name, string path)> animFbxes, bool replace)
    {
        if (ActiveModel is null) return;
        if (replace) ActiveModel.Animations = new List<CModel.CAnimDef>();

        var existingByName = ActiveModel.Animations.ToDictionary(a => a.Name, a => a);

        foreach (var (actionName, fbxPath) in animFbxes)
        {
            Scene scene;
            try { scene = Import(fbxPath); } catch { continue; }

            CModelData data = Process(scene);
            if (data.AnimationCount == 0) continue;

            var src = data.Animations[0];
            var anim = new CAnimation(actionName, src.TicksPerSecond, src.DurationInTicks, src.ChannelsByBoneName);

            if (existingByName.TryGetValue(actionName, out var existing))
            {
                existing.Animation = anim;
            }
            else
            {
                var animDef = new CModel.CAnimDef
                {
                    Name = actionName,
                    Animation = anim,
                    Speed = 1f,
                    Events = new(),
                    BoneMask = new()
                };
                ActiveModel.Animations.Add(animDef);
                existingByName[actionName] = animDef;
            }
        }
        RebuildSequencePlayer();
    }

    private static void RebuildSequencePlayer()
    {
        SequencePlayer = null;
        if (ActiveModel?.Animations == null || ActiveModel.Animations.Count == 0) return;

        var bindpose = ActiveModel.Animations.Find(a => a.Name.ToLower() == "bindpose");
        if (bindpose == null) return;

        var bp = new CAnimationPlayer(ActiveModel);
        bp.AnimDef = bindpose;
        bp.IsPlaying = false;
        bp.Update(0f);
        ActiveModel.BoneTransforms = bp.BoneSpaceTransforms.ToArray();

        SequencePlayer = new CAnimationPlayer(ActiveModel);
        SequencePlayer.AnimDef = ActiveModel.Animations[0];
    }

    public static void ReimportFBXModel(string path)
    {
        if (ActiveModel is null)
        {
            ImportFBXFile(path);
            return;
        }

        var existingAnimations = ActiveModel.Animations.ToList();
        var existingAttachmentPoints = ActiveModel.AttachmentPoints.ToList();
        var existingBoneTransforms = ActiveModel.BoneTransforms;
        var existingBodygroupsByName = ActiveModel.Bodygroups.ToDictionary(bg => bg.Name, bg => bg);

        var existingBoneDataByName = ActiveModel.BoneData.ToDictionary(b => b.Name, b => b);

        Scene scene = Import(path);

        for (int i = 0; i < scene.MeshCount; i++)
        {
            if (scene.Meshes[i].Bones.Count != 0) continue;
            scene.Meshes[i].Bones.Add(new Assimp.Bone("STATICMODEL_ROOT_BONE_REPLACE", new(), Array.Empty<VertexWeight>()));
        }

        CModelData meshData = Process(scene);

        ActiveModel.Bodygroups = new List<CModel.CBodyGroup>();

        if (meshData.MeshCount != 0)
        {
            var assignment = ComputeDefaultLodAssignment(meshData.Meshes.Select(m => m.Name));
            EnsureLodLevelCount(ActiveModel, assignment.Count > 0 ? assignment.Max(a => a.LodLevel) : 0);
            ActiveModel.Bodygroups = BuildBodygroupsFromAssignment(meshData, assignment, existingBodygroupsByName, meshData.BoneCount);
        }

        Array.Resize(ref existingBoneTransforms, meshData.BoneCount);

        for(int i = ActiveModel.BoneTransforms.Length; i < meshData.BoneCount; i++)
        {
            existingBoneTransforms[i] = Matrix.Identity;
        }

        ActiveModel.Animations = existingAnimations;
        ActiveModel.AttachmentPoints = existingAttachmentPoints;
        ActiveModel.BoneTransforms = existingBoneTransforms;

        var newBoneData = new List<CBoneData>();
        foreach (var newBone in meshData.Bones)
        {
            if (existingBoneDataByName.TryGetValue(newBone.Name, out var existing))
            {
                newBoneData.Add(new CBoneData
                {
                    Name = newBone.Name,
                    Index = newBone.Index,
                    ParentIndex = newBone.ParentIndex,
                    PhysicsParentOverrideIndex = existing.PhysicsParentOverrideIndex,
                    Offset = newBone.Offset,
                    LocalTransform = newBone.LocalTransform,
                    BoundsSize = existing.BoundsSize,
                    BoundsCenter = existing.BoundsCenter,
                    JointNormalHalfCone = existing.JointNormalHalfCone,
                    JointPlaneHalfCone = existing.JointPlaneHalfCone,
                    JointTwistMin = existing.JointTwistMin,
                    JointTwistMax = existing.JointTwistMax,
                });
            }
            else
            {
                newBoneData.Add(newBone);
            }
        }

        ActiveModel.BoneData = newBoneData;

        ActiveModel.Bones = new List<CBone>();
        foreach (var bone in newBoneData)
            ActiveModel.Bones.Add(CBone.CreateFrom(ActiveModel, bone));

        ActiveModel.ModelTransforms = new Matrix[ActiveModel.Bones.Count];
        Array.Fill(ActiveModel.ModelTransforms, Matrix.Identity);

        CMorphApplicatorFactory.EnsureMorphApplicators(ActiveModel, EditorHost.Instance.GraphicsDevice);

        RebuildSequencePlayer();

        foreach (var bg in ActiveModel.Bodygroups)
        {
            if (bg.MorphTargets?.Count > 0)
                bg.MorphApplicator = new CMorphApplicator(EditorHost.Instance.GraphicsDevice, bg.MeshData.Vertices);
        }
        MorphState.Initialize(ActiveModel);
    }

    // Catches sequences left pointing at an animation name that no longer exists
    // after a rename/delete in the source file.
    public static List<(string sequenceName, string missingAnimationName)> FindDanglingSequenceReferences()
    {
        var problems = new List<(string, string)>();
        if (ActiveModel?.Sequences == null || ActiveModel.Animations == null) return problems;

        var animationNames = new HashSet<string>(ActiveModel.Animations.Select(a => a.Name));

        foreach (var seq in ActiveModel.Sequences)
        {
            CollectDanglingReferences(seq.Name, seq.Root, animationNames, problems);
        }

        return problems;
    }

    private static void CollectDanglingReferences(string sequenceName, BlendSource node, HashSet<string> animationNames, List<(string, string)> problems)
    {
        if (node == null) return;

        switch (node.Type)
        {
            case BlendSourceType.Clip:
                if (!string.IsNullOrEmpty(node.AnimationName) && !animationNames.Contains(node.AnimationName))
                {
                    problems.Add((sequenceName, node.AnimationName));
                }
                break;

            case BlendSourceType.Blend1D:
                foreach (var entry in node.Entries1D)
                {
                    CollectDanglingReferences(sequenceName, entry.Source, animationNames, problems);
                }
                break;

            case BlendSourceType.Blend2D:
                foreach (var entry in node.Entries2D)
                {
                    CollectDanglingReferences(sequenceName, entry.Source, animationNames, problems);
                }
                break;
        }
    }
    public static (string BaseName, int LodLevel) ParseLodMeshName(string rawMeshName)
    {
        var match = LodSuffixPattern.Match(rawMeshName);
        if (match.Success && int.TryParse(match.Groups[2].Value, out int level) && level > 0)
            return (match.Groups[1].Value, level);

        return (rawMeshName, 0);
    }

    public class MeshLodAssignment
    {
        public string RawMeshName;
        public string BodygroupName;
        public int LodLevel;
    }
    public static List<MeshLodAssignment> ComputeDefaultLodAssignment(IEnumerable<string> rawMeshNames)
    {
        var result = new List<MeshLodAssignment>();
        foreach (var name in rawMeshNames)
        {
            var (baseName, level) = ParseLodMeshName(name);
            result.Add(new MeshLodAssignment { RawMeshName = name, BodygroupName = baseName, LodLevel = level });
        }
        return result;
    }

    private static void EnsureLodLevelCount(CModel model, int maxLevel)
    {
        // Placeholder spacing
        while (model.LODLevels.Count < maxLevel)
        {
            float previousDistance = model.LODLevels.Count > 0 ? model.LODLevels[^1].Distance : 0f;
            model.LODLevels.Add(new CModel.CLODLevel { Distance = previousDistance + 25f });
        }
    }

    private static List<CModel.CBodyGroup> BuildBodygroupsFromAssignment(
        CModelData meshData,
        List<MeshLodAssignment> assignments,
        Dictionary<string, CModel.CBodyGroup> existingByName,
        int boneCount)
    {
        var groups = new Dictionary<string, List<(CMeshData mesh, int level)>>();

        foreach (var rawMesh in meshData.Meshes)
        {
            var assignment = assignments.Find(a => a.RawMeshName == rawMesh.Name);
            string bgName = assignment?.BodygroupName ?? rawMesh.Name;
            int level = assignment?.LodLevel ?? 0;

            if (!groups.TryGetValue(bgName, out var list))
                groups[bgName] = list = new List<(CMeshData, int)>();

            list.Add((rawMesh, level));
        }

        var result = new List<CModel.CBodyGroup>();

        foreach (var (bgName, meshes) in groups)
        {
            var baseEntry = meshes.Find(m => m.level == 0);
            if (baseEntry.mesh.Vertices == null) baseEntry = meshes[0]; // no explicit LOD0 supplied

            existingByName.TryGetValue(bgName, out var existing);

            var bodygroup = new CModel.CBodyGroup
            {
                Name = bgName,
                SourceMeshName = baseEntry.mesh.Name,
                Mesh = CSkinnedMesh.CreateFrom(EditorHost.Instance.GraphicsDevice, baseEntry.mesh),
                MeshData = baseEntry.mesh,
                IsSkinned = boneCount > 1,
                MaterialID = existing?.MaterialID ?? 0,
                IsEye = existing?.IsEye ?? false,
                Offset = existing?.Offset ?? Matrix.Identity,
                MorphTargets = new(),
                LODMeshes = existing?.LODMeshes != null
                    ? new List<CModel.CLODMeshEntry>(existing.LODMeshes)
                    : new List<CModel.CLODMeshEntry>(),
            };

            foreach (var (mesh, level) in meshes)
            {
                if (level == 0) continue;

                while (bodygroup.LODMeshes.Count < level) bodygroup.LODMeshes.Add(null);

                bodygroup.LODMeshes[level - 1] = new CModel.CLODMeshEntry
                {
                    Hidden = false,
                    Mesh = CSkinnedMesh.CreateFrom(EditorHost.Instance.GraphicsDevice, mesh),
                    MeshData = mesh,
                    SourceMeshName = mesh.Name
                };
            }

            result.Add(bodygroup);
        }

        return result;
    }

    private static readonly PostProcessSteps postProcessSteps =
                                              PostProcessSteps.FlipUVs                // currently need
                                            | PostProcessSteps.JoinIdenticalVertices  // optimizes indexed
                                            | PostProcessSteps.Triangulate            // precaution
                                            | PostProcessSteps.FindInvalidData        // sometimes normals export wrong (remove & replace:)
                                            | PostProcessSteps.GenerateSmoothNormals  // smooths normals after identical verts removed (or bad normals)
                                            | PostProcessSteps.ImproveCacheLocality   // possible better cache optimization                                        
                                            //| PostProcessSteps.FixInFacingNormals     // doesn't work well with planes - turn off if some faces go dark                                       
                                            | PostProcessSteps.CalculateTangentSpace  // use if you'll probably be using normal mapping 
                                            | PostProcessSteps.GenerateUVCoords       // useful for non-uv-map export primitives                                                
                                            | PostProcessSteps.ValidateDataStructure
                                            | PostProcessSteps.FindInstances
                                            | PostProcessSteps.LimitBoneWeights
                                            | PostProcessSteps.GlobalScale            // use with AI_CONFIG_GLOBAL_SCALE_FACTOR_KEY (if need)                                                
                                            | PostProcessSteps.FlipWindingOrder;       // (CCW to CW) Depends on your rasterizing setup (need clockwise to fix inside-out problem?)        
    private static Scene ImportRaw(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(path);

        var ctx = new AssimpContext();
        ctx.SetConfig(new FBXImportCamerasConfig(false));
        ctx.SetConfig(new SortByPrimitiveTypeConfig(
            Assimp.PrimitiveType.Point | Assimp.PrimitiveType.Line));

        return ctx.ImportFile(path,
            PostProcessSteps.Triangulate
            | PostProcessSteps.FlipUVs
            | PostProcessSteps.FlipWindingOrder
            | PostProcessSteps.GlobalScale);
    }
    public static Scene Import(string filename, bool genDummySkeleton = false)
    {
        // Ensure the file exists.
        if (!File.Exists(filename)) throw new FileNotFoundException("The skinned mesh model could not be found.", filename);

        List<PropertyConfig> configurations = new List<PropertyConfig>()
            {
                new NoSkeletonMeshesConfig(!genDummySkeleton),      // true to disable dummy-skeleton mesh
                new FBXImportCamerasConfig(false),     // true would import cameras
                new SortByPrimitiveTypeConfig(Assimp.PrimitiveType.Point | Assimp.PrimitiveType.Line), // primitive types we should remove
                new VertexBoneWeightLimitConfig(4),    // max weights per vertex (4 is very common - our shader will use 4)
                new NormalSmoothingAngleConfig(66.0f), // if no normals, generate (threshold 66 degrees) 
                new FBXStrictModeConfig(false),        // true only for fbx-strict-mode
                new TangentSmoothingAngleConfig(66.0f)
            };

        // Create the context.
        AssimpContext assimpContext = new AssimpContext();
        foreach (PropertyConfig config in configurations)
            assimpContext.SetConfig(config);

        // Load the scene.
        Scene scene = assimpContext.ImportFile(filename, postProcessSteps);

        return scene;
    }

    #region Settings
    /// <summary> Sometimes values have a slight error due to the constant number crunching. For example, the scale might go from 1 to 0.99999994. This option allows this error to be rounded away. 0 disables rounding. </summary>
    public static int KeyframeDecimalPlaceRounding { get; set; } = 5;
    #endregion

    #region Process Functions
    public static CModelData Process(Scene input)
    {
        buildArmature(input, out armatureData armature);

        CMeshData[] skinnedMeshData = new CMeshData[input.MeshCount];

        for (int meshIndex = 0; meshIndex < input.MeshCount; meshIndex++)
        {
            Mesh mesh = input.Meshes[meshIndex];
            int[] indices = mesh.GetIndices();
            CSkinnedVertex[] vertices = new CSkinnedVertex[mesh.VertexCount];

            for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; vertexIndex++)
            {
                CSkinnedVertex vertex = new CSkinnedVertex
                {
                    Position = mesh.Vertices[vertexIndex].ToMonoGameVector3(),
                    Normal = mesh.Normals[vertexIndex].ToMonoGameVector3(),
                    Tangent = mesh.Tangents[vertexIndex].ToMonoGameVector3(),
                    Binormal = mesh.BiTangents[vertexIndex].ToMonoGameVector3(),
                    UV = mesh.TextureCoordinateChannels[0][vertexIndex].ToMonoGameVector2(),
                };

                vertices[vertexIndex] = vertex;
            }

            foreach (Assimp.Bone bone in mesh.Bones)
            {
                int boneIndex = armature.GetIndexOf(bone);

                for (int weightIndex = 0; weightIndex < bone.VertexWeightCount; weightIndex++)
                {
                    VertexWeight vertexWeight = bone.VertexWeights[weightIndex];
                    vertices[vertexWeight.VertexID].SetNextWeight(boneIndex, vertexWeight.Weight);
                }
            }

            skinnedMeshData[meshIndex] = new CMeshData(mesh.Name, vertices, indices);
        }

        // Process each animation in the scene.
        List<CAnimation> animations = new List<CAnimation>(input.AnimationCount);
        for (int animationIndex = 0; animationIndex < input.AnimationCount; animationIndex++)
        {
            Assimp.Animation inputAnimation = input.Animations[animationIndex];
            Dictionary<string, CBoneChannel> channelsByBoneName = new Dictionary<string, CBoneChannel>();

            for (int channelIndex = 0; channelIndex < inputAnimation.NodeAnimationChannelCount; channelIndex++)
            {
                NodeAnimationChannel inputChannel = inputAnimation.NodeAnimationChannels[channelIndex];

                // If the channel isn't for a bone, then ignore it.
                // This may cause issues for non-bones that are animated, but it's a safe assumption that nobody's out there directly animating nodes.
                if (!armature.BoneNodesByBoneName.TryGetValue(inputChannel.NodeName, out Node boneNode)) continue;

                // If this channel's node's parent is not a bone, then its parent's transforms need to be applied to every animated value.
                // This is because usually the armature itself is rotated 90 degrees so that Y is up, but since the armature is being disposed of, this needs to be baked into the data.
                bool needsTransforming = !armature.BoneNodesByBoneName.ContainsKey(boneNode.Parent.Name);
                Matrix parentTransform = needsTransforming ? boneNode.Parent.Transform.ToMonoGameMatrixTransposed() : Matrix.Identity;

                List<Keyframe<Vector3>> scaleFrames = new List<Keyframe<Vector3>>(inputChannel.ScalingKeyCount);
                List<Keyframe<Microsoft.Xna.Framework.Quaternion>> rotationFrames = new List<Keyframe<Microsoft.Xna.Framework.Quaternion>>(inputChannel.RotationKeyCount);
                List<Keyframe<Vector3>> positionFrames = new List<Keyframe<Vector3>>(inputChannel.PositionKeyCount);

                for (int scaleFrameIndex = 0; scaleFrameIndex < inputChannel.ScalingKeyCount; scaleFrameIndex++)
                {
                    VectorKey scaleKey = inputChannel.ScalingKeys[scaleFrameIndex];
                    Vector3 scale = scaleKey.Value.ToMonoGameVector3();

                    if (needsTransforming) (Matrix.CreateScale(scale) * parentTransform).Decompose(out scale, out _, out _);

                    if (KeyframeDecimalPlaceRounding > 0)
                    {
                        scale.X = (float)Math.Round(scale.X, KeyframeDecimalPlaceRounding);
                        scale.Y = (float)Math.Round(scale.Y, KeyframeDecimalPlaceRounding);
                        scale.Z = (float)Math.Round(scale.Z, KeyframeDecimalPlaceRounding);
                    }

                    scaleFrames.Add(new Keyframe<Vector3>(scaleFrameIndex, (int)Math.Round(scaleKey.Time), scale));
                }

                for (int rotationFrameIndex = 0; rotationFrameIndex < inputChannel.RotationKeyCount; rotationFrameIndex++)
                {
                    QuaternionKey quaternionKey = inputChannel.RotationKeys[rotationFrameIndex];

                    Microsoft.Xna.Framework.Quaternion rotation = quaternionKey.Value.ToMonoGameQuaternion();

                    if (needsTransforming) (Matrix.CreateFromQuaternion(rotation) * parentTransform).Decompose(out _, out rotation, out _);

                    if (KeyframeDecimalPlaceRounding > 0)
                    {
                        rotation.X = (float)Math.Round(rotation.X, KeyframeDecimalPlaceRounding);
                        rotation.Y = (float)Math.Round(rotation.Y, KeyframeDecimalPlaceRounding);
                        rotation.Z = (float)Math.Round(rotation.Z, KeyframeDecimalPlaceRounding);
                        rotation.W = (float)Math.Round(rotation.W, KeyframeDecimalPlaceRounding);
                    }

                    rotationFrames.Add(new Keyframe<Microsoft.Xna.Framework.Quaternion>(rotationFrameIndex, (int)Math.Round(quaternionKey.Time), rotation));
                }

                for (int positionFrameIndex = 0; positionFrameIndex < inputChannel.PositionKeyCount; positionFrameIndex++)
                {
                    VectorKey positionKey = inputChannel.PositionKeys[positionFrameIndex];

                    Vector3 position = positionKey.Value.ToMonoGameVector3();

                    if (needsTransforming) (Matrix.CreateTranslation(position) * parentTransform).Decompose(out _, out _, out position);

                    if (KeyframeDecimalPlaceRounding > 0)
                    {
                        position.X = (float)Math.Round(position.X, KeyframeDecimalPlaceRounding);
                        position.Y = (float)Math.Round(position.Y, KeyframeDecimalPlaceRounding);
                        position.Z = (float)Math.Round(position.Z, KeyframeDecimalPlaceRounding);
                    }

                    positionFrames.Add(new Keyframe<Vector3>(positionFrameIndex, (int)Math.Round(positionKey.Time), position));
                }

                channelsByBoneName.Add(inputChannel.NodeName, new CBoneChannel(inputChannel.NodeName, scaleFrames, rotationFrames, positionFrames));
            }

            string name = inputAnimation.Name.Remove(0, inputAnimation.Name.IndexOf('|') + 1);

            animations.Add(new CAnimation(name, (int)Math.Round(inputAnimation.TicksPerSecond), (int)Math.Round(inputAnimation.DurationInTicks), channelsByBoneName));
        }

        return new CModelData(skinnedMeshData, animations, armature.Bones);
    }
    private static IEnumerable<(string SourceName, CMeshData FinalMeshData, List<CMorphTarget> MorphTargets)> EnumerateLodTargets(CModel model)
    {
        foreach (var bg in model.Bodygroups)
        {
            if (!string.IsNullOrEmpty(bg.SourceMeshName))
                yield return (bg.SourceMeshName, bg.MeshData, bg.MorphTargets);

            foreach (var entry in bg.LODMeshes)
            {
                if (entry == null || entry.Hidden || string.IsNullOrEmpty(entry.SourceMeshName)) continue;
                yield return (entry.SourceMeshName, entry.MeshData, entry.MorphTargets);
            }
        }
    }
    public static void ImportMorphTargets(List<(string meshName, string key, string path)> shapeKeys, string baseFbxPath)
    {
        if (ActiveModel == null || shapeKeys == null || shapeKeys.Count == 0) return;

        Scene baseRaw;
        try { baseRaw = ImportRaw(baseFbxPath); }
        catch (Exception e) { Console.WriteLine($"[Morph] Base raw import failed: {e.Message}"); return; }

        const float RF = 10000f;

        var rawMaps = new Dictionary<string, List<int>[]>();
        var targetsByName = new Dictionary<string, (CMeshData finalMeshData, List<CMorphTarget> morphTargets)>();

        foreach (var (sourceName, finalMeshData, morphTargets) in EnumerateLodTargets(ActiveModel))
        {
            targetsByName[sourceName] = (finalMeshData, morphTargets);

            var rawMesh = baseRaw.Meshes.FirstOrDefault(m => m.Name == sourceName);
            if (rawMesh == null)
            {
                Console.WriteLine($"[Morph] No raw mesh found for source '{sourceName}' skipping.");
                continue;
            }

            var finalVerts = finalMeshData.Vertices;
            var finalByPos = new Dictionary<(int, int, int), List<int>>(finalVerts.Length);
            for (int j = 0; j < finalVerts.Length; j++)
            {
                var key = Rnd(finalVerts[j].Position, RF);
                if (!finalByPos.TryGetValue(key, out var lst))
                    finalByPos[key] = lst = new List<int>(2);
                lst.Add(j);
            }

            int rawCount = rawMesh.VertexCount;
            var rawToFinal = new List<int>[rawCount];
            int matched = 0;

            for (int k = 0; k < rawCount; k++)
            {
                var key = Rnd(rawMesh.Vertices[k].ToMonoGameVector3(), RF);
                if (finalByPos.TryGetValue(key, out var lst)) { rawToFinal[k] = lst; matched++; }
            }

            Console.WriteLine($"[Morph] {sourceName}: {matched}/{rawCount} raw verts mapped to final");
            rawMaps[sourceName] = rawToFinal;
        }

        foreach (var (meshName, shapeName, path) in shapeKeys)
        {
            if (!rawMaps.TryGetValue(meshName, out var rawToFinal) || !targetsByName.TryGetValue(meshName, out var target))
            {
                Console.WriteLine($"[Morph] '{shapeName}': no target found for source mesh '{meshName}' skipping.");
                continue;
            }

            Scene morphRaw;
            try { morphRaw = ImportRaw(path); }
            catch (Exception e) { Console.WriteLine($"[Morph] {shapeName}: {e.Message}"); continue; }

            var morphMesh = morphRaw.Meshes.FirstOrDefault(m => m.Name == meshName) ?? morphRaw.Meshes[0];

            if (morphMesh.VertexCount != rawToFinal.Length)
            {
                Console.WriteLine($"[Morph] '{shapeName}' ({meshName}): raw count mismatch (base={rawToFinal.Length}, morph={morphMesh.VertexCount})");
                continue;
            }

            var morphVerts = morphMesh.Vertices;
            var finalVerts = target.finalMeshData.Vertices;
            const float Eps = 0.0001f;

            var jToDelta = new Dictionary<int, Vector3>(finalVerts.Length / 4);

            for (int k = 0; k < morphMesh.VertexCount; k++)
            {
                var finals = rawToFinal[k];
                if (finals == null) continue;

                var basePos = finalVerts[finals[0]].Position;
                var morphPos = morphVerts[k].ToMonoGameVector3();
                var delta = morphPos - basePos;

                if (delta.LengthSquared() <= Eps * Eps) continue;

                foreach (int j in finals) jToDelta[j] = delta;
            }

            var indices = jToDelta.Keys.ToArray();
            var deltas = jToDelta.Values.ToArray();

            target.morphTargets.Add(new CMorphTarget
            {
                Name = shapeName,
                Indices = indices,
                DeltaPositions = deltas,
                DeltaNormals = new Vector3[indices.Length]
            });

            Console.WriteLine($"[Morph] '{shapeName}' ({meshName}): {indices.Length} affected verts");
        }

        CMorphApplicatorFactory.EnsureMorphApplicators(ActiveModel, EditorHost.Instance.GraphicsDevice);
    }
    private static (int, int, int) Rnd(Vector3 v, float f) =>
        ((int)(v.X * f + (v.X < 0 ? -0.5f : 0.5f)),
         (int)(v.Y * f + (v.Y < 0 ? -0.5f : 0.5f)),
         (int)(v.Z * f + (v.Z < 0 ? -0.5f : 0.5f)));
    #endregion

    #region Armature Helpers
    private struct armatureData
    {
        #region Backing Fields
        private readonly List<CBoneData> bones;

        private readonly Dictionary<string, Node> boneNodesByBoneName;

        private readonly Dictionary<string, Assimp.Bone> bonesByNodeName;
        #endregion

        #region Properties
        /// <summary> The collection of bone nodes keyed by bone name. </summary>
        public IReadOnlyDictionary<string, Node> BoneNodesByBoneName => boneNodesByBoneName;

        public IReadOnlyDictionary<string, Assimp.Bone> BonesByNodeName => bonesByNodeName;

        public IReadOnlyList<CBoneData> Bones => bones;

        public Assimp.Bone RootBone { get; }

        public Node RootBoneNode { get; }
        #endregion

        #region Constructors
        public armatureData(Assimp.Bone rootBone, List<CBoneData> bones, Dictionary<string, Node> boneNodesByBoneName, Dictionary<string, Assimp.Bone> bonesByNodeName)
        {
            RootBone = rootBone;
            RootBoneNode = boneNodesByBoneName[rootBone.Name];

            this.bones = bones;
            this.boneNodesByBoneName = boneNodesByBoneName;
            this.bonesByNodeName = bonesByNodeName;
        }
        #endregion

        #region Bone Functions
        public int GetIndexOf(Assimp.Bone bone) => bones.FindIndex((data) => data.Name == bone.Name);

        public int GetParentIndex(Assimp.Bone bone)
        {
            string parentName = BoneNodesByBoneName[bone.Name].Parent.Name;
            return bones.FindIndex((data) => data.Name == parentName);
        }
        #endregion
    }
    private static bool isModelSkinned(Scene scene)
    {
        List<Assimp.Bone> bones = new List<Assimp.Bone>();

        // Add the bones of each mesh to the collection.
        foreach (Mesh mesh in scene.Meshes)
            foreach (Assimp.Bone bone in mesh.Bones)
                if (!bones.Contains(bone)) bones.Add(bone);

        // Create the raw collections to hold the data about the armature.
        List<BoneData> boneList = new List<BoneData>();
        Dictionary<string, Node> boneNodesByBoneName = new Dictionary<string, Node>();
        Dictionary<string, Assimp.Bone> bonesByNodeName = new Dictionary<string, Assimp.Bone>();

        Assimp.Bone rootBone = null;
        populateArmatureDictionary(bones, boneNodesByBoneName, bonesByNodeName, scene.RootNode, ref rootBone);

        return rootBone != null;
    }
    private static void buildArmature(Scene scene, out armatureData armature)
    {
        List<Assimp.Bone> bones = new List<Assimp.Bone>();

        // Add the bones of each mesh to the collection.
        foreach (Mesh mesh in scene.Meshes)
            foreach (Assimp.Bone bone in mesh.Bones)
                if (!bones.Contains(bone)) bones.Add(bone);

        // Create the raw collections to hold the data about the armature.
        List<CBoneData> boneList = new List<CBoneData>();
        Dictionary<string, Node> boneNodesByBoneName = new Dictionary<string, Node>();
        Dictionary<string, Assimp.Bone> bonesByNodeName = new Dictionary<string, Assimp.Bone>();

        // Populate the dictionaries, keying together bones and nodes.
        Assimp.Bone rootBone = null;
        populateArmatureDictionary(bones, boneNodesByBoneName, bonesByNodeName, scene.RootNode, ref rootBone);

        // Ensure a root bone exists.
        if (rootBone == null)
            throw new Exception("Cannot created skinned mesh of mesh with no bones!");

        // Create the armature with the references to the created collections.
        armature = new armatureData(rootBone, boneList, boneNodesByBoneName, bonesByNodeName);

        // Populate the bone list, this updates the underlying collection within the armature.
        int boneIndex = 0;
        populateArmatureBoneList(ref boneIndex, boneList, rootBone, armature);
    }

    private static void populateArmatureBoneList(ref int boneIndex, List<CBoneData> bones, Assimp.Bone currentBone, armatureData armature)
    {
        int parentIndex = armature.GetParentIndex(currentBone);
        Node boneNode = armature.BoneNodesByBoneName[currentBone.Name];

        bones.Add(new CBoneData
        {
            Name = currentBone.Name,
            Index = boneIndex,
            ParentIndex = parentIndex,
            PhysicsParentOverrideIndex = null,
            Offset = currentBone.OffsetMatrix.ToMonoGameMatrixTransposed(),
            LocalTransform = parentIndex >= 0
                ? boneNode.Transform.ToMonoGameMatrixTransposed()
                : (boneNode.Transform * boneNode.Parent.Transform).ToMonoGameMatrixTransposed()
        });

        boneIndex++;

        foreach (Node childBoneNode in boneNode.Children)
        {
            if (!armature.BonesByNodeName.TryGetValue(childBoneNode.Name, out var bone)) continue;
            populateArmatureBoneList(ref boneIndex, bones, bone, armature);
        }
    }

    private static void populateArmatureDictionary(List<Assimp.Bone> bones, Dictionary<string, Node> boneNodesByBoneName, Dictionary<string, Assimp.Bone> bonesByNodeName, Node currentNode, ref Assimp.Bone rootBone)
    {
        bool insideRecognizedBone = boneNodesByBoneName.ContainsKey(currentNode.Name);

        // Go over each child node in the given node.
        foreach (Node childNode in currentNode.Children)
        {
            // See if this node's name matches the name of any bone. If it does, then add it to the dictionary.

            Assimp.Bone matchedBone = null;

            foreach (Assimp.Bone bone in bones)
            {
                if (bone.Name == childNode.Name || bone.Name == "STATICMODEL_ROOT_BONE_REPLACE")
                {
                    matchedBone = bone;
                    break;
                }
            }

            // No skin data references this node
            if (matchedBone == null && insideRecognizedBone && childNode.MeshCount == 0)
            {
                matchedBone = new Assimp.Bone(childNode.Name, new(), Array.Empty<VertexWeight>());
                bones.Add(matchedBone);
            }

            if (matchedBone != null && !boneNodesByBoneName.ContainsKey(matchedBone.Name))
            {
                boneNodesByBoneName.Add(matchedBone.Name, childNode);
                bonesByNodeName.Add(matchedBone.Name, matchedBone);

                if (!boneNodesByBoneName.ContainsKey(currentNode.Name))
                    rootBone = matchedBone;
            }

            // If this node has children, recursively check them.
            populateArmatureDictionary(bones, boneNodesByBoneName, bonesByNodeName, childNode, ref rootBone);
        }
    }
    #endregion
}

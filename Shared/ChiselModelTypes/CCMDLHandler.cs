using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System.Collections.Generic;
using System.Linq;

namespace Chisel.Models.Data
{
    public static class CCMDLHandler
    {
        static MessagePackSerializerOptions serializerOptions;

        static CCMDLHandler()
        {
            var resolver = CompositeResolver.Create(
                new IMessagePackFormatter[] { new Byte4Formatter(), new ColorFormatter() },
                new IFormatterResolver[] { ContractlessStandardResolver.Instance }
            );
            serializerOptions = MessagePackSerializer.DefaultOptions.WithResolver(resolver);
        }

        public static CCMDL LoadCCMDL(byte[] data)
        {
            var ccmdl = MessagePackSerializer.Deserialize<CCMDL>(data, serializerOptions);
            return ccmdl;
        }

        public static CModel LoadFromCCMDL(byte[] data, GraphicsDevice graphicsDevice)
        {
            var ccmdl = MessagePackSerializer.Deserialize<CCMDL>(data, serializerOptions);

            CModel model = new CModel();
            if (ccmdl.animations != null) model.Animations = new List<CModel.CAnimDef>(ccmdl.animations);
            if (ccmdl.sequences != null) model.Sequences = new List<CModel.CSequence>(ccmdl.sequences);
            if (ccmdl.attachmentPoints != null) model.AttachmentPoints = new List<CModel.CAttachmentPoint>(ccmdl.attachmentPoints);
            if (ccmdl.eyeDefs != null) model.EyeDefs = new List<CModel.CEyeDef>(ccmdl.eyeDefs);
            if (ccmdl.lodLevels != null) model.LODLevels = new List<CModel.CLODLevel>(ccmdl.lodLevels);
            if (ccmdl.ikChains != null) model.IKChains = new List<CModel.CIKChain>(ccmdl.ikChains);
            model.EyeTexture = ccmdl.eyeTexture;
            model.BoneData = new List<CBoneData>(ccmdl.bones);
            model.Name = ccmdl.name;
            model.Bodygroups = ccmdl.bodyGroups.Select(g =>
            {
                CModel.CBodyGroup bodyGroup = new CModel.CBodyGroup();

                bodyGroup.MeshData = g.meshData;
                bodyGroup.Name = g.name;
                bodyGroup.Offset = g.offset;
                bodyGroup.MaterialID = GlobalMapData.MaterialNameToIndex[g.material];
                bodyGroup.IsSkinned = g.skinned;
                bodyGroup.IsEye = g.isEye;
                bodyGroup.Mesh = CSkinnedMesh.CreateFrom(graphicsDevice, g.meshData);
                bodyGroup.MorphTargets = g.morphTargets?.ToList() ?? new List<CMorphTarget>();

                if(g.meshLODs != null)
                {
                    foreach (var lodmesh in g.meshLODs)
                    {
                        if (lodmesh == null)
                        {
                            bodyGroup.LODMeshes.Add(null!);
                            continue;
                        }

                        CModel.CLODMeshEntry lodMesh = new CModel.CLODMeshEntry();

                        lodMesh.MorphTargets = lodmesh.MorphTargets;
                        lodMesh.SourceMeshName = lodmesh.SourceMeshName;
                        lodMesh.Hidden = lodmesh.Hidden;
                        lodMesh.MeshData = lodmesh.MeshData;
                        if(lodmesh.MeshData.VertexCount > 0) lodMesh.Mesh = CSkinnedMesh.CreateFrom(graphicsDevice, lodmesh.MeshData);

                        bodyGroup.LODMeshes.Add(lodMesh);
                    }
                }

                return bodyGroup;
            }).ToList();
            model.Bones = new List<CBone>();
            foreach (var bone in ccmdl.bones)
            {
                model.Bones.Add(CBone.CreateFrom(model, bone));
            }
            model.ResolveIKChains();
            return model;
        }
    }

    public class CCMDLBodyGroup
    {
        public CMeshData meshData { get; set; }
        public CCMDLLODMesh[] meshLODs { get; set; }
        public Matrix offset { get; set; }
        public string name { get; set; }
        public bool skinned { get; set; }
        public bool isEye { get; set; }
        public string material { get; set; }
        public CMorphTarget[] morphTargets { get; set; }
    }

    public class CCMDLLODMesh
    {
        public bool Hidden { get; set; }
        public CMeshData MeshData { get; set; }
        public string SourceMeshName { get; set; }
        public List<CMorphTarget> MorphTargets { get; set; } = new();
    }

    public class CCMDL
    {
        public string name { get; set; }
        public CCMDLBodyGroup[] bodyGroups { get; set; }
        public CModel.CLODLevel[] lodLevels { get; set; }
        public CModel.CEyeDef[] eyeDefs { get; set; }
        public CModel.CAnimDef[] animations { get; set; }
        public CModel.CSequence[] sequences { get; set; }
        public CModel.CAttachmentPoint[] attachmentPoints { get; set; }
        public CModel.CIKChain[] ikChains { get; set; }
        public EyeTextureRecipe eyeTexture { get; set; } = EyeTextureRecipe.Default;
        public CBoneData[] bones { get; set; }
    }
}
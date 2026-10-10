using Relic.Models;
using Relic.Models.Data;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System.Linq;
using static Relic.Models.CModel;

namespace Rockwall2.Editor.Common.Utils;

public static class CCMDLWriter
{
    static MessagePackSerializerOptions serializerOptions;

    static CCMDLWriter()
    {
        var resolver = CompositeResolver.Create(
            new IMessagePackFormatter[] { new Byte4Formatter(), new ColorFormatter() },
            new IFormatterResolver[] { ContractlessStandardResolver.Instance }
        );
        serializerOptions = MessagePackSerializer.DefaultOptions.WithResolver(resolver);
    }

    public static byte[] GetCCMDLWriteableData(CModel source)
    {
        CCMDL mdl = new CCMDL();
        mdl.name = source.Name;
        if (source.Animations != null) mdl.animations = source.Animations.ToArray();
        if (source.Sequences != null) mdl.sequences = source.Sequences.ToArray();
        if (source.AttachmentPoints != null) mdl.attachmentPoints = source.AttachmentPoints.ToArray();
        if (source.EyeDefs != null) mdl.eyeDefs = source.EyeDefs.ToArray();
        if (source.LODLevels != null) mdl.lodLevels = source.LODLevels.ToArray();
        if (source.IKChains != null) mdl.ikChains = source.IKChains.ToArray();
        mdl.eyeTexture = source.EyeTexture;
        mdl.bones = source.BoneData.ToArray();
        mdl.bodyGroups = source.Bodygroups.Select(g =>
        {
            var bGroup = new CCMDLBodyGroup();
            bGroup.name = g.Name;
            bGroup.meshData = g.MeshData;
            bGroup.material = GlobalMapData.LoadedMaterials[g.MaterialID].Name;
            bGroup.skinned = g.IsSkinned;
            bGroup.isEye = g.IsEye;
            bGroup.offset = g.Offset;
            bGroup.morphTargets = g.MorphTargets?.ToArray() ?? [];

            bGroup.meshLODs = g.LODMeshes.Select(src =>
            {
                CCMDLLODMesh lodMesh = new CCMDLLODMesh();

                // Still need something here.
                if (src == null) return null;

                lodMesh.MorphTargets = src.MorphTargets;
                lodMesh.SourceMeshName = src.SourceMeshName;
                lodMesh.Hidden = src.Hidden;
                lodMesh.MeshData = src.MeshData;

                return lodMesh;
            }).ToArray()!;

            return bGroup;
        }).ToArray();

        return MessagePackSerializer.Serialize(mdl, serializerOptions);
    }

    public static CModel LoadFromCCMDL(byte[] data, GraphicsDevice graphicsDevice)
    {
        return CCMDLHandler.LoadFromCCMDL(data, graphicsDevice);
    }
}
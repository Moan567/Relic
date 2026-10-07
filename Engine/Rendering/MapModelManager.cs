using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Rendering;
public static class MapModelManager
{
    private static FrozenDictionary<uint, RenderableMapModel[]> leafToModels;
    public static void SetModels(MapPropModel[] models)
    {
        if (models == null)
        {
            leafToModels = null;
            return;
        }

        var graphics = MainEngine.Instance.GraphicsDevice;

        Dictionary<uint, List<RenderableMapModel>> tempModels = new();

        foreach (var mdl in models)
        {
            if (!tempModels.TryGetValue(mdl.VisLeaf, out var list))
            {
                list = new List<RenderableMapModel>();
                tempModels.Add(mdl.VisLeaf, list);
            }

            int materialID = GlobalMapData.MaterialNameToIndex[mdl.Material];

            var material = GlobalMapData.LoadedMaterials[materialID];

            var vb = new VertexBuffer(
                graphics,
                typeof(MapPropModelVertex),
                mdl.Vertices.Length,
                BufferUsage.WriteOnly);

            vb.SetData(mdl.Vertices);

            var ib = new IndexBuffer(
                graphics,
                IndexElementSize.ThirtyTwoBits,
                mdl.Indices.Length,
                BufferUsage.WriteOnly);

            ib.SetData(mdl.Indices);

            list.Add(new RenderableMapModel
            {
                VisLeaf = mdl.VisLeaf,
                MaterialID = materialID,
                NoCull = material.NoCull,
                VertexBuffer = vb,
                IndexBuffer = ib,
                PrimitiveCount = mdl.Indices.Length / 3
            });
        }

        leafToModels = tempModels
            .Select(x => new KeyValuePair<uint, RenderableMapModel[]>(
                x.Key,
                x.Value.ToArray()))
            .ToFrozenDictionary();
    }
    public static RenderableMapModel[] GetModelsAtLeaf(uint leaf)
    {
        if (leafToModels?.TryGetValue(leaf, out var mdls) ?? false) return mdls;

        return null;
    }
}
public sealed class RenderableMapModel : IDisposable
{
    public uint VisLeaf;

    public int MaterialID;

    public VertexBuffer VertexBuffer;
    public IndexBuffer IndexBuffer;

    public int PrimitiveCount;

    public bool NoCull;

    public void Dispose()
    {
        VertexBuffer?.Dispose();
        IndexBuffer?.Dispose();
    }
}
using Chisel.Models;
using Chisel.Models.Data;
using Chisel.Utils;
using MapCompiler.Compilation;
using MapCompiler.Compilation.GPU;
using MapCompiler.Compilation.GPU.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MapCompiler.Compilation;

public static class ModelTracker
{
    struct TrackedModel
    {
        public Vector3 Origin;
        public string Material;
        public List<CSkinnedVertex> Vertices;
        public int[] Indices;
    }

    static ConcurrentBag<TrackedModel> SparseModels = new();
    public static bool Any;

    public static void LoadAll(EntityReference[] entityReferences)
    {
        Any = false;
        Parallel.For(0, entityReferences.Length, e =>
        {
            var entity = entityReferences[e];
            if (entity.EntityName != "DetailModel") return;
            if (entity.Properties == null || entity.Properties.Length <= 0) return;

            var filepath = Path.Combine(
                Program.WorkingDir,
                Path.ChangeExtension(entity.Properties[0].Value, "ccmdl"));

            if (!File.Exists(filepath) || Path.GetExtension(filepath) != ".ccmdl") return;

            var mat = Matrix.CreateFromYawPitchRoll(
                          MathHelper.ToRadians(entity.SpawnRotation.X),
                          MathHelper.ToRadians(entity.SpawnRotation.Y),
                          MathHelper.ToRadians(entity.SpawnRotation.Z)) *
                      Matrix.CreateWorld(entity.Position, Vector3.Forward, Vector3.Up);

            var model = CCMDLHandler.LoadCCMDL(File.ReadAllBytes(filepath));

            foreach (var bg in model.bodyGroups)
            {
                var verts = bg.meshData.Vertices.Select(p =>
                {
                    var newp = p;
                    newp.Position = Vector3.Transform(p.Position, mat);
                    newp.Normal = Vector3.TransformNormal(p.Normal, mat);
                    return newp;
                }).ToList();

                if (verts.Count == 0) continue;

                SparseModels.Add(new TrackedModel
                {
                    Origin = entity.Position,
                    Material = bg.material,
                    Vertices = verts,
                    Indices = bg.meshData.Indices
                });
                Any = true;
            }
        });
    }

    public static List<MapPropModel> ConcatModels(List<Light> lights)
    {
        var detailModels = new List<MapPropModel>();
        var groupedModels = SparseModels.GroupBy(t => (Leaf: BSPRoot.Traverse(t.Origin), t.Material));

        foreach (var g in groupedModels)
        {
            var rawVerts = g.SelectMany(m => m.Vertices).ToArray();
            var bakedVerts = new MapPropModelVertex[rawVerts.Length];
            var tris = new List<int>();

            int triOffset = 0;
            foreach(var m in g)
            {
                tris.AddRange(m.Indices.Select(i=>i+triOffset));
                triOffset += m.Vertices.Count;
            }

            Parallel.For(0, rawVerts.Length, i =>
            {
                var v = rawVerts[i];
                Vector3 lit = BakeVertexLight(v.Position, Vector3.Normalize(v.Normal), lights);

                bakedVerts[i] = new MapPropModelVertex
                {
                    Position = v.Position,
                    Normal = v.Normal,
                    TextureCoordinate = v.UV,
                    Color = lit,
                };
            });

            var modelGroup = new MapPropModel
            {
                VisLeaf = g.Key.Leaf,
                Material = g.Key.Material,
                Vertices = bakedVerts,
                Indices = tris.ToArray()
            };

            detailModels.Add(modelGroup);
        }

        return detailModels;
    }
    public static List<MapPropModel> ConcatModelsGpu(
        GpuLightmapper gpu,
        List<Light> lights,
        GpuBuffer texelHomePatchBuffer,
        Vector3[] patchFinalArr,
        int lightmapResolution,
        Vector3 ambientColor,
        float ambientIntensity)
    {
        var groupList = SparseModels
            .GroupBy(t => (Leaf: BSPRoot.Traverse(t.Origin), t.Material))
            .ToList();

        var rawVertsPerGroup = new CSkinnedVertex[groupList.Count][];
        var trisPerGroup = new int[groupList.Count][];
        int totalVerts = 0;

        for (int g = 0; g < groupList.Count; g++)
        {
            rawVertsPerGroup[g] = groupList[g].SelectMany(m => m.Vertices).ToArray();
            totalVerts += rawVertsPerGroup[g].Length;

            var tris = new List<int>();
            int triOffset = 0;
            foreach (var m in groupList[g])
            {
                tris.AddRange(m.Indices.Select(i => i + triOffset));
                triOffset += m.Vertices.Count;
            }
            trisPerGroup[g] = tris.ToArray();
        }

        var flatPositions = new Vector3[totalVerts];
        var flatNormals = new Vector3[totalVerts];

        int cursor = 0;
        for (int g = 0; g < groupList.Count; g++)
        {
            foreach (var v in rawVertsPerGroup[g])
            {
                Vector3 normal = v.Normal.LengthSquared() > 1e-12f ? Vector3.Normalize(v.Normal) : Vector3.Up;
                flatPositions[cursor] = v.Position + normal * 0.005f;
                flatNormals[cursor] = normal;
                cursor++;
            }
        }

        using var vertResources = gpu.PrepareVertexResources(flatPositions, flatNormals);
        using var indirectPositions = gpu.PrepareFlatPositions(flatPositions);

        Vector3[] direct = gpu.BakePropVertexDirect(vertResources, lights);
        Vector3[][] indirectCoeffs = gpu.BakeLightNodeSH(
            indirectPositions, texelHomePatchBuffer, patchFinalArr, lightmapResolution, ambientColor, ambientIntensity);

        var detailModels = new List<MapPropModel>(groupList.Count);
        cursor = 0;

        for (int g = 0; g < groupList.Count; g++)
        {
            var rawVerts = rawVertsPerGroup[g];
            var bakedVerts = new MapPropModelVertex[rawVerts.Length];

            for (int i = 0; i < rawVerts.Length; i++)
            {
                var v = rawVerts[i];
                float[] basis = SphericalHarmonicsUtils.EvaluateSHBasis(flatNormals[cursor]);

                Vector3 indirect = Vector3.Zero;
                for (int k = 0; k < 9; k++)
                {
                    indirect += indirectCoeffs[cursor][k] * basis[k];
                }

                bakedVerts[i] = new MapPropModelVertex
                {
                    Position = v.Position,
                    Normal = v.Normal,
                    TextureCoordinate = v.UV,
                    Color = Vector3.Max(direct[cursor] + indirect * 2, Vector3.Zero),
                };

                cursor++;
            }

            detailModels.Add(new MapPropModel
            {
                VisLeaf = groupList[g].Key.Leaf,
                Material = groupList[g].Key.Material,
                Vertices = bakedVerts,
                Indices = trisPerGroup[g],
            });
        }

        return detailModels;
    }
    public static List<BvhTriangle> BuildBvhTriangles()
    {
        var tris = new List<BvhTriangle>();

        foreach (var model in SparseModels)
        {
            for (int t = 0; t < model.Indices.Length; t += 3)
            {
                int i0 = model.Indices[t];
                int i1 = model.Indices[t + 1];
                int i2 = model.Indices[t + 2];

                var v0 = model.Vertices[i0];
                var v1 = model.Vertices[i1];
                var v2 = model.Vertices[i2];

                tris.Add(new BvhTriangle
                {
                    V0 = v0.Position,
                    V1 = v1.Position,
                    V2 = v2.Position,
                    Uv0 = v0.UV,
                    Uv1 = v1.UV,
                    Uv2 = v2.UV,
                    Albedo = new Vector3(0),
                    SourceBrush = -1,
                    EntityGroup = -1,
                    IsSkybox = false,
                });
            }
        }

        return tris;
    }
    private static Vector3 BakeVertexLight(Vector3 position, Vector3 normal, List<Light> lights)
    {
        // Lift slightly off the surface to avoid self-intersection
        Vector3 samplePos = position + normal * 0.005f;

        Vector3 direct = Vector3.Zero;
        foreach (var light in lights)
        {
            LightSample s = light.Type switch
            {
                Light.LightType.Point => LightCalculator.FromPoint(
                                                   light, samplePos, normal, normal, normal),
                Light.LightType.Directional => LightCalculator.FromDirectional(
                                                   light, samplePos, normal, normal, normal),
                Light.LightType.SpotLight => LightCalculator.FromSpot(
                                                   light, samplePos, normal, normal, normal),
                _ => default,
            };
            // this stupid util and its / 255f all the time. Thanks me.
            direct += new Vector3(s.B1.R, s.B1.G, s.B1.B) / 255f / 255f;
        }

        Vector3 indirect = Vector3.Zero;
        float totalWeight = 0;
        var nodeBundle = LightNodeTraversal.GetClosestNodeBundle(samplePos);

        if (nodeBundle?.Children is { Length: > 0 } children)
        {
            foreach(var child in children)
            {
                float distance = Vector3.Distance(position, child.Pos);

                if (BSPRoot.TraceRay(new Ray(position, Vector3.Normalize(child.Pos - position)), distance).Hit) continue;

                float weight = 1.0f / (distance + 0.001f);

                var coeffs = child.IndirectCoefficients;
                if (coeffs != null)
                {
                    float[] basis = SphericalHarmonicsUtils.EvaluateSHBasis(normal);
                    int bands = Math.Min(9, coeffs.Length);
                    for (int k = 0; k < bands; k++)
                        indirect += coeffs[k] * basis[k] * weight;

                    totalWeight += weight;

                    indirect = Vector3.Max(indirect, Vector3.Zero);
                }
            }
            indirect /= totalWeight;
        }

        return Vector3.Max(direct + indirect * 2, Vector3.Zero);
    }
}
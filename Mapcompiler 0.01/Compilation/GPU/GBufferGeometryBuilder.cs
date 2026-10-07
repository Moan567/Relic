using MapCompiler.Compilation.GPU.Resources;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;
internal class GBufferGeometryBuilder
{

    public static GBufferVertex[] BuildGBufferGeometry(
        Brush[] brushes, Terrain[] terrains,
        Dictionary<(int brush, int face, int vertex), SmoothedVertexData> smoothedNormals)
    {
        var verts = new List<GBufferVertex>();

        for (int i = 0; i < brushes.Length; i++)
        {
            int entityGroup = TriangleOccluder.GetBrushEntityGroup(i);
            for (int f = 0; f < brushes[i].Faces.Length; f++)
            {
                var face = brushes[i].Faces[f];
                if (face.toolFace) continue;

                var faceLoop = SmoothGroups.BuildFaceLoop(brushes[i], f);
                for (int t = 0; t < face.Indices.Length; t += 3)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int vi = face.Indices[t + c];
                        Vector3 worldPos = brushes[i].Vertices[vi] + brushes[i].Position;
                        var vd = SmoothGroups.SampleAt(brushes[i], i, f, worldPos, smoothedNormals, faceLoop);

                        verts.Add(new GBufferVertex
                        {
                            Position = worldPos,
                            Normal = vd.Normal,
                            Basis1 = vd.Basis1,
                            Basis2 = vd.Basis2,
                            Basis3 = vd.Basis3,
                            LightmapUV = brushes[i].LightmapUVs[vi],
                            SourceBrush = i,
                            EntityGroup = entityGroup
                        });
                    }
                }
            }
        }

        for (int ti = 0; ti < terrains.Length; ti++)
        {
            for (int t = 0; t < terrains[ti].Triangles.Length; t += 3)
            {
                for (int c = 0; c < 3; c++)
                {
                    int vi = terrains[ti].Triangles[t + c];
                    var vert = terrains[ti].Vertices[vi];
                    Vector3 normal = vert.Normal;
                    Vector4 tangent4 = vert.Tangent.ToVector4();
                    Vector3 tRaw = Vector3.Normalize(new Vector3(tangent4.X, tangent4.Y, tangent4.Z));
                    float hand = tangent4.W >= 0f ? 1f : -1f;
                    Vector3 tang = Vector3.Normalize(tRaw - normal * Vector3.Dot(normal, tRaw));
                    Vector3 binorm = Vector3.Cross(normal, tang) * hand;

                    verts.Add(new GBufferVertex
                    {
                        Position = vert.Position,
                        Normal = normal,
                        Basis1 = Vector3.Normalize(MapCompileOrchestrator.B1.X * tang + MapCompileOrchestrator.B1.Y * binorm + MapCompileOrchestrator.B1.Z * normal),
                        Basis2 = Vector3.Normalize(MapCompileOrchestrator.B2.X * tang + MapCompileOrchestrator.B2.Y * binorm + MapCompileOrchestrator.B2.Z * normal),
                        Basis3 = Vector3.Normalize(MapCompileOrchestrator.B3.X * tang + MapCompileOrchestrator.B3.Y * binorm + MapCompileOrchestrator.B3.Z * normal),
                        LightmapUV = terrains[ti].lightmapUvs[vi],
                        SourceBrush = -1,
                        EntityGroup = -1
                    });
                }
            }
        }

        return verts.ToArray();
    }
}

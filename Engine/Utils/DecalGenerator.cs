using Chisel.Collision;
using Chisel.Utils;
using Engine.Console;
using Engine.Rendering;
using Microsoft.VisualBasic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Engine.Utils
{
    public enum DecalEffect
    {
        Overlay,
        Add,
        Multiply
    }
    public class RealtimeDecal
    {
        public bool permanent;
        public DecalVertex[] vertices;
        public ushort[] indices;
        public Texture2D texture;
        public DecalEffect effect;
        public ShaderHandle customShader;
        public Vector2 uvScale = Vector2.One;
        public Vector2 uvOffset = Vector2.Zero;
    }
    public static class DecalManager
    {
        const int MAXDECAL = 2048;
        const int MAXUNIQUEDECALMATS = 1024; // pretty unlikely that this fills up I think.
        static int curDecal;
        public static RealtimeDecal[] RealtimeDecals = new RealtimeDecal[MAXDECAL];

        public static ShaderHandle Shader => shader;

        static ShaderHandle shader;
        static BlendState[] blendStates = new BlendState[3];
        static RasterizerState state;
        private class DecalRun
        {
            public List<ushort> Indices = new();
            public DynamicIndexBuffer IndexBuffer;
            public Texture2D Texture;
            public DecalEffect Effect;
            public ShaderHandle CustomShader;
        }

        private static Dictionary<(Texture2D, ShaderHandle, DecalEffect), DecalRun> decalRunLookup = new();
        private static List<DecalRun> activeDecalRuns = new();
        private static DynamicVertexBuffer decalBatchVertexBuffer;
        private static bool decalsDirty = true;

        public static void Init()
        {
            shader ??= ShaderBuilder.BuildParticlesShader(MainEngine.Instance.GraphicsDevice);

            blendStates[0] = new BlendState
            {
                ColorSourceBlend = Blend.SourceAlpha,
                ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaSourceBlend = Blend.One,
                AlphaDestinationBlend = Blend.InverseSourceAlpha,
            };
            blendStates[1] = new BlendState
            {
                ColorBlendFunction = BlendFunction.Add,
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One,
            };
            blendStates[2] = new BlendState
            {
                ColorBlendFunction = BlendFunction.Add,
                ColorSourceBlend = Blend.DestinationColor,
                ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaSourceBlend = Blend.One,
                AlphaDestinationBlend = Blend.InverseSourceAlpha,
            };
            state = new RasterizerState();
            state.CullMode = CullMode.CullClockwiseFace;

            decalBatchVertexBuffer = new DynamicVertexBuffer(MainEngine.Instance.GraphicsDevice,
                typeof(DecalVertex),
                MAXDECAL*64, // probably a safe guess?
                BufferUsage.WriteOnly);
        }
        public static void SetLightmaps(Texture2D b1, Texture2D b2, Texture2D b3)
        {
            shader.Param("LightmapB1").SetValue(b1);
            shader.Param("LightmapB2").SetValue(b2);
            shader.Param("LightmapB3").SetValue(b3);
            shader.Param("LightmapSize").SetValue(b1.Bounds.Size.ToVector2());
        }

        public static void MarkDirty()
        {
            decalsDirty = true;
        }

        public static int AddAndGetIndex(RealtimeDecal d)
        {
            RealtimeDecals[curDecal] = d;
            int old = curDecal;

            int iter = 0; 
            curDecal++; curDecal %= MAXDECAL;
            while ((RealtimeDecals[curDecal] != null && RealtimeDecals[curDecal].permanent) && iter < MAXDECAL) { curDecal++; curDecal %= MAXDECAL; iter++; }

            decalsDirty = true;

            return old;
        }


        internal static void RebuildDecalRunsIfDirty()
        {
            if (!decalsDirty)
            {
                return;
            }

            RebuildDecalRuns();
            decalsDirty = false;
        }

        internal static void RebuildDecalRuns()
        {
            foreach (DecalRun run in activeDecalRuns)
            {
                run.Indices.Clear();
            }
            activeDecalRuns.Clear();

            int vertexAllowance = 0;
            for (int i = 0; i < MAXDECAL; i++)
            {
                if (RealtimeDecals[i] is null) continue;
                if (RealtimeDecals[i].vertices.Length <= 0) continue;

                vertexAllowance += RealtimeDecals[i].vertices.Length;
            }

            if (vertexAllowance == 0)
            {
                return;
            }

            if (vertexAllowance > decalBatchVertexBuffer.VertexCount)
            {
                Logger.AppendError($"Decal vertex allowance {vertexAllowance} exceeds vertex buffer capacity {decalBatchVertexBuffer.VertexCount}.");
                vertexAllowance = decalBatchVertexBuffer.VertexCount;
            }

            DecalVertex[] vertices = new DecalVertex[vertexAllowance];
            int currentVertexOffset = 0;

            for (int i = 0; i < MAXDECAL; i++)
            {
                RealtimeDecal decal = RealtimeDecals[i];

                if (decal is null) continue;
                if (decal.vertices.Length <= 0) continue;
                if (currentVertexOffset + decal.vertices.Length > vertexAllowance) break;

                var key = (decal.texture, decal.customShader, decal.effect);

                if (!decalRunLookup.TryGetValue(key, out DecalRun run))
                {
                    run = new DecalRun
                    {
                        Texture = decal.texture,
                        CustomShader = decal.customShader,
                        Effect = decal.effect,
                    };
                    decalRunLookup[key] = run;
                }

                if (run.Indices.Count == 0)
                {
                    activeDecalRuns.Add(run);
                }

                for (int j = 0; j < decal.vertices.Length; j++)
                {
                    vertices[j + currentVertexOffset] = decal.vertices[j];
                }

                for (int k = 0; k < decal.indices.Length; k++)
                {
                    run.Indices.Add((ushort)(decal.indices[k] + currentVertexOffset));
                }

                currentVertexOffset += decal.vertices.Length;
            }

            List<(Texture2D, ShaderHandle, DecalEffect)> staleKeys = null;
            foreach (var pair in decalRunLookup)
            {
                if (!activeDecalRuns.Contains(pair.Value))
                {
                    staleKeys ??= new List<(Texture2D, ShaderHandle, DecalEffect)>();
                    staleKeys.Add(pair.Key);
                }
            }

            if (staleKeys != null)
            {
                for (int i = 0; i < staleKeys.Count; i++)
                {
                    decalRunLookup[staleKeys[i]].IndexBuffer?.Dispose();
                    decalRunLookup.Remove(staleKeys[i]);
                }
            }

            for (int i = 0; i < activeDecalRuns.Count; i++)
            {
                DecalRun run = activeDecalRuns[i];

                if (run.IndexBuffer == null || run.Indices.Count > run.IndexBuffer.IndexCount)
                {
                    run.IndexBuffer?.Dispose();
                    run.IndexBuffer = new DynamicIndexBuffer(MainEngine.Instance.GraphicsDevice, IndexElementSize.SixteenBits, run.Indices.Count, BufferUsage.WriteOnly);
                }

                run.IndexBuffer.SetData(run.Indices.ToArray());
            }

            decalBatchVertexBuffer.SetData(vertices, 0, vertices.Length);
        }

        public static void RemoveDecal(int index)
        {
            RealtimeDecals[index] = null;
            decalsDirty = true;
        }

        public static void ClearAllDecals()
        {
            RealtimeDecals = new RealtimeDecal[MAXDECAL];
            decalsDirty = true;
        }

        public static void RenderAllDecals()
        {
            if (shader.IsDisposed || shader.GraphicsDevice == null) return;

            RebuildDecalRunsIfDirty();

            var old = MainEngine.Instance.GraphicsDevice.RasterizerState;

            MainEngine.Instance.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

            MainEngine.Instance.GraphicsDevice.RasterizerState = state;
            MainEngine.Instance.GraphicsDevice.SetVertexBuffer(decalBatchVertexBuffer);

            shader.SetTechnique("Decals");

            for (int i = 0; i < activeDecalRuns.Count; i++)
            {
                var run = activeDecalRuns[i];

                if (RenderEngine.CurrentWireframeDisplayMode == 0) MainEngine.Instance.GraphicsDevice.BlendState = blendStates[(int)run.Effect];
                else MainEngine.Instance.GraphicsDevice.BlendState = BlendState.Opaque;

                shader.Param("World").SetValue(RenderEngine.WorldMatrix);
                shader.Param("View").SetValue(RenderEngine.ViewMatrix);
                shader.Param("Projection").SetValue(RenderEngine.ProjectionMatrix);
                shader.Param("MainTex").SetValue(run.Texture);
                shader.Param("AlphaClip").SetValue(false);
                shader.Param("UseVertColor").SetValue(run.Effect == DecalEffect.Overlay);

                if (run.CustomShader != null)
                {
                    run.CustomShader.Param("World").SetValue(RenderEngine.WorldMatrix);
                    run.CustomShader.Param("View").SetValue(RenderEngine.ViewMatrix);
                    run.CustomShader.Param("Projection").SetValue(RenderEngine.ProjectionMatrix);
                    run.CustomShader.Param("Texture").SetValue(run.Texture);
                    run.CustomShader.Param("TextureSize").SetValue(new Vector2(run.Texture.Width, run.Texture.Height));
                }

                var renderShader = run.CustomShader ?? shader;

                MainEngine.Instance.GraphicsDevice.Indices = run.IndexBuffer;
                renderShader.RenderEachPass(() =>
                    MainEngine.Instance.GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, run.Indices.Count / 3));
            }

            MainEngine.Instance.GraphicsDevice.RasterizerState = old;
        }
    }
    public static class DecalGenerator
    {
        sealed class Vector3EpsilonComparer : IEqualityComparer<Vector3>
        {
            const float eps = 1e-4f;
            public bool Equals(Vector3 a, Vector3 b)
                => Vector3.DistanceSquared(a, b) < eps * eps;
            public int GetHashCode(Vector3 v)
            {
                int xi = (int)MathF.Round(v.X / eps);
                int yi = (int)MathF.Round(v.Y / eps);
                int zi = (int)MathF.Round(v.Z / eps);
                return xi ^ (yi << 10) ^ (zi << 20);
            }
        }

        sealed class RadialSortComparer : IComparer<Vector3>
        {
            public Vector3 center;
            public Vector3 axisX;
            public Vector3 axisY;
            public bool descending;

            public int Compare(Vector3 a, Vector3 b)
            {
                Vector3 da = a - center;
                Vector3 db = b - center;

                float angleA = MathF.Atan2(Vector3.Dot(da, axisY), Vector3.Dot(da, axisX));
                float angleB = MathF.Atan2(Vector3.Dot(db, axisY), Vector3.Dot(db, axisX));

                return descending ? angleB.CompareTo(angleA) : angleA.CompareTo(angleB);
            }
        }

        static readonly Vector3EpsilonComparer vertexComparer = new Vector3EpsilonComparer();
        static readonly RadialSortComparer radialSort = new RadialSortComparer();

        static readonly List<Vector3> polygonScratch = new List<Vector3>(32);
        static readonly List<Vector3> clipBufferA = new List<Vector3>(32);
        static readonly List<Vector3> clipBufferB = new List<Vector3>(32);
        static readonly List<Vector3> clippedScratch = new List<Vector3>(32);
        static readonly List<Vector3> collinearScratch = new List<Vector3>(32);
        static readonly List<Vector3> triangleScratch = new List<Vector3>(3);
        static readonly List<DecalVertex> localVertsScratch = new List<DecalVertex>(32);

        public static int ProjectDecal(OrientedBoundingBox decalBounds, Texture2D surface, int modifyExistingIndex = -1, Vector2? uvScale = null, Vector2? uvOffset = null)
        {
            RealtimeDecal decal;
            int id = DecalManager.RealtimeDecals.Length;

            if (modifyExistingIndex == -1)
            {
                decal = new RealtimeDecal();
                decal.texture = surface;
            }
            else
            {
                decal = DecalManager.RealtimeDecals[modifyExistingIndex];
                decal.texture = surface;
                id = modifyExistingIndex;
            }

            decal.uvScale = uvScale ?? Vector2.One;
            decal.uvOffset = uvOffset ?? Vector2.Zero;

            Plane[] clipPlanes = decalBounds.GetPlanes();
            Matrix inverseTransform = Matrix.Invert(decalBounds.Transformation);
            BoundingBox decalAABB = decalBounds.GetBoundingBox();
            List<DecalVertex> vertices = new List<DecalVertex>();
            List<ushort> indicies = new List<ushort>();

            void ProcessVerts(List<Vector3> clippedPolygon, Vector3 normal, Vector3 tangent, Vector3 binormal,
                               Vector3 refA, Vector2 lmA, Vector3 refB, Vector2 lmB, Vector3 refC, Vector2 lmC)
            {
                if (clippedPolygon.Count == 0) return;

                localVertsScratch.Clear();

                if (clippedPolygon.Count >= 3)
                {
                    foreach (Vector3 worldPos in clippedPolygon)
                    {
                        Vector3 localPos = Vector3.Transform(worldPos, inverseTransform);

                        Vector2 uv = new Vector2(
                            (localPos.X / (decalBounds.Extents.X * 2)) + 0.5f,
                            (localPos.Y / (decalBounds.Extents.Y * 2)) + 0.5f
                        );
                        uv = uv * decal.uvScale + decal.uvOffset;

                        float d = (localPos.Z / (decalBounds.Extents.Y * 2)) + 0.5f;

                        Barycentric(worldPos, refA, refB, refC, out float bu, out float bv, out float bw);
                        Vector2 lightmapUV = lmA * bu + lmB * bv + lmC * bw;

                        DecalVertex decalVertex = new DecalVertex()
                        {
                            Position = worldPos + normal * 0.01f,
                            Normal = normal,
                            TextureCoordinate = uv,
                            LightmapCoordinate = lightmapUV,
                            Tangent = tangent,
                            Binormal = binormal,
                            Color = new Vector4(0f, 0f, 0f, float.Clamp(d,0,1)),
                        };

                        if (!localVertsScratch.Contains(decalVertex))
                            localVertsScratch.Add(decalVertex);
                    }
                }

                int start = vertices.Count;
                vertices.AddRange(localVertsScratch);
                for (int t = 0; t < localVertsScratch.Count - 1; t++)
                {
                    indicies.Add((ushort)start);
                    indicies.Add((ushort)(start + t));
                    indicies.Add((ushort)(start + t + 1));
                }
            }

            for (int i = 0; i < GlobalMapData.ActiveMap.Brushes.Length; i++)
            {
                int brushID = i;

                if (decalAABB.Contains(GlobalMapData.ActiveMap.BrushBounds[brushID]) == ContainmentType.Disjoint)
                {
                    continue;
                }

                Brush brush = GlobalMapData.ActiveMap.Brushes[brushID];

                if (brush.IsClip || (brush.IsEntity && !brush.IsDetail) || brush.IsLightNodeVolume)
                {
                    continue;
                }

                for (int f = 0; f < GlobalMapData.ActiveMap.Brushes[brushID].Faces.Length; f++)
                {
                    Face face = GlobalMapData.ActiveMap.Brushes[brushID].Faces[f];

                    if (Vector3.Dot(face.Normal, decalBounds.Transformation.Forward) < 0)
                        continue;

                    if (face.Indices.Length < 3) continue;

                    Vector3 p0 = brush.Vertices[face.Indices[0]] + brush.Position;
                    Vector3 p1 = brush.Vertices[face.Indices[1]] + brush.Position;
                    Vector3 p2 = brush.Vertices[face.Indices[2]] + brush.Position;
                    Vector2 uv0 = brush.LightmapUVs[face.Indices[0]];
                    Vector2 uv1 = brush.LightmapUVs[face.Indices[1]];
                    Vector2 uv2 = brush.LightmapUVs[face.Indices[2]];

                    polygonScratch.Clear();

                    for (int fi = 0; fi < face.Indices.Length; fi++)
                    {
                        Vector3 vert = brush.Vertices[face.Indices[fi]] + brush.Position;

                        bool duplicate = false;
                        for (int pj = 0; pj < polygonScratch.Count; pj++)
                        {
                            if (vertexComparer.Equals(polygonScratch[pj], vert))
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (!duplicate)
                        {
                            polygonScratch.Add(vert);
                        }
                    }

                    if (polygonScratch.Count == 0) continue;

                    ClipPolygonAgainstPlanes(polygonScratch, clipPlanes, clippedScratch);
                    if (clippedScratch.Count < 3) continue;

                    RemoveCollinear(clippedScratch, clipPlanes, collinearScratch);

                    ProcessVerts(collinearScratch, face.Normal, face.Tangent, face.Binormal, p0, uv0, p1, uv1, p2, uv2);
                }
            }

            if (GlobalMapData.ActiveMap.Terrains != null)
            {
                for (int i = 0; i < GlobalMapData.ActiveMap.Terrains.Length; i++)
                {
                    var terrain = GlobalMapData.ActiveMap.Terrains[i];
                    if (terrain.Bounds.Contains(decalAABB) == ContainmentType.Disjoint)
                    {
                        continue;
                    }

                    for (int t = 0; t < terrain.Triangles.Length; t += 3)
                    {
                        int ia = terrain.Triangles[t + 0], ib = terrain.Triangles[t + 1], ic = terrain.Triangles[t + 2];
                        Vector3 A = terrain.Vertices[ia].Position;
                        Vector3 B = terrain.Vertices[ib].Position;
                        Vector3 C = terrain.Vertices[ic].Position;

                        var triBox = new BoundingBox(Vector3.Min(Vector3.Min(A, B), C), Vector3.Max(Vector3.Max(A, B), C));
                        if (triBox.Contains(decalAABB) == ContainmentType.Disjoint) continue;

                        triangleScratch.Clear();
                        triangleScratch.Add(A); triangleScratch.Add(B); triangleScratch.Add(C);

                        ClipPolygonAgainstPlanes(triangleScratch, clipPlanes, clippedScratch);
                        if (clippedScratch.Count < 3) continue;

                        RemoveCollinear(clippedScratch, clipPlanes, collinearScratch);

                        Vector3 triNormal = Vector3.Normalize(Vector3.Cross(B - A, C - A));

                        Vector4 tanRaw = terrain.Vertices[ia].Tangent.ToVector4();
                        Vector3 triTangent = tanRaw.LengthSquared() > 1e-6f
                            ? Vector3.Normalize(new Vector3(tanRaw.X, tanRaw.Y, tanRaw.Z))
                            : Vector3.Normalize(B - A);
                        Vector3 triBinormal = Vector3.Normalize(Vector3.Cross(triNormal, triTangent)) * (tanRaw.W == 0f ? 1f : MathF.Sign(tanRaw.W));

                        ProcessVerts(collinearScratch, triNormal, triTangent, triBinormal,
                            A, terrain.Vertices[ia].LightmapCoordinate,
                            B, terrain.Vertices[ib].LightmapCoordinate,
                            C, terrain.Vertices[ic].LightmapCoordinate);
                    }
                }
            }

            decal.vertices = vertices.ToArray();
            decal.indices = indicies.ToArray();

            if (modifyExistingIndex == -1)
            {
                return DecalManager.AddAndGetIndex(decal);
            }
            else
            {
                DecalManager.RealtimeDecals[modifyExistingIndex] = decal;
                DecalManager.MarkDirty();
                return modifyExistingIndex;
            }
        }
        static void Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            v = (d11 * d20 - d01 * d21) / denom;
            w = (d00 * d21 - d01 * d20) / denom;
            u = 1f - v - w;
        }
        static void ClipPolygonAgainstPlanes(List<Vector3> polygon, Plane[] clipPlanes, List<Vector3> result)
        {
            result.Clear();

            if (polygon == null || polygon.Count == 0)
            {
                return;
            }

            List<Vector3> input = clipBufferA;
            List<Vector3> output = clipBufferB;

            input.Clear();
            input.AddRange(polygon);

            foreach (var plane in clipPlanes)
            {
                if (input.Count == 0)
                {
                    break;
                }

                output.Clear();

                Vector3 S = input[input.Count - 1];
                bool insideS = plane.DotCoordinate(S) >= 0;

                foreach (var E in input)
                {
                    bool insideE = plane.DotCoordinate(E) >= 0;

                    if (insideE)
                    {
                        if (!insideS)
                        {
                            var I = IntersectEdgeWithPlane(S, E, plane);
                            output.Add(I);
                        }
                        output.Add(E);
                    }
                    else if (insideS)
                    {
                        var I = IntersectEdgeWithPlane(S, E, plane);
                        output.Add(I);
                    }

                    S = E;
                    insideS = insideE;
                }

                var temp = input;
                input = output;
                output = temp;
            }

            result.AddRange(input);
        }

        static void RemoveCollinear(
            List<Vector3> poly,
            Plane[] clipPlanes,
            List<Vector3> result,
            float collinearEps = 1e-5f,
            float planeEps = 1e-4f)
        {
            result.Clear();
            int n = poly.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 prev = poly[(i + n - 1) % n];
                Vector3 cur = poly[i];
                Vector3 next = poly[(i + 1) % n];

                int nearPlanes = 0;
                for (int p = 0; p < clipPlanes.Length; p++)
                {
                    if (MathF.Abs(clipPlanes[p].DotCoordinate(cur)) < planeEps)
                    {
                        nearPlanes++;
                    }
                }

                if (nearPlanes >= 2)
                {
                    result.Add(cur);
                    continue;
                }

                Vector3 v1 = Vector3.Normalize(cur - prev);
                Vector3 v2 = Vector3.Normalize(next - cur);

                float dot = Vector3.Dot(v1, v2);
                if (MathF.Abs(dot - 1f) > collinearEps)
                {
                    result.Add(cur);
                }
            }
        }

        public static Vector3 IntersectEdgeWithPlane(Vector3 A, Vector3 B, Plane plane)
        {
            float t1 = plane.DotCoordinate(A);
            float t2 = plane.DotCoordinate(B);

            if (float.Sign(t1) == float.Sign(t2)) return B;

            float frac = t1 / (t1 - t2);

            Vector3 mid = A + frac * (B - A);

            return mid;
        }
    }
    public static class OBBExtensions
    {
        public static Plane[] GetPlanes(this OrientedBoundingBox obb)
        {
            Plane[] planes = new Plane[6];

            Vector3 center = obb.Center;
            Vector3 extents = obb.Extents;
            Vector3 right = obb.Transformation.Right;
            Vector3 up = obb.Transformation.Up;
            Vector3 forward = obb.Transformation.Forward;

            // LEFT plane: The plane is at the left face (center - right * extents.X)
            // and the normal points right (into the box).
            planes[0] = new Plane(center - right * extents.X, right);

            // RIGHT plane: The plane is at the right face (center + right * extents.X)
            // and the normal points left (into the box).
            planes[1] = new Plane(center + right * extents.X, -right);

            // BOTTOM plane: The plane is at the bottom face (center - up * extents.Y)
            // and the normal points upward (into the box).
            planes[2] = new Plane(center - up * extents.Y, up);

            // TOP plane: The plane is at the top face (center + up * extents.Y)
            // and the normal points downward (into the box).
            planes[3] = new Plane(center + up * extents.Y, -up);

            // BACK plane: The plane is at the back face (center - forward * extents.Z)
            // and the normal points forward (into the box).
            planes[4] = new Plane(center - forward * extents.Z, forward);

            // FRONT plane: The plane is at the front face (center + forward * extents.Z)
            // and the normal points backward (into the box).
            planes[5] = new Plane(center + forward * extents.Z, -forward);

            return planes;
        }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DecalVertex : IVertexType
    {
        public Vector3 Position;
        public Vector4 Color;
        public Vector3 Normal;
        public Vector2 TextureCoordinate;
        public Vector2 LightmapCoordinate;
        public Vector3 Tangent;
        public Vector3 Binormal;

        public static readonly VertexDeclaration VertexDeclaration;

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

        public DecalVertex(Vector3 position, Vector4 color, Vector3 normal, Vector2 textureCoordinate)
        {
            Position = position;
            Color = color;
            Normal = normal;
            TextureCoordinate = textureCoordinate;
        }

        public override int GetHashCode()
        {
            return (((((Position.GetHashCode() * 397) ^ Color.GetHashCode()) * 397) ^ Normal.GetHashCode()) * 397) ^ TextureCoordinate.GetHashCode();
        }

        public override string ToString()
        {
            string[] obj = new string[9] { "{{Position:", null, null, null, null, null, null, null, null };
            Vector3 position = Position;
            obj[1] = position.ToString();
            obj[2] = " Color:";
            Vector4 color = Color;
            obj[3] = color.ToString();
            obj[4] = " Normal:";
            position = Normal;
            obj[5] = position.ToString();
            obj[6] = " TextureCoordinate:";
            Vector2 textureCoordinate = TextureCoordinate;
            obj[7] = textureCoordinate.ToString();
            obj[8] = "}}";
            return string.Concat(obj);
        }

        public static bool operator ==(DecalVertex left, DecalVertex right)
        {
            if (left.Position == right.Position && left.Color == right.Color && left.Normal == right.Normal)
            {
                return left.TextureCoordinate == right.TextureCoordinate;
            }

            return false;
        }

        public static bool operator !=(DecalVertex left, DecalVertex right)
        {
            return !(left == right);
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return this == (DecalVertex)obj;
        }

        static DecalVertex()
        {
            VertexDeclaration = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Vector4, VertexElementUsage.Color, 0),
                new VertexElement(28, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(40, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(48, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(56, VertexElementFormat.Vector3, VertexElementUsage.Tangent, 0),
                new VertexElement(68, VertexElementFormat.Vector3, VertexElementUsage.Binormal, 0));
        }
    }
}

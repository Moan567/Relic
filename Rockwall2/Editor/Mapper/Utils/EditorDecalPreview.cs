using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rockwall2.Editor.Mapper.Utils;

public static class EditorDecalPreview
{
    class Cached
    {
        public string dirtyKey;
        public VertexBuffer vertexBuffer;
        public IndexBuffer indexBuffer;
        public int primitiveCount;
    }

    static readonly Dictionary<Guid, Cached> cache = new();
    static readonly List<Vector3> clipBufferA = new(32);
    static readonly List<Vector3> clipBufferB = new(32);
    static readonly List<Vector3> polygonScratch = new(32);
    static readonly List<Vector3> clippedScratch = new(32);

    static Vector3 ReadPosition(EntityReference ent, string name, Vector3 fallback)
    {
        var raw = ent.Properties?.FirstOrDefault(p => p.Name == name).Value;
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }
        var parts = raw.Split(',');
        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    static float ReadFloat(EntityReference ent, string name, float fallback)
    {
        var raw = ent.Properties?.FirstOrDefault(p => p.Name == name).Value;
        return string.IsNullOrEmpty(raw) ? fallback : float.Parse(raw, CultureInfo.InvariantCulture);
    }

    static string ReadString(EntityReference ent, string name) => ent.Properties?.FirstOrDefault(p => p.Name == name).Value;

    public static void DrawForEntity(GraphicsDevice gd, BasicEffect basicEffect, EntityReference ent)
    {
        string materialName = ReadString(ent, "Decal Material");
        if (string.IsNullOrEmpty(materialName))
        {
            return;
        }
        if (!GlobalMapData.MaterialNameToIndex.TryGetValue(materialName, out int materialIndex))
        {
            return;
        }

        Texture2D texture = GlobalMapData.LoadedMaterials[materialIndex].Texture;
        if (texture == null)
        {
            return;
        }

        Vector3 min = ReadPosition(ent, "Decal Min Bounds", -Vector3.One);
        Vector3 max = ReadPosition(ent, "Decal Max Bounds", Vector3.One);
        Vector2 uvScale = new(ReadFloat(ent, "Decal UV Scale X", 1f), ReadFloat(ent, "Decal UV Scale Y", 1f));
        Vector2 uvOffset = new(ReadFloat(ent, "Decal UV Offset X", 0f), ReadFloat(ent, "Decal UV Offset Y", 0f));

        Guid guid = ent.GroupingID ?? Guid.Empty;
        string dirtyKey = $"{ent.Position}|{ent.SpawnRotation}|{min}|{max}|{materialName}|{uvScale}|{uvOffset}";

        if (!cache.TryGetValue(guid, out var cached) || cached.dirtyKey != dirtyKey)
        {
            cached = Rebuild(gd, ent, min, max, uvScale, uvOffset);
            cached.dirtyKey = dirtyKey;
            cache[guid] = cached;
        }

        if (cached.primitiveCount == 0)
        {
            return;
        }

        var oldBlend = gd.BlendState;
        gd.BlendState = BlendState.NonPremultiplied;
        gd.DepthStencilState = DepthStencilState.DepthRead;

        basicEffect.World = Matrix.Identity;
        basicEffect.TextureEnabled = true;
        basicEffect.VertexColorEnabled = false;
        basicEffect.Texture = texture;
        basicEffect.DiffuseColor = Vector3.One;
        basicEffect.Alpha = 1f;

        gd.SetVertexBuffer(cached.vertexBuffer);
        gd.Indices = cached.indexBuffer;
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, cached.primitiveCount);
        }
        gd.SetVertexBuffer(null);

        basicEffect.TextureEnabled = false;
        gd.DepthStencilState = DepthStencilState.Default;
        gd.BlendState = oldBlend;
    }

    static Cached Rebuild(GraphicsDevice gd, EntityReference ent, Vector3 min, Vector3 max, Vector2 uvScale, Vector2 uvOffset)
    {
        Matrix rot = Matrix.CreateFromYawPitchRoll(
            MathHelper.ToRadians(ent.SpawnRotation.X),
            MathHelper.ToRadians(ent.SpawnRotation.Y),
            MathHelper.ToRadians(ent.SpawnRotation.Z));

        Vector3 extents = (max - min) * 0.5f;
        Vector3 worldCenter = ent.Position + Vector3.Transform(min + extents, rot);
        Matrix boxWorld = rot * Matrix.CreateTranslation(worldCenter);
        Matrix boxInverse = Matrix.Invert(boxWorld);

        Vector3 right = boxWorld.Right, up = boxWorld.Up, forward = boxWorld.Forward;
        Plane[] clipPlanes =
        {
            new Plane(worldCenter - right * extents.X, right),
            new Plane(worldCenter + right * extents.X, -right),
            new Plane(worldCenter - up * extents.Y, up),
            new Plane(worldCenter + up * extents.Y, -up),
            new Plane(worldCenter - forward * extents.Z, forward),
            new Plane(worldCenter + forward * extents.Z, -forward),
        };

        Vector3 absR = new(MathF.Abs(right.X), MathF.Abs(right.Y), MathF.Abs(right.Z));
        Vector3 absU = new(MathF.Abs(up.X), MathF.Abs(up.Y), MathF.Abs(up.Z));
        Vector3 absF = new(MathF.Abs(forward.X), MathF.Abs(forward.Y), MathF.Abs(forward.Z));
        Vector3 halfExtentWorld = absR * extents.X + absU * extents.Y + absF * extents.Z;
        var worldAABB = new BoundingBox(worldCenter - halfExtentWorld, worldCenter + halfExtentWorld);

        var verts = new List<VertexPositionTexture>();
        var indices = new List<ushort>();

        void ProcessVerts(List<Vector3> poly)
        {
            if (poly.Count < 3)
            {
                return;
            }

            int start = verts.Count;
            foreach (var worldPos in poly)
            {
                Vector3 localPos = Vector3.Transform(worldPos, boxInverse);
                Vector2 uv = new Vector2(
                    (localPos.X / (extents.X * 2)) + 0.5f,
                    (localPos.Y / (extents.Y * 2)) + 0.5f) * uvScale + uvOffset;

                verts.Add(new VertexPositionTexture(worldPos, uv));
            }

            for (int t = 1; t < poly.Count - 1; t++)
            {
                indices.Add((ushort)start);
                indices.Add((ushort)(start + t));
                indices.Add((ushort)(start + t + 1));
            }
        }

        for (int i = 0; i < MapTools.Brushes.Length; i++)
        {
            if (worldAABB.Contains(MapTools.BrushBounds[i]) == ContainmentType.Disjoint)
            {
                continue;
            }

            var brush = MapTools.Brushes[i];
            if (brush.IsClip || (brush.IsEntity && !brush.IsDetail) || brush.IsLightNodeVolume)
            {
                continue;
            }

            for (int f = 0; f < brush.Faces.Length; f++)
            {
                var face = brush.Faces[f];
                if (Vector3.Dot(face.Normal, forward) < 0)
                {
                    continue;
                }

                polygonScratch.Clear();
                foreach (var idx in face.Indices)
                {
                    var vert = brush.Vertices[idx] + brush.Position;
                    if (!polygonScratch.Any(p => Vector3.DistanceSquared(p, vert) < 1e-8f))
                    {
                        polygonScratch.Add(vert + face.Normal*0.01f);
                    }
                }
                if (polygonScratch.Count < 3)
                {
                    continue;
                }

                ClipPolygonAgainstPlanes(polygonScratch, clipPlanes, clippedScratch);
                if (clippedScratch.Count < 3)
                {
                    continue;
                }

                ProcessVerts(new List<Vector3>(clippedScratch));
            }
        }

        if (MapTools.Terrains != null)
        {
            for (int i = 0; i < MapTools.Terrains.Length; i++)
            {
                var terrain = MapTools.Terrains[i];
                if (terrain.Bounds.Contains(worldAABB) == ContainmentType.Disjoint)
                {
                    continue;
                }

                for (int t = 0; t < terrain.Triangles.Length; t += 3)
                {
                    polygonScratch.Clear();
                    polygonScratch.Add(terrain.Vertices[terrain.Triangles[t]].Position + terrain.Vertices[terrain.Triangles[t]].Normal*0.01f);
                    polygonScratch.Add(terrain.Vertices[terrain.Triangles[t + 1]].Position + terrain.Vertices[terrain.Triangles[t + 1]].Normal * 0.01f);
                    polygonScratch.Add(terrain.Vertices[terrain.Triangles[t + 2]].Position + terrain.Vertices[terrain.Triangles[t + 2]].Normal * 0.01f);

                    ClipPolygonAgainstPlanes(polygonScratch, clipPlanes, clippedScratch);
                    if (clippedScratch.Count < 3)
                    {
                        continue;
                    }

                    ProcessVerts(new List<Vector3>(clippedScratch));
                }
            }
        }

        var cached = new Cached();
        if (verts.Count > 0)
        {
            cached.vertexBuffer = new VertexBuffer(gd, typeof(VertexPositionTexture), verts.Count, BufferUsage.WriteOnly);
            cached.vertexBuffer.SetData(verts.ToArray());
            cached.indexBuffer = new IndexBuffer(gd, IndexElementSize.SixteenBits, indices.Count, BufferUsage.WriteOnly);
            cached.indexBuffer.SetData(indices.Reverse<ushort>().ToArray());
            cached.primitiveCount = indices.Count / 3;
        }
        return cached;
    }

    static void ClipPolygonAgainstPlanes(List<Vector3> polygon, Plane[] clipPlanes, List<Vector3> result)
    {
        result.Clear();
        if (polygon.Count == 0)
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
                        output.Add(IntersectEdgeWithPlane(S, E, plane));
                    }
                    output.Add(E);
                }
                else if (insideS)
                {
                    output.Add(IntersectEdgeWithPlane(S, E, plane));
                }
                S = E;
                insideS = insideE;
            }

            (input, output) = (output, input);
        }

        result.AddRange(input);
    }

    static Vector3 IntersectEdgeWithPlane(Vector3 A, Vector3 B, Plane plane)
    {
        float t1 = plane.DotCoordinate(A);
        float t2 = plane.DotCoordinate(B);
        if (float.Sign(t1) == float.Sign(t2))
        {
            return B;
        }
        float frac = t1 / (t1 - t2);
        return A + frac * (B - A);
    }
}
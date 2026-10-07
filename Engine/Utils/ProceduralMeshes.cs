using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils;
public static class ProceduralMeshes
{
    public struct ProceduralModelVertexData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Tangent;
        public Vector3 Binormal;
        public Vector2 TextureCoordinate;

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector3, VertexElementUsage.Tangent, 0),
            new VertexElement(36, VertexElementFormat.Vector3, VertexElementUsage.Binormal, 0),
            new VertexElement(48, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
        );
    }
    public static void CreateSphere(GraphicsDevice device, float radius, int segments, int rings,
        out VertexBuffer vertexBuffer, out IndexBuffer indexBuffer, out int indexCount)
    {
        List<ProceduralModelVertexData> vertices = new List<ProceduralModelVertexData>();
        List<int> indices = new List<int>();

        // Generate vertices
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathHelper.Pi * ring / rings;
            float sinPhi = (float)Math.Sin(phi);
            float cosPhi = (float)Math.Cos(phi);

            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = MathHelper.TwoPi * seg / segments;
                float sinTheta = (float)Math.Sin(theta);
                float cosTheta = (float)Math.Cos(theta);

                // Position (spherical to cartesian)
                Vector3 normal = new Vector3(
                    sinPhi * cosTheta,
                    cosPhi,
                    sinPhi * sinTheta
                );
                Vector3 position = normal * radius;

                // Tangent (derivative with respect to theta)
                Vector3 tangent = new Vector3(
                    -sinTheta,
                    0,
                    cosTheta
                );
                tangent.Normalize();

                // Binormal (derivative with respect to phi, or cross product)
                Vector3 binormal = Vector3.Cross(normal, tangent);
                binormal.Normalize();

                // Texture coordinates
                Vector2 texCoord = new Vector2(
                    (float)seg / segments,
                    (float)ring / rings
                );

                vertices.Add(new ProceduralModelVertexData
                {
                    Position = position,
                    Normal = normal,
                    Tangent = tangent,
                    Binormal = binormal,
                    TextureCoordinate = texCoord
                });
            }
        }

        // Generate indices
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int current = ring * (segments + 1) + seg;
                int next = current + segments + 1;

                // First triangle
                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);

                // Second triangle
                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        // Create buffers
        vertexBuffer = new VertexBuffer(device, ProceduralModelVertexData.VertexDeclaration,
            vertices.Count, BufferUsage.WriteOnly);
        vertexBuffer.SetData(vertices.ToArray());

        indexBuffer = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits,
            indices.Count, BufferUsage.WriteOnly);
        indexBuffer.SetData(indices.ToArray());

        indexCount = indices.Count;
    }
}

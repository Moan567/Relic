using System;
using OpenTK.Mathematics;

namespace Microsoft.Xna.Framework
{
    // Minimal shim types to ease migration from Microsoft.Xna.Framework to OpenTK.
    // Expand as needed.

    public struct Vector3
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vector3 Zero => new Vector3(0f,0f,0f);
        public static implicit operator OpenTK.Mathematics.Vector3(Vector3 v) => new OpenTK.Mathematics.Vector3(v.X, v.Y, v.Z);
        public static implicit operator Vector3(OpenTK.Mathematics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }

    public struct Matrix
    {
        private Matrix4 _m;
        public static Matrix Identity => new Matrix { _m = Matrix4.Identity };
        public Vector3 Translation
        {
            get => new Vector3(_m.M41, _m.M42, _m.M43);
            set { _m.M41 = value.X; _m.M42 = value.Y; _m.M43 = value.Z; }
        }
        public static implicit operator Matrix4(Matrix m) => m._m;
        public static implicit operator Matrix(Matrix4 m) => new Matrix { _m = m };
    }

    // Keep only math types here (Vector3, Matrix). Graphics and content types live in sub-namespaces below.
}

namespace Microsoft.Xna.Framework.Content
{
    public class ContentManager
    {
        public string RootDirectory { get; set; } = string.Empty;
        public T Load<T>(string path) => default!;
    }
}

namespace Microsoft.Xna.Framework.Graphics
{
    public enum BufferUsage { WriteOnly }
    public enum PrimitiveType { TriangleStrip }

    public struct VertexPosition
    {
        public Microsoft.Xna.Framework.Vector3 Position;
        public VertexPosition(Microsoft.Xna.Framework.Vector3 p) { Position = p; }
    }

    public class Texture2D { }
    public class TextureCube { }

    public class GraphicsDevice
    {
        public void SetVertexBuffer(VertexBuffer? vb) { }
        public void DrawPrimitives(PrimitiveType type, int start, int count) { }
    }

    public class VertexBuffer
    {
        private Array? _data;
        public VertexBuffer(GraphicsDevice dev, Type type, int count, BufferUsage usage) { }
        public void SetData<T>(T[] data) { _data = data; }
    }
}

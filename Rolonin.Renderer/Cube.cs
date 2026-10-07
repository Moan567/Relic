namespace Rolonin.Renderer
{
    internal static class Cube
    {
        // 36 vertices (12 triangles) * 3 components
        public static readonly float[] Vertices = new float[] {
            // front
            -1f, -1f,  1f,  1f, -1f,  1f,  1f,  1f,  1f,
            -1f, -1f,  1f,  1f,  1f,  1f, -1f,  1f,  1f,
            // back
            -1f, -1f, -1f, -1f,  1f, -1f,  1f,  1f, -1f,
            -1f, -1f, -1f,  1f,  1f, -1f,  1f, -1f, -1f,
            // left
            -1f, -1f, -1f, -1f, -1f,  1f, -1f,  1f,  1f,
            -1f, -1f, -1f, -1f,  1f,  1f, -1f,  1f, -1f,
            // right
             1f, -1f, -1f,  1f,  1f, -1f,  1f,  1f,  1f,
             1f, -1f, -1f,  1f,  1f,  1f,  1f, -1f,  1f,
            // top
            -1f,  1f, -1f, -1f,  1f,  1f,  1f,  1f,  1f,
            -1f,  1f, -1f,  1f,  1f,  1f,  1f,  1f, -1f,
            // bottom
            -1f, -1f, -1f,  1f, -1f, -1f,  1f, -1f,  1f,
            -1f, -1f, -1f,  1f, -1f,  1f, -1f, -1f,  1f,
        };

        public const int VertexCount = 36;
    }
}

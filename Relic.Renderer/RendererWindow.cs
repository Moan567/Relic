using System;
using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Rolonin.Renderer
{
    public class RendererWindow : GameWindow
    {
        private int _vao;
        private int _vbo;
        private Shader? _shader;

        public RendererWindow(GameWindowSettings gws, NativeWindowSettings nws)
            : base(gws, nws)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            GL.Enable(EnableCap.DepthTest);
            GL.ClearColor(0.1f, 0.1f, 0.12f, 1.0f);

            _shader = new Shader(VertexShaderSource, FragmentShaderSource);

            float[] vertices = Cube.Vertices;

            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            int posLocation = _shader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(posLocation);
            GL.VertexAttribPointer(posLocation, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            GL.BindVertexArray(0);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _shader?.Use();

            var proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), Size.X / (float)Size.Y, 0.1f, 100f);
            var view = Matrix4.LookAt(new Vector3(0,0,3), Vector3.Zero, Vector3.UnitY);
            _shader?.SetMatrix4("uProjection", proj);
            _shader?.SetMatrix4("uView", view);
            _shader?.SetMatrix4("uModel", Matrix4.Identity);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, Cube.VertexCount);
            GL.BindVertexArray(0);

            SwapBuffers();
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            if (_shader != null) _shader.Dispose();
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
        }

        private const string VertexShaderSource = @"#version 330 core
layout(location = 0) in vec3 aPosition;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
void main() { gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0); }";

        private const string FragmentShaderSource = @"#version 330 core
out vec4 FragColor;
void main() { FragColor = vec4(0.6, 0.7, 0.9, 1.0); }";
    }
}

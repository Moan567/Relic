using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Rolonin.Renderer
{
    public class Shader : IDisposable
    {
        private readonly int _handle;

        public Shader(string vertSource, string fragSource)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertSource);
            GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out int vsStatus);
            if (vsStatus == 0) throw new Exception(GL.GetShaderInfoLog(vs));

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragSource);
            GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out int fsStatus);
            if (fsStatus == 0) throw new Exception(GL.GetShaderInfoLog(fs));

            _handle = GL.CreateProgram();
            GL.AttachShader(_handle, vs);
            GL.AttachShader(_handle, fs);
            GL.LinkProgram(_handle);
            GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out int linkStatus);
            if (linkStatus == 0) throw new Exception(GL.GetProgramInfoLog(_handle));

            GL.DetachShader(_handle, vs);
            GL.DetachShader(_handle, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        public void Use() => GL.UseProgram(_handle);
        public int GetAttribLocation(string name) => GL.GetAttribLocation(_handle, name);
        public void SetMatrix4(string name, Matrix4 mat)
        {
            int loc = GL.GetUniformLocation(_handle, name);
            GL.UniformMatrix4(loc, false, ref mat);
        }

        public void Dispose()
        {
            GL.DeleteProgram(_handle);
        }
    }
}

using Microsoft.Xna.Framework;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU;
public sealed class GraphicsProgram : IDisposable
{
    private readonly GL gl;
    public uint Handle { get; private set; }

    public GraphicsProgram(GL gl, string vertexShaderName, string fragmentShaderName, string geometryShaderName = null, params string[] includeNames)
    {
        this.gl = gl;

        uint vertexShader = CompileStage(gl, GLEnum.VertexShader, vertexShaderName, includeNames);
        uint fragmentShader = CompileStage(gl, GLEnum.FragmentShader, fragmentShaderName, includeNames);
        uint geometryShader = geometryShaderName != null ? CompileStage(gl, GLEnum.GeometryShader, geometryShaderName, includeNames) : 0;

        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vertexShader);
        gl.AttachShader(Handle, fragmentShader);
        if (geometryShader != 0) gl.AttachShader(Handle, geometryShader);
        gl.LinkProgram(Handle);

        gl.GetProgram(Handle, GLEnum.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = gl.GetProgramInfoLog(Handle);
            throw new InvalidOperationException($"Graphics program '{vertexShaderName}'/'{fragmentShaderName}' link failed: {log}");
        }

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        if (geometryShader != 0) gl.DeleteShader(geometryShader);
    }

    private static uint CompileStage(GL gl, GLEnum stage, string shaderName, string[] includeNames)
    {
        string source = ShaderLoader.LoadWithIncludes(shaderName, includeNames);

        uint shader = gl.CreateShader(stage);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, GLEnum.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"Shader '{shaderName}' compile failed: {log}");
        }

        return shader;
    }

    public void Use()
    {
        gl.UseProgram(Handle);
    }

    public void SetUniform(string name, int value) => gl.Uniform1(gl.GetUniformLocation(Handle, name), value);
    public void SetUniform(string name, float value) => gl.Uniform1(gl.GetUniformLocation(Handle, name), value);
    public void SetUniform(string name, Vector3 value) => gl.Uniform3(gl.GetUniformLocation(Handle, name), value.X, value.Y, value.Z);
    public void SetUniformInt2(string name, int x, int y) => gl.Uniform2(gl.GetUniformLocation(Handle, name), x, y);

    public void Dispose()
    {
        if (Handle != 0)
        {
            gl.DeleteProgram(Handle);
            Handle = 0;
        }
    }
}
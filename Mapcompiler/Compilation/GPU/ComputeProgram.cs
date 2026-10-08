using Microsoft.Xna.Framework;
using Silk.NET.OpenGL;
using System;
using System.Diagnostics;

namespace MapCompiler.Compilation.GPU;
public sealed class ComputeProgram : IDisposable
{
    private readonly GL gl;
    private readonly string shaderName;
    public uint Handle { get; private set; }

    public ComputeProgram(GL gl, string shaderName, params string[] includeNames)
    {
        this.gl = gl;
        this.shaderName = shaderName;
        string source = ShaderLoader.LoadWithIncludes(shaderName, includeNames);

        uint shader = gl.CreateShader(GLEnum.ComputeShader);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, GLEnum.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"Compute shader '{shaderName}' compile failed: {log}");
        }

        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, shader);
        gl.LinkProgram(Handle);

        gl.GetProgram(Handle, GLEnum.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = gl.GetProgramInfoLog(Handle);
            throw new InvalidOperationException($"Compute program '{shaderName}' link failed: {log}");
        }

        gl.DeleteShader(shader);
    }

    public void Use()
    {
        gl.UseProgram(Handle);
    }

    public void SetUniform(string name, int value)
    {
        gl.Uniform1(gl.GetUniformLocation(Handle, name), value);
    }
    public void SetUniform(string name, float value)
    {
        gl.Uniform1(gl.GetUniformLocation(Handle, name), value);
    }
    public void SetUniform(string name, Vector3 value)
    {
        gl.Uniform3(gl.GetUniformLocation(Handle, name), value.X, value.Y, value.Z);
    }
    public void SetUniformInt3(string name, int x, int y, int z)
    {
        gl.Uniform3(gl.GetUniformLocation(Handle, name), x, y, z);
    }
    public void Dispatch(uint groupsX, uint groupsY = 1, uint groupsZ = 1)
    {
        gl.DispatchCompute(groupsX, groupsY, groupsZ);
    }

    public void DispatchRowsChunked(GL gl, int resolution, string rowOffsetUniform, uint localSizeX, uint localSizeY, MemoryBarrierMask barrier, int rowsPerChunk = 64, string label = null)
    {
        uint groupsX = (uint)((resolution + localSizeX - 1) / localSizeX);

        ChunkedDispatch.Rows(gl, resolution, rowsPerChunk, label ?? shaderName, (rowStart, rows) =>
        {
            uint groupsY = (uint)((rows + localSizeY - 1) / localSizeY);
            SetUniform(rowOffsetUniform, rowStart);
            Dispatch(groupsX, groupsY);
        }, barrier);
    }

    public void DispatchUnitsChunked(GL gl, int totalUnits, string offsetUniform, uint localSizeX, MemoryBarrierMask barrier, int chunkSize = 65536, string label = null)
    {
        ChunkedDispatch.Units(gl, totalUnits, chunkSize, label ?? shaderName, (start, count) =>
        {
            uint groups = (uint)((count + localSizeX - 1) / localSizeX);
            SetUniform(offsetUniform, start);
            Dispatch(groups);
        }, barrier);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            gl.DeleteProgram(Handle);
            Handle = 0;
        }
    }
}
public static class ChunkedDispatch
{
    public static void Rows(GL gl, int resolution, int rowsPerChunk, string label, Action<int, int> dispatchChunk, MemoryBarrierMask barrier)
    {
        nint pendingFence = 0;
        int totalChunks = Math.Max(1, (resolution + rowsPerChunk - 1) / rowsPerChunk);
        int chunkIndex = 0;

        for (int rowStart = 0; rowStart < resolution; rowStart += rowsPerChunk)
        {
            int rows = Math.Min(rowsPerChunk, resolution - rowStart);
            float progress = (float)chunkIndex / totalChunks;

            if (pendingFence != 0)
            {
                FenceWaiter.Wait(gl, pendingFence, label, progress);
                gl.DeleteSync(pendingFence);
            }

            dispatchChunk(rowStart, rows);
            gl.MemoryBarrier(barrier);

            pendingFence = gl.FenceSync(GLEnum.SyncGpuCommandsComplete, (uint)0);
            SpinnerConsole.Report(label, progress);
            chunkIndex++;
        }

        if (pendingFence != 0)
        {
            FenceWaiter.Wait(gl, pendingFence, label, 1f);
            gl.DeleteSync(pendingFence);
        }

        SpinnerConsole.Finish();
    }

    public static void Units(GL gl, int totalUnits, int chunkSize, string label, Action<int, int> dispatchChunk, MemoryBarrierMask barrier)
    {
        nint pendingFence = 0;
        int totalChunks = Math.Max(1, (totalUnits + chunkSize - 1) / chunkSize);
        int chunkIndex = 0;

        for (int start = 0; start < totalUnits; start += chunkSize)
        {
            int count = Math.Min(chunkSize, totalUnits - start);
            float progress = (float)chunkIndex / totalChunks;

            if (pendingFence != 0)
            {
                FenceWaiter.Wait(gl, pendingFence, label, progress);
                gl.DeleteSync(pendingFence);
            }

            dispatchChunk(start, count);
            gl.MemoryBarrier(barrier);

            pendingFence = gl.FenceSync(GLEnum.SyncGpuCommandsComplete, (uint)0);
            SpinnerConsole.Report(label, progress);
            chunkIndex++;
        }

        if (pendingFence != 0)
        {
            FenceWaiter.Wait(gl, pendingFence, label, 1f);
            gl.DeleteSync(pendingFence);
        }

        SpinnerConsole.Finish();
    }
}
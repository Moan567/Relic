using Microsoft.Xna.Framework;
using Silk.NET.OpenGL;
using System;
using System.Runtime.InteropServices;

namespace MapCompiler.Compilation.GPU.Resources;
[StructLayout(LayoutKind.Sequential)]
public struct GpuPatch
{
    public Vector3 Center; public int ExcludeBrush;
    public Vector3 C0; public int IsTerrain;
    public Vector3 C1; public float Pad1;
    public Vector3 C2; public float Pad2;
    public Vector3 C3; public float Pad3;
    public Vector3 Normal; public float Pad4;
    public Vector2 StartUV;
    public Vector2 EndUV;
    public Vector3 Albedo; public float Pad5;
}

public sealed class PatchResources : IDisposable
{
    private readonly GpuBuffer patches;
    public int Count { get; }

    public PatchResources(GL gl, Patch[] sourcePatches)
    {
        Count = sourcePatches.Length;
        var gpuPatches = new GpuPatch[sourcePatches.Length];
        for (int i = 0; i < sourcePatches.Length; i++)
        {
            gpuPatches[i] = new GpuPatch
            {
                Center = sourcePatches[i].center,
                ExcludeBrush = sourcePatches[i].isTerrain ? -1 : sourcePatches[i].id1,
                C0 = sourcePatches[i].c0,
                IsTerrain = sourcePatches[i].isTerrain ? 1 : 0,
                C1 = sourcePatches[i].c1,
                C2 = sourcePatches[i].c2,
                C3 = sourcePatches[i].c3,
                Normal = sourcePatches[i].normal,
                StartUV = sourcePatches[i].startUV,
                EndUV = sourcePatches[i].endUV,
                Albedo = new Vector3(sourcePatches[i].texr, sourcePatches[i].texg, sourcePatches[i].texb)
            };
        }

        patches = new GpuBuffer(gl);
        patches.Upload<GpuPatch>(gpuPatches);
    }

    public void Bind()
    {
        patches.BindBase(GpuBindings.Patches);
    }

    public void Dispose()
    {
        patches.Dispose();
    }
}

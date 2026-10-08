using Microsoft.Xna.Framework;
using Rockwall;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation.GPU.Resources;
[StructLayout(LayoutKind.Sequential)]
public struct GpuLight
{
    public Vector3 Position; public int Type;
    public Vector3 Rotation; public float Intensity;
    public Vector3 Color; public float Range;
    public float Angle;
    public float InnerAngle;
    public int Id;
    public float Padding;
}

public sealed class LightResources : IDisposable
{
    private readonly GpuBuffer lights;
    public int Count { get; }

    public LightResources(GL gl, List<Light> sourceLights)
    {
        Count = sourceLights.Count;
        var gpuLights = new GpuLight[sourceLights.Count];
        for (int i = 0; i < sourceLights.Count; i++)
        {
            var l = sourceLights[i];
            gpuLights[i] = new GpuLight
            {
                Position = l.Position,
                Type = l.Type switch
                {
                    Light.LightType.Point => 0,
                    Light.LightType.Directional => 1,
                    Light.LightType.SpotLight => 2,
                    _ => 0
                },
                Rotation = l.Rotation,
                Intensity = l.Intensity,
                Color = l.Color.ToVector3(),
                Range = l.Range,
                Angle = l.Angle,
                InnerAngle = l.InnerAngle,
                Id = l.ID
            };
        }

        lights = new GpuBuffer(gl);
        lights.Upload<GpuLight>(gpuLights);
    }

    public void Bind()
    {
        lights.BindBase(GpuBindings.Lights);
    }

    public void Dispose()
    {
        lights.Dispose();
    }
}
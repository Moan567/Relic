#version 430
layout(local_size_x = 64) in;

layout(std430, binding = 21) readonly buffer LayerB1Buffer { vec4 lmB1[]; };
layout(std430, binding = 22) readonly buffer LayerB2Buffer { vec4 lmB2[]; };
layout(std430, binding = 23) readonly buffer LayerB3Buffer { vec4 lmB3[]; };

struct GpuPatch
{
    vec3 center; int excludeBrush;
    vec3 c0; int isTerrain;
    vec3 c1; float pad1;
    vec3 c2; float pad2;
    vec3 c3; float pad3;
    vec3 normal; float pad4;
    vec2 startUV;
    vec2 endUV;
    vec3 albedo; float pad5;
};

layout(std430, binding = 10) readonly buffer PatchesBuffer { GpuPatch patches[]; };
layout(std430, binding = 16) buffer PatchSeedResultBuffer { vec4 seedResult[]; };

uniform int patchCount;
uniform int lightmapResolution;
uniform int patchOffset;

void main()
{
    uint idx = uint(patchOffset) + gl_GlobalInvocationID.x;
    if (idx >= uint(patchCount))
    {
        return;
    }

    GpuPatch p = patches[idx];

    int xStart = int(floor(p.startUV.x * float(lightmapResolution)));
    int xEnd = int(ceil(p.endUV.x * float(lightmapResolution)));
    int yStart = int(floor(p.startUV.y * float(lightmapResolution)));
    int yEnd = int(ceil(p.endUV.y * float(lightmapResolution)));

    vec3 accum = vec3(0.0);
    int samples = 0;

    for (int x = xStart; x < xEnd; x++)
    {
        for (int y = yStart; y < yEnd; y++)
        {
            if (x < 0 || x >= lightmapResolution || y < 0 || y >= lightmapResolution)
            {
                continue;
            }
            
            int idx = y * lightmapResolution + x;
            vec3 c1 = lmB1[idx].rgb;
            vec3 c2 = lmB2[idx].rgb;
            vec3 c3 = lmB3[idx].rgb;

            accum += (c1 + c2 + c3) / 3.0;
            samples++;
        }
    }

    seedResult[idx] = samples > 0 ? vec4(accum / float(samples), 0.0) : vec4(0.0);
}
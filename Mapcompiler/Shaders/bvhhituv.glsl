layout(std430, binding = 24) readonly buffer TriUv0Buffer { vec2 triUv0[]; };
layout(std430, binding = 25) readonly buffer TriUv1Buffer { vec2 triUv1[]; };
layout(std430, binding = 26) readonly buffer TriUv2Buffer { vec2 triUv2[]; };

vec2 GetTriHitUV(uint triIdx, float u, float v)
{
    return (1.0 - u - v) * triUv0[triIdx] + u * triUv1[triIdx] + v * triUv2[triIdx];
}
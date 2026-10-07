#include <PosFixup.glsl>
#include <ModelCommon.glsl>

#define MAXBONES 200
uniform mat4 Bones[200];

attribute vec4 in_Position0;
attribute vec3 in_Normal0;
attribute vec3 in_Tangent0;
attribute vec3 in_Binormal0;
attribute vec2 in_TextureCoordinate0;
attribute vec4 in_BlendIndices0; // 4 bone indices, packed as floats
attribute vec4 in_BlendWeight0;
attribute float in_BlendIndices1; // which eye (0 or 1), used as a lerp factor below

varying vec4 WorldPos;
varying float EyeIndex;

void main()
{
    mat4 skinning = mat4(0.0);
    for (int i = 0; i < 4; i++)
    {
        int boneIndex = int(in_BlendIndices0[i]);
        skinning += Bones[boneIndex] * in_BlendWeight0[i];
    }

    vec3 skinnedPosition = (vec4(in_Position0.xyz, 1.0) * skinning).xyz;

    vec4 worldPosition = vec4(skinnedPosition, 1.0) * World;
    vec4 viewPosition = worldPosition * View;
    gl_Position = viewPosition * Projection;

    EyeIndex = in_BlendIndices1;
    WorldPos = worldPosition;
    WorldPos.w = gl_Position.w;

    ApplyPosFixup();
}

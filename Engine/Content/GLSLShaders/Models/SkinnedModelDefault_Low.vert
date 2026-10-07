#include <ModelCommon.glsl>
#include <PosFixup.glsl>

#define MAXBONES 200
uniform mat4 Bones[200];

attribute vec4 in_Position0;
attribute vec3 in_Normal0;
attribute vec3 in_Tangent0;
attribute vec3 in_Binormal0;
attribute vec2 in_TextureCoordinate0;
attribute vec4 in_BlendIndices0;
attribute vec4 in_BlendWeight0;

varying vec4 ScreenPosition;
varying vec4 WorldPos;
varying vec4 Color;
varying vec3 Normal;
varying vec3 Tangent;
varying vec3 Binormal;
varying vec2 TextureCoordinate;

void main()
{
    mat4 skinning = mat4(0.0);
    for (int i = 0; i < 4; i++)
    {
        int boneIndex = int(in_BlendIndices0[i]);
        skinning += Bones[boneIndex] * in_BlendWeight0[i];
    }

    vec3 skinnedPosition = (vec4(in_Position0.xyz, 1.0) * skinning).xyz;
    vec3 skinnedNormal = (vec4(in_Normal0, 0.0) * skinning).xyz;

    vec4 worldPosition = vec4(skinnedPosition, 1.0) * World;
    vec4 viewPosition = worldPosition * View;
    gl_Position = viewPosition * Projection;
    ScreenPosition = gl_Position;

    Normal = normalize((vec4(skinnedNormal, 1.0) * WorldInverseTranspose).xyz);
    Tangent = normalize((vec4(in_Tangent0, 1.0) * WorldInverseTranspose).xyz);
    Binormal = normalize((vec4(in_Binormal0, 1.0) * WorldInverseTranspose).xyz);

    vec3 addLightColor = CalculateStaticLighting(worldPosition.xyz, Normal);
    Color = vec4(addLightColor, 1.0);

    float lightIntensity = dot(Normal, DiffuseLightDirection) * 0.5 + 0.5;
    Color = vec4(max(DiffuseColor.rgb * DiffuseIntensity * lightIntensity, 0.0) + Color.rgb, 1.0);

    vec3 ambientColor = EvaluateSH(indirectSH, Normal);
    Color += vec4(ambientColor, 0.0);

    TextureCoordinate = in_TextureCoordinate0;
    WorldPos = worldPosition;
    WorldPos.w = gl_Position.w;

    ApplyPosFixup();
}

// ported from Particles.fx's DecalVS (technique Decals)
#include <Common.glsl>
#include <PosFixup.glsl>

uniform mat4 World;
uniform mat4 View;
uniform mat4 Projection;

const vec3 LM_B1 = vec3(0.8164966, 0.0, 0.5773503);
const vec3 LM_B2 = vec3(-0.4082483, 0.7071068, 0.5773503);
const vec3 LM_B3 = vec3(-0.4082483, -0.7071068, 0.5773503);

attribute vec4 in_Position0;
attribute vec4 in_Color0;
attribute vec3 in_Normal0;
attribute vec2 in_TextureCoordinate0;
attribute vec2 in_TextureCoordinate1; // lightmap UV
attribute vec3 in_Tangent0;
attribute vec3 in_Binormal0;

varying vec4 Color;
varying vec2 UV;
varying vec2 LightmapUV;
varying vec4 WorldPos;
varying vec3 Normal;
varying vec3 Basis1;
varying vec3 Basis2;
varying vec3 Basis3;

void main()
{
    vec4 worldPosition = in_Position0 * World;
    gl_Position = (worldPosition * View) * Projection;
    WorldPos = worldPosition;
    Color = in_Color0;
    UV = in_TextureCoordinate0;
    LightmapUV = in_TextureCoordinate1;
    Normal = in_Normal0;

    Basis1 = LM_B1.x * in_Tangent0 + LM_B1.y * in_Binormal0 + LM_B1.z * in_Normal0;
    Basis2 = LM_B2.x * in_Tangent0 + LM_B2.y * in_Binormal0 + LM_B2.z * in_Normal0;
    Basis3 = LM_B3.x * in_Tangent0 + LM_B3.y * in_Binormal0 + LM_B3.z * in_Normal0;

    ApplyPosFixup();
}

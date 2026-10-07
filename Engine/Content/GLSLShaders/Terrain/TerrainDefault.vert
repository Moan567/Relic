// ported from TerrainDefault.fx's MainVS (technique BasicColorDrawing)
#include <Common.glsl>
#include <PosFixup.glsl>

uniform mat4 World;
uniform mat4 View;
uniform mat4 Projection;
uniform bool Skybox3DView;
uniform vec3 SkyboxViewTranslation;

const vec3 VALVE_B1 = vec3(0.81649661, 0.0, 0.57735026);
const vec3 VALVE_B2 = vec3(-0.40824831, 0.70710678, 0.57735026);
const vec3 VALVE_B3 = vec3(-0.40824831, -0.70710678, 0.57735026);

attribute vec4 in_Position0;
attribute vec4 in_Normal0;
attribute vec3 in_TextureCoordinate0; // xy = UV, z = blend factor
attribute vec2 in_TextureCoordinate1; // lightmap UV
attribute vec4 in_Tangent0;           // w = handedness

varying vec3 Normal;
varying vec3 Tangent;
varying vec3 Binormal;
varying vec3 B1;
varying vec3 B2;
varying vec3 B3;
varying vec4 WorldPos;
varying vec3 TexCoord;
varying vec2 LightmapCoord;

void main()
{
    vec4 worldPosition = in_Position0 * World;
    vec4 viewPosition = worldPosition * View;

    gl_Position = viewPosition * Projection;
    WorldPos = worldPosition;
    WorldPos.w = gl_Position.w;

    if (Skybox3DView)
    {
        WorldPos = (worldPosition - vec4(SkyboxViewTranslation, 0.0)) * 16.0;
        vec4 fakeViewPosition = WorldPos * View;
        WorldPos.w = (fakeViewPosition * Projection).w;
    }

    // Transform TBN into world space
    vec3 N = normalize((vec4(in_Normal0.xyz, 0.0) * World).xyz);
    vec3 T = normalize((vec4(in_Tangent0.xyz, 0.0) * World).xyz);
    vec3 B = cross(N, T) * in_Tangent0.w;

    // Rotate the canonical Valve basis into world space using this vertex's TBN.
    B1 = VALVE_B1.x * T + VALVE_B1.y * B + VALVE_B1.z * N;
    B2 = VALVE_B2.x * T + VALVE_B2.y * B + VALVE_B2.z * N;
    B3 = VALVE_B3.x * T + VALVE_B3.y * B + VALVE_B3.z * N;

    Normal = N;
    TexCoord = in_TextureCoordinate0;
    LightmapCoord = in_TextureCoordinate1;
    Tangent = T;
    Binormal = B;

    ApplyPosFixup();
}

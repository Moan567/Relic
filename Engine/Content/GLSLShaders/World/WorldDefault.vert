#include <Common.glsl>
#include <PosFixup.glsl>

uniform mat4 World;
uniform mat4 View;
uniform mat4 Projection;

uniform vec3 SkyboxViewTranslation;

const vec3 LM_B1 = vec3(0.8164966, 0.0, 0.5773503);
const vec3 LM_B2 = vec3(-0.4082483, 0.7071068, 0.5773503);
const vec3 LM_B3 = vec3(-0.4082483, -0.7071068, 0.5773503);

uniform bool ExpandWireframe;
uniform bool Skybox3DView;

attribute vec4 in_Position0;
attribute vec4 in_Normal0;
attribute vec3 in_Tangent0;
attribute vec3 in_Binormal0;
attribute vec2 in_TextureCoordinate0;
attribute vec2 in_TextureCoordinate1; // lightmap UV

varying vec4 WorldPos;
varying vec3 Normal;
varying vec2 TextureCoordinate;
varying vec2 LightCoordinate;
varying vec3 FaceTangent;
varying vec3 FaceBinormal;
varying vec3 Basis1;
varying vec3 Basis2;
varying vec3 Basis3;
varying vec4 ScreenPos;

void main()
{
    vec4 localPosition = ExpandWireframe
        ? in_Position0 + in_Normal0 * 0.0001
        : in_Position0;

    vec4 worldPosition = localPosition * World;
    vec4 viewPosition = worldPosition * View;

    gl_Position = viewPosition * Projection;
    WorldPos = worldPosition;
    WorldPos.w = gl_Position.w;
    ScreenPos = gl_Position;

    if (Skybox3DView)
    {
        WorldPos = (worldPosition - vec4(SkyboxViewTranslation, 0.0)) * 16.0;

        vec4 fakeViewPosition = WorldPos * View;
        WorldPos.w = (fakeViewPosition * Projection).w;
    }

    Normal = in_Normal0.xyz;
    TextureCoordinate = in_TextureCoordinate0;
    LightCoordinate = in_TextureCoordinate1;

    FaceTangent = in_Tangent0;
    FaceBinormal = in_Binormal0;

    Basis1 = LM_B1.x * in_Tangent0 + LM_B1.y * in_Binormal0 + LM_B1.z * in_Normal0.xyz;
    Basis2 = LM_B2.x * in_Tangent0 + LM_B2.y * in_Binormal0 + LM_B2.z * in_Normal0.xyz;
    Basis3 = LM_B3.x * in_Tangent0 + LM_B3.y * in_Binormal0 + LM_B3.z * in_Normal0.xyz;

    ApplyPosFixup();
}

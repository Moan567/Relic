#include <Common.glsl>
#include <PosFixup.glsl>

uniform mat4 World;
uniform mat4 View;
uniform mat4 Projection;

attribute vec4 in_Position0;
attribute vec4 in_Normal0;
attribute vec3 in_Tangent0;
attribute vec3 in_Binormal0;
attribute vec2 in_TextureCoordinate0;
attribute vec2 in_TextureCoordinate1;

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
    vec4 worldPosition = in_Position0 * World;
    vec4 viewPosition = worldPosition * View;

    gl_Position = viewPosition * Projection;
    WorldPos = worldPosition;
    ScreenPos = gl_Position;

    Normal = in_Normal0.xyz;
    TextureCoordinate = in_TextureCoordinate0;
    LightCoordinate = in_TextureCoordinate1;

    FaceTangent = in_Tangent0;
    FaceBinormal = in_Binormal0;

    // Pointless here, but they still need to be set
    Basis1 = in_Normal0.xyz;
    Basis2 = in_Normal0.xyz;
    Basis3 = in_Normal0.xyz;
    
    ApplyPosFixup();
}

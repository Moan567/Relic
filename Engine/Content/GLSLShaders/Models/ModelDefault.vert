#include <ModelCommon.glsl>
#include <PosFixup.glsl>

attribute vec4 in_Position0;
attribute vec4 in_Normal0;
attribute vec4 in_Tangent0;
attribute vec4 in_Binormal0;
attribute vec2 in_TextureCoordinate0;

varying vec4 ScreenPosition;
varying vec4 WorldPos;
varying vec4 Color;
varying vec3 Normal;
varying vec3 Tangent;
varying vec3 Binormal;
varying vec2 TextureCoordinate;

void main()
{
    vec4 worldPosition = in_Position0 * World;
    vec4 viewPosition = worldPosition * View;
    gl_Position = viewPosition * Projection;
    ScreenPosition = gl_Position;

    Normal = normalize((in_Normal0 * WorldInverseTranspose).xyz);
    Tangent = normalize((in_Tangent0 * WorldInverseTranspose).xyz);
    Binormal = normalize((in_Binormal0 * WorldInverseTranspose).xyz);

    Color = vec4(1.0, 1.0, 1.0, 1.0);
    TextureCoordinate = in_TextureCoordinate0;
    WorldPos = worldPosition;
    WorldPos.w = gl_Position.w;

    ApplyPosFixup();
}

#include <ModelCommon.glsl>
#include <PosFixup.glsl>

attribute vec4 in_Position0;
attribute vec4 in_Normal0;
attribute vec4 in_Color0;
attribute vec2 in_TextureCoordinate0;

varying vec3 WorldPos;
varying vec3 Normal;
varying vec4 Color;
varying vec2 UV;
varying vec4 ScreenPos;

void main()
{
    vec4 worldPosition = in_Position0 * World;
    vec4 viewPosition = worldPosition * View;
    gl_Position = viewPosition * Projection;
    ScreenPos = gl_Position;
    Normal = in_Normal0.xyz;
    WorldPos = worldPosition.xyz;
    Color = ApplyFog(in_Color0, worldPosition.xyz);
    UV = in_TextureCoordinate0;

    ApplyPosFixup();
}

// ported from Particles.fx's MainVS (technique Particles)
#include <Common.glsl>
#include <PosFixup.glsl>

uniform mat4 World;
uniform mat4 View;
uniform mat4 Projection;

attribute vec4 in_Position0;
attribute vec4 in_Color0;
attribute vec2 in_TextureCoordinate0;

varying vec4 Color;
varying vec2 UV;
varying vec4 ScreenPos;

void main()
{
    vec4 worldPosition = in_Position0 * World;
    vec4 viewPosition = worldPosition * View;
    gl_Position = viewPosition * Projection;
    ScreenPos = gl_Position;
    Color = in_Color0;
    UV = in_TextureCoordinate0;

    ApplyPosFixup();
}

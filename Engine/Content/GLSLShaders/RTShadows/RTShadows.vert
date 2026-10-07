#include <PosFixup.glsl>

uniform mat4 World;
uniform mat4 View;
uniform mat4 Projection;

attribute vec4 in_Position0;
attribute vec2 in_TextureCoordinate0;
attribute vec4 in_Color0;

varying vec2 texcoord;
varying vec4 color;

void main()
{
    gl_Position = ((in_Position0 * World) * View) * Projection;
    texcoord = in_TextureCoordinate0;
    color = in_Color0;

    ApplyPosFixup();
}

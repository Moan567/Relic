#include <PosFixup.glsl>

uniform mat4 WorldViewProjection;

attribute vec4 in_Position0;
attribute vec4 in_Color0;

varying vec4 Color;

void main()
{
    gl_Position = in_Position0 * WorldViewProjection;
    Color = in_Color0;
    
    ApplyPosFixup();
}

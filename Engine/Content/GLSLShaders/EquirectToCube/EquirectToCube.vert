#include <PosFixup.glsl>

attribute vec4 in_Position0;

varying vec2 ClipPosition;

void main()
{
    gl_Position = in_Position0;
    ClipPosition = in_Position0.xy;

    ApplyPosFixup();
}

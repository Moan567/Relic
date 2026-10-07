#include <PosFixup.glsl>

attribute vec4 in_Position0;
attribute vec2 in_TextureCoordinate0;

varying vec2 TexCoord;

void main()
{
    gl_Position = in_Position0;
    TexCoord = in_TextureCoordinate0;

    ApplyPosFixup();
}

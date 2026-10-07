#include <PosFixup.glsl>

uniform mat4 World;
uniform mat4 View;
uniform mat4 Projection;

attribute vec4 in_Position0;

varying vec3 TextureCoordinate;

void main()
{
    vec4 worldPosition = in_Position0 * World;
    vec4 viewPosition = worldPosition * View;
    gl_Position = viewPosition * Projection;
    TextureCoordinate = worldPosition.xyz;
    
    ApplyPosFixup();
}

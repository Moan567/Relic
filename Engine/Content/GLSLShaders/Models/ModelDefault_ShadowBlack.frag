#include <ModelCommon.glsl>

varying vec4 ScreenPosition;
varying vec4 WorldPos;
varying vec4 Color;
varying vec3 Normal;
varying vec3 Tangent;
varying vec3 Binormal;
varying vec2 TextureCoordinate;

void main()
{
    gl_FragColor = WriteSceneOutput(vec4(0.0, 0.0, 0.0, 1.0));
}

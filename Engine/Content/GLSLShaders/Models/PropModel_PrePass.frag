// ported from PropModel.fx's PrePassPS
#include <ModelCommon.glsl>

uniform sampler2D MainTex;
uniform bool AlphaPass; // unused in original PropModel.fx, kept for parameter parity

varying vec3 WorldPos;
varying vec3 Normal;
varying vec4 Color;
varying vec2 UV;
varying vec4 ScreenPos;

void main()
{
    vec4 texColor = texture2D(MainTex, UV);

    if (texColor.a - 0.6 < 0.0)
        discard;
    texColor.a = 1.0;

    gl_FragColor = texColor;
}

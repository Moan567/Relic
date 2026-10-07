// ported from PropModel.fx's MainPS
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
    texColor.rgb = ConvertSRGB(texColor.rgb);

    if (!Transparent)
    {
        if (texColor.a - 0.5 < 0.0)
            discard;
        texColor.a = 1.0;
    }

    if (texColor.a > 0.6)
        texColor.a = 1.0;

    vec4 lightColor = Color;
    lightColor = ApplyRealtimeLights(lightColor.xyz, Normal, WorldPos);

    vec4 finalColor = vec4(ApplyLight(texColor.xyz, lightColor.xyz), texColor.a);

    gl_FragColor = WriteSceneOutput(finalColor);
}

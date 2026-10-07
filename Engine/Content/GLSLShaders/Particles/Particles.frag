// ported from Particles.fx's MainPS (technique Particles)
#include <Common.glsl>

uniform sampler2D MainTex;
uniform bool AlphaClip;
uniform bool UseVertColor;

varying vec4 Color;
varying vec2 UV;
varying vec4 ScreenPos;

void main()
{
    vec4 texColor = texture2D(MainTex, UV, -1.0);
    texColor.rgb = ConvertSRGB(texColor.rgb);

    if (AlphaClip)
    {
        if (texColor.a - 0.8 < 0.0)
            discard;
        texColor.a = 1.0;
    }

    gl_FragColor = texColor * (UseVertColor ? Color : vec4(1.0, 1.0, 1.0, 1.0));
}

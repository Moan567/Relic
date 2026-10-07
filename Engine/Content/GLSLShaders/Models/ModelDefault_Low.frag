// ported from ModelDefault.fx's PixelShaderFunction_Low / _Low_NoClip via
// ModelCommon.fxh's BasicModelPixelShader_Low / _LowCore
#include <ModelCommon.glsl>

uniform sampler2D MainTex;
uniform bool AlphaClip;

varying vec4 ScreenPosition;
varying vec4 WorldPos;
varying vec4 Color;
varying vec3 Normal;
varying vec3 Tangent;
varying vec3 Binormal;
varying vec2 TextureCoordinate;

void main()
{
    vec4 texColor = texture2D(MainTex, TextureCoordinate);
    texColor.rgb = ConvertSRGB(texColor.rgb);

    if (!Transparent)
    {
        if (AlphaClip && texColor.a - 0.8 < 0.0)
            discard;
        texColor.a = 1.0;
    }

    vec3 light = Color.rgb;

    for (int j = 0; j < realtimeLightCount; j++)
    {
        vec3 ldir = normalize(realtimeLightPositions[j].xyz - WorldPos.xyz);
        float att = clamp((realtimeLightPositions[j].a
            - distance(WorldPos.xyz, realtimeLightPositions[j].rgb)) / realtimeLightPositions[j].a, 0.0, 1.0);
        att *= dot(ldir, Normal) * 0.5 + 0.5;

        float spotAng = realtimeLightSpotData[j].w;
        if (spotAng > 0.0)
        {
            float ang = acos(dot(-ldir, realtimeLightSpotData[j].xyz));
            att *= sqrt(clamp((spotAng - ang) / spotAng, 0.0, 1.0));
        }

        light += realtimeLightColors[j].rgb * realtimeLightColors[j].a * att;
    }

    vec3 lit = ApplyLight(texColor.rgb, light);
    gl_FragColor = WriteSceneOutput(vec4(ApplyFog(vec4(lit, texColor.a), WorldPos.xyz).rgb, texColor.a));
}

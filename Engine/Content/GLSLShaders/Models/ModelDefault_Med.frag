// ported from ModelDefault.fx's PixelShaderFunction_Med / _Med_NoClip via
// ModelCommon.fxh's BasicModelPixelShader_Med / _MedCore
#include <ModelCommon.glsl>

uniform sampler2D MainTex;
uniform sampler2D SpecTex;
uniform sampler2D NormalTex; // unused at Med (kept for parameter parity)
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
    vec4 specColor = texture2D(SpecTex, TextureCoordinate);

    if (!Transparent)
    {
        if (AlphaClip && texColor.a - 0.8 < 0.0)
            discard;
        texColor.a = 1.0;
    }

    vec3 N = Normal;
    vec3 V = normalize(cameraPos - WorldPos.xyz);

    vec3 diffuse;
    vec3 specular = CalculateCombinedLighting(WorldPos.xyz, N, V, specColor.r, specColor.g, diffuse);

    vec3 ambient = EvaluateSH(indirectSH, N);
    vec3 specTerm = specular * length(Color.xyz);
    vec3 lit = ApplyLight(texColor.rgb, diffuse + ambient) + specTerm;

    gl_FragColor = WriteSceneOutput(vec4(ApplyFog(vec4(lit, texColor.a), WorldPos.xyz).rgb, texColor.a));
}

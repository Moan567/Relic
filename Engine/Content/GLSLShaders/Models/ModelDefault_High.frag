// ported from ModelDefault.fx's PixelShaderFunction / _NoClip via
// ModelCommon.fxh's BasicModelPixelShader / BasicModelPixelShaderCore
#include <ModelCommon.glsl>
#include <Cubemap.glsl>

uniform sampler2D MainTex;
uniform sampler2D SpecTex;
uniform sampler2D NormalTex;
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

    vec4 specColor = texture2D(SpecTex, TextureCoordinate);
    vec3 bump = (2.0 * texture2D(NormalTex, TextureCoordinate).xyz) - 1.0;
    vec3 N = normalize(Normal + bump.x * Tangent + bump.y * Binormal);
    vec3 V = normalize(cameraPos - WorldPos.xyz);

    vec3 diffuse;
    vec3 specular = CalculateCombinedLighting(WorldPos.xyz, N, V, specColor.r, specColor.g, diffuse);

    vec3 ambient = EvaluateSH(indirectSH, N);

    vec3 reflection = vec3(0.0, 0.0, 0.0);
    if (specColor.b > 0.01)
        reflection = CubemapSample(reflect(-V, N)).rgb * specColor.b;

    vec3 lit = ApplyLight(texColor.xyz, diffuse + ambient) + specular + reflection;

    gl_FragColor = WriteSceneOutput(vec4(ApplyFog(vec4(lit, texColor.a), WorldPos.xyz).rgb, texColor.a));
}

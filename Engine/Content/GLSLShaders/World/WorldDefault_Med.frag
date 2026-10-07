#include <Common.glsl>

uniform sampler2D BrushTex;
uniform sampler2D BrushNorm;
uniform sampler2D LightmapB1;
uniform sampler2D LightmapB2;
uniform sampler2D LightmapB3;

uniform bool DisableLighting;
uniform bool ShowLightmap;
uniform bool AlphaClip;

varying vec4 WorldPos;
varying vec3 Normal;
varying vec2 TextureCoordinate;
varying vec2 LightCoordinate;
varying vec3 FaceTangent;
varying vec3 FaceBinormal;
varying vec3 Basis1;
varying vec3 Basis2;
varying vec3 Basis3;
varying vec4 ScreenPos;

vec4 GetWorldLightOutput_lq(vec3 basis1, vec3 basis2, vec3 basis3, vec2 lightCoordinate, vec3 bumpNormal)
{
    vec4 lightNormColor = texture2D(LightmapB1, lightCoordinate);
    vec4 lightTanColor = texture2D(LightmapB2, lightCoordinate);
    vec4 lightBiColor = texture2D(LightmapB3, lightCoordinate);

    vec4 lightColor = lightNormColor * dot(basis1, bumpNormal) +
                       lightTanColor * dot(basis2, bumpNormal) +
                       lightBiColor * dot(basis3, bumpNormal);

    if (!DisableLighting || ShowLightmap)
    {
        return vec4(lightColor.xyz, 1.0);
    }
    else
    {
        return vec4(1.0, 1.0, 1.0, 1.0);
    }
}

void main()
{
    vec4 textureColor = texture2D(BrushTex, TextureCoordinate, -0.5);
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    if (AlphaClip && textureColor.a - 0.01 < 0.0)
        discard;

    vec3 bump = (2.0 * texture2D(BrushNorm, TextureCoordinate).xyz) - vec3(1.0, 1.0, 1.0);
    vec3 bumpNormal = Normal + (bump.x * FaceTangent + bump.y * FaceBinormal * -1.0);

    vec4 Color0 = textureColor;
    vec4 Color2 = WorldPos;

    vec4 Color3 = vec4(GetWorldLightOutput_lq(Basis1, Basis2, Basis3, LightCoordinate, bumpNormal).xyz, 1.0);

    vec3 lightTerm = ApplyRealtimeLights(Color3.xyz, bumpNormal, Color2.xyz).xyz;

    Color3 = ApplyFog(vec4(ApplyLight(Color0.rgb, lightTerm), Color0.a), WorldPos.xyz);

    gl_FragColor = WriteSceneOutput(Color3);
}

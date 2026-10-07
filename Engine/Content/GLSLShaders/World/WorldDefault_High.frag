#include <Common.glsl>
#include <Cubemap.glsl>

uniform sampler2D BrushTex;
uniform sampler2D BrushSpec;
uniform sampler2D BrushNorm;
uniform sampler2D LightmapB1;
uniform sampler2D LightmapB2;
uniform sampler2D LightmapB3;

uniform vec2 LightmapSize;
uniform vec3 cubeSamplePos;

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

// from http://www.java-gaming.org/index.php?topic=35123.0
vec4 cubic(float v)
{
    vec4 n = vec4(1.0, 2.0, 3.0, 4.0) - v;
    vec4 s = n * n * n;
    float x = s.x;
    float y = s.y - 4.0 * s.x;
    float z = s.z - 4.0 * s.y + 6.0 * s.x;
    float w = 6.0 - x - y - z;
    return vec4(x, y, z, w) * (1.0 / 6.0);
}

vec4 textureBicubic(sampler2D samp, vec2 texCoords)
{
    vec2 texSize = LightmapSize;
    vec2 invTexSize = 1.0 / texSize;

    texCoords = texCoords * texSize - 0.5;

    vec2 fxy = fract(texCoords);
    texCoords -= fxy;

    vec4 xcubic = cubic(fxy.x);
    vec4 ycubic = cubic(fxy.y);

    vec4 c = texCoords.xxyy + vec4(-0.5, 1.5, -0.5, 1.5);

    vec4 s = vec4(xcubic.xz + xcubic.yw, ycubic.xz + ycubic.yw);
    vec4 offset = c + vec4(xcubic.yw, ycubic.yw) / s;

    offset *= invTexSize.xxyy;

    vec4 sample0 = texture2D(samp, offset.xz);
    vec4 sample1 = texture2D(samp, offset.yz);
    vec4 sample2 = texture2D(samp, offset.xw);
    vec4 sample3 = texture2D(samp, offset.yw);

    float sx = s.x / (s.x + s.y);
    float sy = s.z / (s.z + s.w);

    return mix(mix(sample3, sample2, sx), mix(sample1, sample0, sx), sy);
}

vec4 GetWorldLightOutput(vec3 basis1, vec3 basis2, vec3 basis3, vec2 lightCoordinate, vec3 bumpNormal)
{
    vec4 lightNormColor = textureBicubic(LightmapB1, lightCoordinate);
    vec4 lightTanColor = textureBicubic(LightmapB2, lightCoordinate);
    vec4 lightBiColor = textureBicubic(LightmapB3, lightCoordinate);

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
    vec4 specularColor = texture2D(BrushSpec, TextureCoordinate);

    vec3 reflectionColor = vec3(0.0, 0.0, 0.0);

    if (specularColor.b > 0.01)
    {
        vec3 viewDir = normalize(WorldPos.xyz - cubeSamplePos);
        vec3 reflectionVector = reflect(viewDir, normalize(bumpNormal));
        vec3 cubeColor = CubemapSample(reflectionVector).rgb;
        reflectionColor = cubeColor * specularColor.b;
    }

    vec4 Color0 = textureColor + vec4(reflectionColor, 0.0);
    vec4 Color2 = WorldPos;

    vec4 Color3 = vec4(GetWorldLightOutput(Basis1, Basis2, Basis3, LightCoordinate, bumpNormal).xyz, 1.0);

    vec3 lightTerm = ApplyRealtimeLights(Color3.xyz, bumpNormal, Color2.xyz).xyz;

    Color3 = ApplyFog(vec4(ApplyLight(Color0.rgb, lightTerm), Color0.a), WorldPos.xyz);

    gl_FragColor = WriteSceneOutput(Color3);
}

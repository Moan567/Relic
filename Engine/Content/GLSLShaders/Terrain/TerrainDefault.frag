// ported from TerrainDefault.fx's MainPS (technique BasicColorDrawing)
#include <Common.glsl>

uniform sampler2D mainTexture;
uniform sampler2D normalTexture;
uniform sampler2D blendTexture;
uniform sampler2D terrainBlendMix;
uniform vec2 LightmapSize;
uniform sampler2D LightmapB1;
uniform sampler2D LightmapB2;
uniform sampler2D LightmapB3;

varying vec3 Normal;
varying vec3 Tangent;
varying vec3 Binormal;
varying vec3 B1;
varying vec3 B2;
varying vec3 B3;
varying vec4 WorldPos;
varying vec3 TexCoord;
varying vec2 LightmapCoord;

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

void main()
{
    float texBlendOffset = texture2D(terrainBlendMix, TexCoord.xy).r;
    vec4 textureColor = mix(texture2D(blendTexture, TexCoord.xy),
                             texture2D(mainTexture, TexCoord.xy),
                             clamp(TexCoord.z + texBlendOffset, 0.0, 1.0));
    textureColor.rgb = ConvertSRGB(textureColor.rgb);

    vec3 bump = (2.0 * texture2D(normalTexture, TexCoord.xy).xyz) - vec3(1.0, 1.0, 1.0);
    vec3 bumpNormal = normalize(Normal + (bump.x * Tangent + bump.y * Binormal * -1.0));

    vec3 lm1 = textureBicubic(LightmapB1, LightmapCoord).rgb;
    vec3 lm2 = textureBicubic(LightmapB2, LightmapCoord).rgb;
    vec3 lm3 = textureBicubic(LightmapB3, LightmapCoord).rgb;

    float w1 = clamp(dot(bumpNormal, normalize(B1)), 0.0, 1.0);
    float w2 = clamp(dot(bumpNormal, normalize(B2)), 0.0, 1.0);
    float w3 = clamp(dot(bumpNormal, normalize(B3)), 0.0, 1.0);

    vec3 lightmapColor = lm1 * w1 + lm2 * w2 + lm3 * w3;

    vec3 realtimeLit = ApplyRealtimeLights(lightmapColor, bumpNormal, WorldPos.xyz).xyz;
    vec4 finalColor = ApplyFog(vec4(ApplyLight(textureColor.rgb, realtimeLit), textureColor.a), WorldPos.xyz);
    gl_FragColor = WriteSceneOutput(finalColor);
}

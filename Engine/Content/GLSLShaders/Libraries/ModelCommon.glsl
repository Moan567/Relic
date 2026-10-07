// ModelCommon.glsl - ported from Content/Shaders/ModelCommon.fxh
//
// Pull this into any model shader with:
//     #include <ModelCommon.glsl>
//
// This only carries the struct-independent lighting math (Common.fxh's old
// VertexShaderOutput-shaped wrapper functions - BasicModelPixelShader and
// friends - aren't ported here since they were coupled to one specific
// varying layout; each model shader now inlines that same logic directly in
// its own main(), calling into these library functions, which keeps the
// library reusable across shaders with different vertex outputs.

#include <Common.glsl>

#define MAXSTATICLIGHTS 4
#define SHCOEFFICIENTS 9

uniform int static_lightaffectingcount;
uniform vec4 static_lightpositions[4];
uniform vec4 static_lightcolors[4];
uniform vec4 static_lightangles[4]; // xyz = heading vector, w = optional spotlight angle

uniform vec3 indirectSH[9];

uniform vec3 cameraForward;
uniform mat4 World;
uniform mat4 WorldInverseTranspose;
uniform mat4 View;
uniform mat4 Projection;

uniform bool ShadowPass;
uniform bool Transparent;

uniform vec3 DiffuseLightDirection;
uniform vec4 DiffuseColor;
uniform float DiffuseIntensity;

uniform float shine;

float Specular(vec3 lightDir, vec3 viewDir, vec3 normal, float smul)
{
    vec3 r = normalize(2.0 * dot(normal, lightDir) * normal - lightDir);
    float ndotl = max(0.0001, dot(normal, lightDir));
    float rdotv = max(0.0, dot(r, viewDir));
    return ndotl * pow(rdotv, smul * shine);
}

vec4 CalculateSpecularLighting(vec3 worldpos, vec3 normal, vec3 viewVector, float specularIntensity, float smul)
{
    vec3 total = vec3(0.0, 0.0, 0.0);

    total += clamp(specularIntensity * max(Specular(DiffuseLightDirection, viewVector, normal, smul), 0.0)
                     * DiffuseColor.rgb * DiffuseIntensity, 0.0, 1.0);

    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        vec3 ldir = normalize(static_lightpositions[i].xyz - worldpos);
        float f = 1.0 - clamp(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w, 0.0, 1.0);
        vec3 lc = static_lightcolors[i].rgb * static_lightcolors[i].a * (f * f);

        float theta = acos(dot(ldir, normalize(static_lightangles[i].xyz)));
        lc *= (theta > static_lightangles[i].w && static_lightangles[i].w > 0.0) ? 0.0 : 1.0;

        total += clamp(specularIntensity * max(Specular(ldir, viewVector, normal, smul), 0.0) * lc, 0.0, 1.0);
    }

    for (int j = 0; j < realtimeLightCount; j++)
    {
        vec3 ldir = normalize(realtimeLightPositions[j].xyz - worldpos);
        float att = clamp((realtimeLightPositions[j].a
            - distance(worldpos, realtimeLightPositions[j].rgb)) / realtimeLightPositions[j].a, 0.0, 1.0);

        vec3 lc = realtimeLightColors[j].rgb * realtimeLightColors[j].a * att * att;

        float spotAng = realtimeLightSpotData[j].w;
        if (spotAng > 0.0)
        {
            float ang = acos(dot(-ldir, realtimeLightSpotData[j].xyz));
            lc *= sqrt(clamp((spotAng - ang) / spotAng, 0.0, 1.0));
        }

        total += clamp(specularIntensity * max(Specular(ldir, viewVector, normal, smul), 0.0) * lc, 0.0, 1.0);
    }

    return vec4(total, 0.0);
}

vec3 CalculateStaticLighting(vec3 worldpos, vec3 worldnorm)
{
    vec3 total = vec3(0.0, 0.0, 0.0);

    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        vec3 lightdir = normalize(static_lightpositions[i].xyz - worldpos);
        float f = 1.0 - clamp(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w, 0.0, 1.0);
        vec3 lc = static_lightcolors[i].rgb * static_lightcolors[i].a * (f * f);

        lc *= dot(lightdir, worldnorm) * 0.5 + 0.5;

        float theta = acos(dot(lightdir, normalize(static_lightangles[i].xyz)));
        lc *= (theta > static_lightangles[i].w && static_lightangles[i].w > 0.0) ? 0.0 : 1.0;

        total += lc;
    }
    return total;
}

vec3 EvaluateSH(vec3 coeffs[9], vec3 d)
{
    return coeffs[0] * 0.282095
         + coeffs[1] * (0.488603 * d.y)
         + coeffs[2] * (0.488603 * d.z)
         + coeffs[3] * (0.488603 * d.x)
         + coeffs[4] * (1.092548 * d.x * d.y)
         + coeffs[5] * (1.092548 * d.y * d.z)
         + coeffs[6] * (0.315392 * (3.0 * d.z * d.z - 1.0))
         + coeffs[7] * (1.092548 * d.x * d.z)
         + coeffs[8] * (0.546274 * (d.x * d.x - d.y * d.y));
}

// out params aren't in the addon's discovered-uniform set (they're not
// uniforms at all), so this keeps HLSL's `out float3 diffuseOut` verbatim -
// GLSL supports `out` function parameters the same way.
vec3 CalculateCombinedLighting(
    vec3 worldpos, vec3 N, vec3 V,
    float specInt, float specSmul,
    out vec3 diffuseOut)
{
    vec3 diff = vec3(0.0, 0.0, 0.0);
    vec3 spec = vec3(0.0, 0.0, 0.0);

    diff += (DiffuseColor.rgb * DiffuseIntensity * (dot(N, DiffuseLightDirection) * 0.5 + 0.5));
    spec += clamp(specInt * max(Specular(DiffuseLightDirection, V, N, specSmul), 0.0)
                     * DiffuseColor.rgb * DiffuseIntensity, 0.0, 1.0);

    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        vec3 ldir = normalize(static_lightpositions[i].xyz - worldpos);
        float f = 1.0 - clamp(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w, 0.0, 1.0);
        vec3 lc = static_lightcolors[i].rgb * static_lightcolors[i].a * (f * f);

        float theta = acos(dot(ldir, normalize(static_lightangles[i].xyz)));
        lc *= (theta > static_lightangles[i].w && static_lightangles[i].w > 0.0) ? 0.0 : 1.0;

        diff += lc * ((dot(ldir, N) * 0.5 + 0.5) * (dot(ldir, N) * 0.5 + 0.5));
        spec += clamp(specInt * max(Specular(ldir, V, N, specSmul), 0.0) * lc, 0.0, 1.0);
    }

    for (int j = 0; j < realtimeLightCount; j++)
    {
        vec3 ldir = normalize(realtimeLightPositions[j].xyz - worldpos);
        float att = clamp((realtimeLightPositions[j].a
            - distance(worldpos, realtimeLightPositions[j].rgb)) / realtimeLightPositions[j].a, 0.0, 1.0);

        vec3 lc = realtimeLightColors[j].rgb * realtimeLightColors[j].a * att * att;

        float spotAng = realtimeLightSpotData[j].w;
        if (spotAng > 0.0)
        {
            float ang = acos(dot(-ldir, realtimeLightSpotData[j].xyz));
            lc *= sqrt(clamp((spotAng - ang) / spotAng, 0.0, 1.0));
        }

        diff += lc * (dot(ldir, N) * 0.5 + 0.5);
        spec += clamp(specInt * max(Specular(ldir, V, N, specSmul), 0.0) * lc, 0.0, 1.0);
    }

    diffuseOut = diff;
    return spec;
}

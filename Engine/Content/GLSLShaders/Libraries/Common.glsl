#define MAXREALTIMELIGHTS 8

uniform int realtimeLightCount;
uniform vec4 realtimeLightPositions[8];   // xyz = position, w = range
uniform vec4 realtimeLightColors[8];      // rgb = color, a = intensity
uniform vec4 realtimeLightSpotData[8];    // xyz = direction, w = spot angle (0 = omni)

uniform vec4 fogColor;
uniform float fogIntensity;
uniform float fogStart;
uniform float fogEnd;
uniform float time;

uniform vec3 cameraPos;
uniform float cameraNear;
uniform float cameraFar;

uniform vec3 sunDir;
uniform vec3 sunColor;

uniform vec2 screenSize;

vec4 GetLightOutput(vec3 inputColor, vec3 AmbientColor)
{
    return vec4(inputColor + AmbientColor, 1.0);
}

vec4 ApplyRealtimeLights(vec3 baseLight, vec3 normal, vec3 worldpos)
{
    vec3 finalColor = baseLight;

    for (int i = 0; i < realtimeLightCount; i++)
    {
        float intensity = clamp((realtimeLightPositions[i].a - distance(worldpos, realtimeLightPositions[i].rgb)) / realtimeLightPositions[i].a, 0.0, 1.0);

        vec3 lightDir = -normalize(worldpos - realtimeLightPositions[i].xyz);
        intensity *= dot(lightDir, normal) * 0.5 + 0.5;

        float spotAng = realtimeLightSpotData[i].w;

        if (spotAng > 0.0)
        {
            float ang = acos(dot(-lightDir, realtimeLightSpotData[i].xyz));
            intensity *= sqrt(clamp((spotAng - ang) / spotAng, 0.0, 1.0));
        }

        finalColor += realtimeLightColors[i].rgb * realtimeLightColors[i].a * intensity;
    }
    return vec4(finalColor, 1.0);
}

vec3 ApplyLight(vec3 baseColor, vec3 lightColor)
{
    return baseColor * lightColor;
}

vec4 ApplyFog(vec4 baseColor, vec3 worldPos)
{
    float dist = clamp((distance(worldPos, cameraPos) - fogStart) / (fogEnd - fogStart), 0.0, 1.0);

    return vec4(mix(baseColor.rgb, fogColor.rgb, dist * fogIntensity), baseColor.a);
}

vec3 ConvertSRGB(vec3 c)
{
    return pow(c, vec3(2.2));
}

float GetLinearDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * cameraNear * cameraFar) / (cameraFar + cameraNear - z * (cameraFar - cameraNear));
}

vec4 WriteSceneOutput(vec4 color)
{
    return color;
}

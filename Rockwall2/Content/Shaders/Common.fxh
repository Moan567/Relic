#define MAXREALTIMELIGHTS 16

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float4 Binormal : BINORMAL;
    float2 TextureCoordinate : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float4 WorldPos : COLOR1;
    float4 Color : COLOR0;
    float3 Normal : TEXCOORD1;
    float3 Tangent : TEXCOORD2;
    float3 Binormal : TEXCOORD3;
    float2 TextureCoordinate : TEXCOORD0;
};

struct Light
{
    float3 WorldPos;
    float3 Color;
    float Intensity;
    float Range;
};
int realtimeLightCount;
float4 realtimeLightPositions[MAXREALTIMELIGHTS];
float4 realtimeLightColors[MAXREALTIMELIGHTS];
float4 realtimeLightSpotData[MAXREALTIMELIGHTS];

float4 fogColor;
float fogIntensity;
float fogStart, fogEnd;

float3 cameraPos;

float4 GetLightOutput(VertexShaderOutput input, float3 AmbientColor)
{
    return float4((input.Color.xyz + AmbientColor), 1);
}
float4 GetLightOutput(float3 inputColor, float3 AmbientColor)
{
    return float4((inputColor + AmbientColor), 1);
}

float4 ApplyRealtimeLights(float3 baseLight, float3 normal, float3 worldpos)
{
    float3 finalColor = baseLight;
    
    [unroll]
    for (int i = 0; i < realtimeLightCount; i++)
    {
        float intensity = saturate((realtimeLightPositions[i].a - distance(worldpos, realtimeLightPositions[i].rgb)) / realtimeLightPositions[i].a);
        
        float3 lightDir = -normalize(worldpos - realtimeLightPositions[i].xyz);
        intensity *= dot(lightDir, normal) * 0.5 + 0.5;

        float spotAng = realtimeLightSpotData[i].w;
        
        if (spotAng > 0)
        {
            float ang = acos(dot(-lightDir, realtimeLightSpotData[i].xyz));
            intensity *= pow(saturate(spotAng - ang) / spotAng, 0.5f);
        }
        
        finalColor += realtimeLightColors[i].rgb * realtimeLightColors[i].a * intensity;
    }
    
    return float4(finalColor, 1);
}

float3 ApplyLight(float3 baseColor, float3 lightColor)
{
    return baseColor * lightColor * 2;
}

float4 ApplyFog(float4 baseColor, float3 worldPos)
{
    float dist = saturate((distance(worldPos, cameraPos) - fogStart) / (fogEnd - fogStart));
    
    return float4(lerp(baseColor.rgb, fogColor.rgb, dist * fogIntensity), baseColor.a);
}
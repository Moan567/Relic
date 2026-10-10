#define MAXSTATICLIGHTS 4
#include "Common.fxh"

int static_lightaffectingcount;
float4 static_lightpositions[MAXSTATICLIGHTS];
float4 static_lightcolors[MAXSTATICLIGHTS];
float4 static_lightangles[MAXSTATICLIGHTS]; //First 3 are the heading vector, 4th is the optional spotlight angle

float3 cameraForward;
float4x4 World;
float4x4 WorldInverseTranspose;
float4x4 View;
float4x4 Projection;

bool ShadowPass;

//Ambient
float4 AmbientColor = float4(1, 1, 1, 1);
float AmbientIntensity = 0.25;

//Diffuse
float3 DiffuseLightDirection = float3(1, 0, 0);
float4 DiffuseColor = float4(1, 1, 1, 1);
float DiffuseIntensity = 1.0;

float shine = 0;

float3 CalculateStaticLighting(float3 worldpos, float3 worldnorm)
{
    float3 totalcolor = float3(0, 0, 0);
    [unroll]
    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        float3 lcolor = static_lightcolors[i].rgb * static_lightcolors[i].a;

        //First, falloff
        lcolor *= pow(1 - saturate(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w), 2);
        
        float3 lightdir = normalize(static_lightpositions[i].xyz - worldpos);
        
        //Then, half lambert
        lcolor *= dot(lightdir, worldnorm) * 0.5 + 0.5;
        
        float theta = acos(dot(lightdir, normalize(static_lightangles[i].xyz)));

        lcolor *= (theta > static_lightangles[i].w && static_lightangles[i].w > 0 ? 0 : 1);
        
        totalcolor += lcolor;
    }
    return totalcolor;
}
float Specular(float3 lightDir, float3 viewDir, float3 normal, float smul)
{
    float3 r = normalize(2.0 * dot(normal, lightDir) * normal - lightDir);
    float ndotl = max(0.0001f, dot(normal, lightDir));
    float rdotv = max(0.0f, dot(r, viewDir));
    return ndotl * pow(rdotv, smul * shine);
}

float4 CalculateSpecularLighting(float3 worldpos, float3 normal, float3 viewVector, float specularIntensity, float smul)
{
    float3 totalcolor = float3(0, 0, 0);
    
    //Directional light
    float3 dlightdir = DiffuseLightDirection;
        
    totalcolor += saturate(specularIntensity * max(Specular(dlightdir, viewVector, normal, smul), 0) * DiffuseColor * DiffuseIntensity);
    
    [unroll]
    for (int i = 0; i < static_lightaffectingcount; i++)
    {
        float3 lightdir = normalize(static_lightpositions[i].xyz - worldpos);
        //float3 r = normalize(2 * dot(normal, lightdir) * normal - lightdir);
        
        float3 lcolor = static_lightcolors[i].rgb * static_lightcolors[i].a;
        lcolor *= pow(1 - saturate(distance(worldpos, static_lightpositions[i].xyz) / static_lightpositions[i].w), 2);
        
        totalcolor += saturate(specularIntensity * max(Specular(lightdir, viewVector, normal, smul), 0) * lcolor);
    }
    [unroll]
    for (int j = 0; j < realtimeLightCount; j++)
    {
        float3 lightdir = normalize(realtimeLightPositions[j].xyz - worldpos);
        float3 r = normalize(2 * dot(normal, lightdir) * normal - lightdir);
        
        float3 lcolor = realtimeLightColors[i].rgb * realtimeLightColors[i].a;
        lcolor *= pow(1 - saturate(distance(worldpos, realtimeLightPositions[i].xyz) / realtimeLightPositions[i].w), 2);
        
        totalcolor += saturate(specularIntensity * max(Specular(lightdir, viewVector, normal, smul), 0) * lcolor);
    }
    return float4(totalcolor, 0);
}
float4 BasicModelPixelShader(VertexShaderOutput input, bool ShadowPass, sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler, float3 ambientColor) : SV_Target0
{
    float4 textureColor = tex2D(textureSampler, input.TextureCoordinate);
    float4 specularColor = tex2D(specularSampler, input.TextureCoordinate);
    
    clip(textureColor.a - 0.1f);
    
    float3 bump = (tex2D(normalSampler, input.TextureCoordinate).xyz - float3(0.5, 0.5, 0.5));
    
    float3 bumpNormal = input.Normal + (bump.x * input.Tangent + bump.y * input.Binormal);
    
    float3 color = float3(1, 1, 1);
    
    float3 addLightColor = CalculateStaticLighting(input.WorldPos.xyz, bumpNormal);
    color = float4(addLightColor, 1);
    
    float lightIntensity = dot(bumpNormal.xyz, DiffuseLightDirection) * 0.5 + 0.5;
    color = float4(saturate(DiffuseColor * DiffuseIntensity * lightIntensity).rgb + color, 1);

    float4 Color0 = textureColor;
    float4 Color3 = float4(GetLightOutput(color.xyz, ambientColor.xyz).xyz, 1);
    
    Color3 = ApplyRealtimeLights(Color3.xyz, bumpNormal, input.WorldPos.xyz);
    
    float4 finalColor = float4(ApplyLight(Color0.rgb, Color3.rgb), Color0.a);
    
    return float4(ApplyFog(finalColor + CalculateSpecularLighting(input.WorldPos.xyz, bumpNormal, normalize(cameraPos - input.WorldPos.xyz), specularColor.r, specularColor.g), input.WorldPos.xyz).xyz, Color0.a);
}
float4 BasicModelPixelShader_Med(VertexShaderOutput input, bool ShadowPass, sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler, float3 ambientColor) : SV_Target0
{
    float4 textureColor = tex2D(textureSampler, input.TextureCoordinate);
    float4 specularColor = tex2D(specularSampler, input.TextureCoordinate);
    
    clip(textureColor.a - 0.1f);
    
    float3 color = float3(1, 1, 1);
    
    float3 addLightColor = CalculateStaticLighting(input.WorldPos.xyz, input.Normal.xyz);
    color = float4(addLightColor, 1);
    
    float lightIntensity = dot(input.Normal.xyz, DiffuseLightDirection) * 0.5 + 0.5;
    color = float4(saturate(DiffuseColor * DiffuseIntensity * lightIntensity).rgb + color, 1);

    float4 Color0 = textureColor;
    float4 Color3 = float4(GetLightOutput(color.xyz, ambientColor.xyz).xyz, 1);
    
    Color3 = ApplyRealtimeLights(Color3.xyz, input.Normal.xyz, input.WorldPos.xyz);
    
    float4 finalColor = float4(ApplyLight(Color0.rgb, Color3.rgb), Color0.a);
    
    return float4(ApplyFog(finalColor + CalculateSpecularLighting(input.WorldPos.xyz, input.Normal.xyz, normalize(cameraPos - input.WorldPos.xyz), specularColor.r, specularColor.g), input.WorldPos.xyz).xyz, Color0.a);
}
float4 BasicModelPixelShader_Low(VertexShaderOutput input, bool ShadowPass, sampler2D textureSampler, sampler2D specularSampler, sampler2D normalSampler, float3 ambientColor) : SV_Target0
{
    float4 textureColor = tex2D(textureSampler, input.TextureCoordinate);
    
    clip(textureColor.a - 0.1f);
    
    float4 Color0 = textureColor;
    float4 Color1 = float4(input.Normal, 1);
    float4 Color2 = float4(input.WorldPos.xyzw);
    float4 Color3 = float4(GetLightOutput(input, ambientColor).xyz, 1);
    
    Color3 = ApplyRealtimeLights(Color3.xyz, Color1.xyz, Color2.xyz);
    
    float4 finalColor = float4(ApplyLight(Color0.rgb, Color3.rgb), Color0.a);
    
    return float4(ApplyFog(finalColor, input.WorldPos.xyz).xyz, Color0.a);
}
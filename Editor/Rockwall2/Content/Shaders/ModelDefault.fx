#if OPENGL
	#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

#include "ModelCommon.fxh"

texture MainTex;
sampler2D textureSampler = sampler_state
{
    Texture = (MainTex);
};
texture SpecTex;
sampler2D specularSampler = sampler_state
{
    Texture = (SpecTex);
};
texture NormalTex;
sampler2D normalSampler = sampler_state
{
    Texture = (NormalTex);
};


VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.Normal = normalize(mul(input.Normal, WorldInverseTranspose));
    output.Tangent = normalize(mul(input.Tangent, WorldInverseTranspose));
    output.Binormal = normalize(mul(input.Binormal, WorldInverseTranspose));
    
    output.Color = float4(1, 1, 1, 1);
    output.TextureCoordinate = input.TextureCoordinate;
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;

    return output;
}
VertexShaderOutput VertexShaderFunction_Low(VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.Normal = normalize(mul(input.Normal, WorldInverseTranspose));
    output.Tangent = normalize(mul(input.Tangent, WorldInverseTranspose));
    output.Binormal = normalize(mul(input.Binormal, WorldInverseTranspose));
    
    float3 addLightColor = CalculateStaticLighting(worldPosition.xyz, output.Normal.xyz);
    
    output.Color = float4(addLightColor, 1);
    
    float lightIntensity = dot(output.Normal.xyz, DiffuseLightDirection) * 0.5 + 0.5;

    output.Color = float4(max(DiffuseColor * DiffuseIntensity * lightIntensity, 0).rgb + output.Color.rgb, 1);

    output.TextureCoordinate = input.TextureCoordinate;
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;

    return output;
}

float4 PixelShaderFunction(VertexShaderOutput input) : SV_Target0
{
    float4 color = BasicModelPixelShader(input, ShadowPass, textureSampler, specularSampler, normalSampler, AmbientColor.xyz * AmbientIntensity);
    return color;
}
float4 PixelShaderFunction_Med(VertexShaderOutput input) : SV_Target0
{
    float4 color = BasicModelPixelShader_Med(input, ShadowPass, textureSampler, specularSampler, normalSampler, AmbientColor.xyz * AmbientIntensity);
    return color;
}
float4 PixelShaderFunction_Low(VertexShaderOutput input) : SV_Target0
{
    float4 color = BasicModelPixelShader_Low(input, ShadowPass, textureSampler, specularSampler, normalSampler, AmbientColor.xyz * AmbientIntensity);
    return color;
}

technique High
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction();
    }
}
technique Med
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_Med();
    }
}
technique Low
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction_Low();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction_Low();
    }
}
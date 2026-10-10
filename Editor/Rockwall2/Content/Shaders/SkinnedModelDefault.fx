#if OPENGL
	#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

#include "ModelCommon.fxh"

#define MAXBONES 200

float4x3 Bones[MAXBONES];

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

struct VSInputNmTxWeights
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL;
    float3 Tangent : TANGENT;
    float3 Binormal : BINORMAL;
    float2 TexCoord : TEXCOORD0;
    int4 Indices : BLENDINDICES0;
    float4 Weights : BLENDWEIGHT0;
};

void Skin(inout VSInputNmTxWeights vin, uniform int boneCount)
{
    float4x3 skinning = 0;

    [unroll]
    for (int i = 0; i < boneCount; i++)
    {
        skinning += Bones[vin.Indices[i]] * vin.Weights[i];
    }

    vin.Position.xyz = mul(vin.Position, skinning);
    vin.Normal = mul(vin.Normal, (float3x3) skinning);
}

VertexShaderOutput MainVS(in VSInputNmTxWeights input)
{
    VertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.Normal = normalize(mul(float4(input.Normal, 1), WorldInverseTranspose));
    output.Tangent = normalize(mul(float4(input.Tangent, 1), WorldInverseTranspose));
    output.Binormal = normalize(mul(float4(input.Binormal, 1), WorldInverseTranspose));
    output.Color = float4(0, 0, 0, 1);
    output.TextureCoordinate = input.TexCoord;
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;

    return output;
}
VertexShaderOutput MainVS_Low(in VSInputNmTxWeights input)
{
    VertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.Normal = normalize(mul(float4(input.Normal, 1), WorldInverseTranspose));
    output.Tangent = normalize(mul(float4(input.Tangent, 1), WorldInverseTranspose));
    output.Binormal = normalize(mul(float4(input.Binormal, 1), WorldInverseTranspose));
    
    float3 addLightColor = CalculateStaticLighting(worldPosition.xyz, output.Normal.xyz);
    
    output.Color = float4(addLightColor, 1);
    
    float lightIntensity = dot(output.Normal.xyz, DiffuseLightDirection) * 0.5 + 0.5;

    output.Color = float4(max(DiffuseColor * DiffuseIntensity * lightIntensity, 0).rgb + output.Color.rgb, 1);

    output.TextureCoordinate = input.TexCoord;
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;

    return output;
}

VertexShaderOutput VSFourBone(in VSInputNmTxWeights input)
{
    Skin(input, 4);
    
    VertexShaderOutput output = MainVS(input);
    
    return output;
}

VertexShaderOutput VSFourBone_Low(in VSInputNmTxWeights input)
{
    Skin(input, 4);
    
    VertexShaderOutput output = MainVS_Low(input);
    
    return output;
}

float4 MainPS(VertexShaderOutput input) : SV_Target0
{
    return BasicModelPixelShader(input, ShadowPass, textureSampler, specularSampler, normalSampler, AmbientColor.xyz * AmbientIntensity);
}
float4 MainPS_Med(VertexShaderOutput input) : SV_Target0
{
    return BasicModelPixelShader_Med(input, ShadowPass, textureSampler, specularSampler, normalSampler, AmbientColor.xyz * AmbientIntensity);
}
float4 MainPS_Low(VertexShaderOutput input) : SV_Target0
{
    return BasicModelPixelShader_Low(input, ShadowPass, textureSampler, specularSampler, normalSampler, AmbientColor.xyz * AmbientIntensity);
}


technique High
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VSFourBone();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
technique Med
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VSFourBone();
        PixelShader = compile PS_SHADERMODEL MainPS_Med();
    }
};
technique Low
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VSFourBone_Low();
        PixelShader = compile PS_SHADERMODEL MainPS_Low();
    }
};
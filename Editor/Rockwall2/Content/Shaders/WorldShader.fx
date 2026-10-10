#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#include "Common.fxh"

matrix World;
matrix View;
matrix Projection;

float faceAlpha;
float3 faceTint;
float faceSelected;

texture faceTexture;

sampler2D faceSampler = sampler_state
{
    Texture = (faceTexture);
};

struct WVertexShaderInput
{
	float4 Position : POSITION0;
    float4 Normal : NORMAL0;
    float2 TexCoords : TEXCOORD0;
};

struct WVertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 ViewPos : TEXCOORD1;
    float4 WorldPos : TEXCOORD2;
    float2 TexCoords : TEXCOORD0;
	float4 Color : COLOR0;
};

WVertexShaderOutput MainVS(in WVertexShaderInput input)
{
	WVertexShaderOutput output = (WVertexShaderOutput)0;

	output.WorldPos = mul(input.Position, World);
	output.ViewPos = mul(output.WorldPos, View);
	output.Position = mul(output.ViewPos, Projection);
    output.Color = float4(dot(input.Normal.xyz, normalize(float3(2, 1, 1))) * 0.25 + 1, 1, 1, faceAlpha);
    output.TexCoords = input.TexCoords;

	return output;
}

float4 MainPS(WVertexShaderOutput input) : COLOR
{
    float4 tex = tex2D(faceSampler, input.TexCoords);
	float distToCam = max(pow(1.0/(length(input.ViewPos)*0.008+1),2),0.3);
	float4 baseCol = float4(lerp(tex.xyz * input.Color.r * distToCam * (faceTint*0.5+0.5), faceTint, faceSelected * 0.1f), tex.a * input.Color.a);

    return ApplyFog(baseCol,input.WorldPos);
}

technique BasicColorDrawing
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
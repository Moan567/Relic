#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

matrix WorldViewProjection;

float3 tint;
bool selected;

texture texture1;
sampler2D texture1Sampler = sampler_state
{
    Texture = (texture1);
};
texture texture2;
sampler2D texture2Sampler = sampler_state
{
    Texture = (texture2);
};

struct VertexShaderInput
{
	float4 Position : POSITION0;
    float4 Normal : NORMAL0;
    float3 TexCoord : TEXCOORD0;
    float4 LightmapCoord : TEXCOORD1;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD0;
	float4 Color : COLOR0;
};

VertexShaderOutput MainVS(in VertexShaderInput input)
{
	VertexShaderOutput output = (VertexShaderOutput)0;
	
    output.Position = mul(input.Position, WorldViewProjection);
    output.Normal = mul(input.Normal, WorldViewProjection);
    output.Color = float4(dot(input.Normal.xyz, normalize(float3(2, 1, 1)))*0.5+0.5, 1, 1, 1 - input.TexCoord.z);
    output.TexCoord = input.TexCoord;

	return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 texSource = tex2D(texture1Sampler, input.TexCoord);
    float4 texBlend = tex2D(texture2Sampler, input.TexCoord);
	
    float4 tex = lerp(texSource, texBlend, input.Color.a);
	
    return float4(lerp(tex.xyz * input.Color.r * (tint * 0.5 + 0.5), tint, selected * 0.1f), 1);
}

technique BasicColorDrawing
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
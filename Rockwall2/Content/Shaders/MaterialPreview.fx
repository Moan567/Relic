// Material preview shader for the Rockwall2 Material Editor.
//
// The editor preview previously used MonoGame's BasicEffect, which can only do
// albedo + one diffuse light -- Reflectivity had no visible effect at all, because
// BasicEffect has no specular exponent and no environment sampling.
//
// This adds:
//   * a Blinn-Phong highlight whose tightness is the engine's "shine" parameter,
//   * an environment reflection from a procedural cubemap, scaled by Reflectivity,
//     fresnel-weighted so grazing angles mirror and face-on angles stay matte.

#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 World;
float4x4 View;
float4x4 Projection;

float3 cameraPos;

// Direction the light travels (not the direction toward the light).
float3 LightDir;
float3 LightColor;
float3 AmbientColor;

// 0 = fully matte, 1 = mirror. This is the material's Reflectivity field.
float Reflectivity;

// Specular tightness. Matches the engine's "shine" parameter, which is an exponent
// rather than a strength -- see ModelCommon.fxh's Specular().
float Shine;

float3 Tint;

texture AlbedoTex;
sampler2D AlbedoSampler = sampler_state
{
	Texture = (AlbedoTex);
};

// MGFX has no `samplerCube` keyword -- a cube texture binds to a plain `sampler`
// and is read with texCUBE(). (HLSL's samplerCube fails to parse here.)
TextureCube EnvMap;
sampler EnvSampler = sampler_state
{
	Texture = (EnvMap);
};

struct VertexShaderInput
{
	float4 Position : POSITION0;
	float3 Normal : NORMAL0;
	float2 TexCoord : TEXCOORD0;
};

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float3 Normal : TEXCOORD0;
	float2 TexCoord : TEXCOORD1;
	float3 WorldPos : TEXCOORD2;
};

VertexShaderOutput MainVS(VertexShaderInput input)
{
	VertexShaderOutput output;
	output.WorldPos = mul(input.Position, World).xyz;

	// Our World matrices carry uniform scale only, so the upper 3x3 doubles as the
	// normal matrix and we can skip a full inverse-transpose.
	output.Normal = mul((float3x3)World, input.Normal).xyz;

	output.Position = mul(mul(input.Position, World), mul(View, Projection));
	output.TexCoord = input.TexCoord;

	return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
	float3 N = normalize(input.Normal);
	float3 V = normalize(cameraPos - input.WorldPos);
	float3 L = normalize(-LightDir);

	float4 albedo = tex2D(AlbedoSampler, input.TexCoord);

	float3 diffuse = LightColor * max(dot(N, L), 0.0);

	// Blinn-Phong. The +1 keeps Shine=0 from degenerating into pow(x, 0) == 1,
	// which would flood the surface with a flat highlight.
	float3 H = normalize(L + V);
	float specular = pow(max(dot(N, H), 0.0), max(Shine, 0.0) * 64.0 + 1.0) * Reflectivity;

	// Environment reflection, fresnel-weighted: mirrors at grazing angles,
	// falls back to plain albedo when looking straight at the surface.
	float3 env = texCUBE(EnvSampler, reflect(-V, N)).rgb;
	float fresnel = pow(1.0 - saturate(dot(N, V)), 4.0);
	float envStrength = Reflectivity * (0.15 + 0.85 * fresnel);

	float3 lit = albedo.rgb * (diffuse + AmbientColor)
		+ LightColor * specular
		+ env * envStrength;

	return float4(lit * Tint, 1);
}

technique MaterialPreview
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};


// --- environment cubemap generation -------------------------------------------------
// Renders a gradient sky into a RenderTargetCube. The direction is taken from the
// raw cube vertex position, which is exactly the direction a cubemap face expects.

struct SkyVertexInput
{
	float4 Position : POSITION0;
};

struct SkyVertexOutput
{
	float4 Position : SV_POSITION;
	float3 Dir : TEXCOORD0;
};

SkyVertexOutput SkyVS(SkyVertexInput input)
{
	SkyVertexOutput output;
	output.Position = input.Position;
	output.Dir = input.Position.xyz;
	return output;
}

float4 SkyPS(SkyVertexOutput input) : COLOR
{
	float3 d = normalize(input.Dir);

	// Straight up is bright, straight down is dark; the sides blend between.
	float t = saturate(d.y);
	float3 sky = lerp(float3(0.30, 0.34, 0.40), float3(0.62, 0.75, 0.98), t);
	float3 ground = lerp(float3(0.10, 0.09, 0.09), sky, saturate(t * 6.0));

	// A soft warm patch gives the reflection something recognisable to catch,
	// which is the whole point of being able to see it.
	float sun = saturate(dot(d, normalize(float3(0.4, 0.75, 0.5))));
	ground += float3(1.0, 0.85, 0.6) * pow(sun, 24.0) * 1.6;

	return float4(ground, 1);
}

technique SkyGeneration
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL SkyVS();
		PixelShader = compile PS_SHADERMODEL SkyPS();
	}
};
#if OPENGL
	#define SV_POSITION POSITION
#endif

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

#include "ModelCommon.fxh"

#define MAXBONES 200
#define MAXEYES 4

float4x3 Bones[MAXBONES];

float4 EyeCenterRadius[MAXEYES]; // xyz = eye center (world), w = eyeball radius
float3 EyeForward[MAXEYES];
float3 EyeRight[MAXEYES];
float3 EyeUp[MAXEYES];
float ThetaFOV[MAXEYES];

static const float IrisPlaneDepthRatio = 0.9;
static const float LimbusBlendRatio = 0.08;

// Refractive index ratio air -> cornea (~1.336)
static const float CorneaEta = 1.0 / 1.336;

const float FresnelPower = 2;
const float FresnelIntensity = 1;
float IrisSize;

texture MainTex;
sampler2D textureSampler = sampler_state
{
    Texture = (MainTex);
};
texture DataTex;
sampler2D dataSampler = sampler_state
{
    Texture = (DataTex);
    MagFilter = Linear;
    MinFilter = Linear;
};

struct VSInputNmTxWeights
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL;
    float3 Tangent : TANGENT;
    float3 Binormal : BINORMAL;
    float2 TexCoord : TEXCOORD0;
    int4 Indices : BLENDINDICES0;
    int EyeIndex : BLENDINDICES1;
    float4 Weights : BLENDWEIGHT0;
};

struct EyeVertexShaderOutput
{
    float4 Position : POSITION0;
    float4 WorldPos : COLOR1;
    float4 Color : COLOR0;
    float3 Normal : TEXCOORD1;
    float3 Tangent : TEXCOORD2;
    float3 Binormal : TEXCOORD3;
    int EyeIndex : TEXCOORD0;
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

EyeVertexShaderOutput MainVS_Low(in VSInputNmTxWeights input)
{
    EyeVertexShaderOutput output;

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

    output.EyeIndex = input.EyeIndex;
    output.WorldPos = worldPosition;
    output.WorldPos.w = output.Position.w;

    return output;
}
EyeVertexShaderOutput VSFourBone_Low(in VSInputNmTxWeights input)
{
    Skin(input, 4);

    EyeVertexShaderOutput output = MainVS_Low(input);

    return output;
}

float4 MainPS(EyeVertexShaderOutput input) : SV_Target0
{
    float3 eyeCenter = lerp(EyeCenterRadius[0].xyz, EyeCenterRadius[1].xyz, input.EyeIndex);
    float eyeRadius = lerp(EyeCenterRadius[0].w, EyeCenterRadius[1].w, input.EyeIndex);
    float3 eyeForward = normalize(lerp(EyeForward[0], EyeForward[1], input.EyeIndex));
    float3 eyeRight = normalize(lerp(EyeRight[0], EyeRight[1], input.EyeIndex));
    float3 eyeUp = normalize(lerp(EyeUp[0], EyeUp[1], input.EyeIndex));
    float thetaFOV = lerp(ThetaFOV[0], ThetaFOV[1], input.EyeIndex);
    
    float corneaLimbusAngle = atan(IrisSize * tan(thetaFOV));
    float limbusBlend = corneaLimbusAngle * LimbusBlendRatio;
    float CorneaRadiusRatio = IrisSize+0.1f;
    float corneaRadius = eyeRadius * CorneaRadiusRatio;
    float irisPlaneDepth = eyeRadius * IrisPlaneDepthRatio;
    float irisPhysicalRadius = irisPlaneDepth * tan(corneaLimbusAngle);

    // Surface normal at this fragment, purely geometric, no view-dependence yet.
    float3 eyeNormal = normalize(input.WorldPos.xyz - eyeCenter);

    // View ray from camera to surface: everything view-dependent (refraction, Fresnel) is built off this.
    float3 V = normalize(input.WorldPos.xyz - cameraPos);

    // Whole-eye gnomonic projection, used for the sclera/data texture 
    // tan(theta) diverges as theta approaches 90 degrees, so projScale rescales things such that
    // ThetaFOV (the angular radius of the visible eye opening) maps exactly to the UV edge.
    float projScale = 1.0 / tan(thetaFOV);
    float denom = max(dot(eyeNormal, eyeForward), 0.0001);
    float x = dot(eyeNormal, eyeRight) * projScale / denom;
    float y = dot(eyeNormal, eyeUp) * projScale / denom;
    float2 eyeTexUV = float2(x, y) * 0.5 + 0.5;
    eyeTexUV.y = 1 - eyeTexUV.y;

    float4 eyeData = tex2D(dataSampler, eyeTexUV);

    float2 bumpXY = eyeData.rg * 2.0 - 1.0;
    float bumpZ = sqrt(saturate(1.0 - dot(bumpXY, bumpXY)));
    float3 tangentBump = float3(bumpXY, bumpZ);
    
    float corneaHeight = eyeData.b;
    float specularMask = eyeData.a;

    //x Limbus boundary: how far into the iris/cornea region this fragment is
    float theta = acos(saturate(dot(eyeNormal, eyeForward)));
    float irisMask = 1.0 - smoothstep(corneaLimbusAngle - limbusBlend, corneaLimbusAngle, theta);

    // A smaller-radius sphere reaches the same angular opening faster than the eyeball itself,
    // so the same physical point maps to a larger angle on the cornea than on the sclera.
    float sinTheta = sin(theta);
    float corneaCurvatureRatio = eyeRadius / corneaRadius;
    float thetaCornea = asin(saturate(sinTheta * corneaCurvatureRatio));

    float cosPhi = dot(eyeNormal, eyeRight) / max(sinTheta, 0.0001);
    float sinPhi = dot(eyeNormal, eyeUp) / max(sinTheta, 0.0001);

    float3 corneaNormal = eyeForward * cos(thetaCornea)
                        + (eyeRight * cosPhi + eyeUp * sinPhi) * sin(thetaCornea);
    
    float3 perturbedNormal = normalize(corneaNormal + tangentBump.x * eyeRight + tangentBump.y * eyeUp);

    // This is what makes the iris look like a flat disc seen through curved glass, rather than
    // a texture wrapped onto the sphere: the UV now depends on view angle, not just surface position.
    float3 T = refract(V, corneaNormal, CorneaEta);

    float3 irisPlanePoint = eyeCenter + eyeForward * irisPlaneDepth;
    float denomPlane = dot(T, eyeForward);
    float tHit = dot(irisPlanePoint - input.WorldPos.xyz, eyeForward) / denomPlane;
    float3 hitPoint = input.WorldPos.xyz + T * tHit;

    float3 localHit = hitPoint - irisPlanePoint;
    float2 irisUV = float2(dot(localHit, eyeRight), dot(localHit, eyeUp)) / irisPhysicalRadius;
    irisUV = irisUV * (IrisSize * 0.5) + 0.5;
    irisUV.y = 1 - irisUV.y;

    // Blend between the refracted iris lookup and the plain sclera lookup at the limbus.
    float2 finalUV = lerp(eyeTexUV, irisUV, irisMask);
    float4 color = tex2D(textureSampler, finalUV);

    // Specular glint and Fresnel rim
    float glint = CalculateSpecularLighting(input.WorldPos.xyz, perturbedNormal, -V, 1, 1) * specularMask;
    color.rgb += glint;
    
    glint = CalculateSpecularLighting(input.WorldPos.xyz, corneaNormal, -V, 1, 1) * specularMask;
    color.rgb += glint;

    float fresnel = pow(1.0 - saturate(dot(perturbedNormal, -V)), FresnelPower) * irisMask;
    color.rgb += fresnel * FresnelIntensity;

    color = ApplyFog(color, input.WorldPos.xyz);

    float4 tint = input.Color;
    tint = ApplyRealtimeLights(tint.xyz, input.Normal.xyz, input.WorldPos.xyz);

    return float4(ApplyLight(color.xyz, tint.xyz), 1);
}

technique High
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VSFourBone_Low();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
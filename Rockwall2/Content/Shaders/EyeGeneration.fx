#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

float Seed;
float4 ScleraColor;
float4 VesselColor;
float4 IrisColor;
float4 IrisRimColor;
float IrisSize;
float4 PupilColor;
float PupilBaseSize;
float PupilFeather;
float FiberDensity;
float VeinDensity;
float VeinDistortion;
float4 IrisFleckColor;
float IrisFleckAmount;
float NormalStrength;
float CorneaBulgeStrength;
float ScleraSpecular;
float IrisSpecular;
float TexelSize;

struct VertexOut
{
    float4 Position : Position;
    float2 UV : TEXCOORD0;
};

struct PixelOut
{
    float4 Color : COLOR0;
    float4 Data : COLOR1;
};

VertexOut VSMain(float4 pos : POSITION, float2 uv : TEXCOORD0)
{
    VertexOut p;
    p.Position = pos;
    p.UV = uv;
    return p;
}

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float Noise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = Hash21(i);
    float b = Hash21(i + float2(1, 0));
    float c = Hash21(i + float2(0, 1));
    float d = Hash21(i + float2(1, 1));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float2 Warp(float2 coord, float2 seedOffset, float strength, float2 offset)
{
    float2 warp = float2(
        Noise2D(coord * 3.0 + seedOffset + offset) - 0.5,
        Noise2D(coord * 3.0 + seedOffset + offset + float2(50, 17)) - 0.5
    );
    return warp * strength;
}

float HeightField(float2 uv)
{
    float2 centered = uv - 0.5;
    float radius = length(centered) * 2.0;
    float2 dir = radius > 0.0001 ? centered / (radius * 0.5) : float2(1, 0);
    float2 seedOffset = float2(Seed * 17.31, Seed * 91.7);

    float2 veinWarp = Warp(centered, seedOffset, VeinDistortion, float2(0, 0));
    float2 veinCoord = dir * VeinDensity + float2(radius * 1.5, 0) + seedOffset + veinWarp;
    float veinNoise = Noise2D(veinCoord);

    float2 fiberWarp = Warp(centered, seedOffset, VeinDistortion * 0.5, float2(200, 200));
    float2 fiberCoord = dir * FiberDensity + float2(radius * 3.0, 0) + seedOffset + float2(100, 100) + fiberWarp;
    float fiberNoise = Noise2D(fiberCoord);

    float irisMask = smoothstep(IrisSize, IrisSize - 0.01, radius);
    float scleraMask = 1.0 - irisMask;

    // Exclude the pupil entirely: no fiber bump inside it, just the smooth cornea baseline.
    float pupilMask = smoothstep(PupilBaseSize + PupilFeather, PupilBaseSize - PupilFeather, radius);
    irisMask *= (1.0 - pupilMask);

    float height = 0.5;
    height -= veinNoise * 0.15 * scleraMask;
    height += (fiberNoise - 0.5) * CorneaBulgeStrength * irisMask;

    return height;
}

PixelOut PSMain(float2 uv : TEXCOORD0)
{
    float2 centered = uv - 0.5;
    float radius = length(centered) * 2.0;
    float2 dir = radius > 0.0001 ? centered / (radius * 0.5) : float2(1, 0);

    float2 seedOffset = float2(Seed * 17.31, Seed * 91.7);

    float2 veinWarp = Warp(centered, seedOffset, VeinDistortion, float2(0, 0));
    float2 veinCoord = dir * VeinDensity + float2(radius * 1.5, 0) + seedOffset + veinWarp;
    float veinNoise = Noise2D(veinCoord);
    float veinMask = smoothstep(IrisSize, 1.0, radius) * saturate(veinNoise * 1.6 - 0.3);
    float4 scleraFinal = lerp(ScleraColor, VesselColor, veinMask);

    float2 fiberWarp = Warp(centered, seedOffset, VeinDistortion * 0.5, float2(200, 200));
    float2 fiberCoord = dir * FiberDensity + float2(radius * 3.0, 0) + seedOffset + float2(100, 100) + fiberWarp;
    float fiberNoise = Noise2D(fiberCoord);

    float colorVarNoise = Noise2D(fiberCoord * 0.5 + float2(300, 300));
    float irisT = saturate(radius / max(IrisSize, 0.0001) + (colorVarNoise - 0.5) * 0.3);
    float4 irisBase = lerp(IrisColor, IrisRimColor, irisT);
    irisBase.rgb *= lerp(0.7, 1.0, fiberNoise);

    float2 fleckCoord = dir * (FiberDensity * 3.0) + float2(radius * 6.0, 0) + seedOffset + float2(700, 700);
    float fleckNoise = Noise2D(fleckCoord);
    float fleckMask = smoothstep(1.0 - IrisFleckAmount, 1.0, fleckNoise);
    irisBase.rgb = lerp(irisBase.rgb, IrisFleckColor.rgb, fleckMask);

    float limbusDarken = smoothstep(IrisSize * 0.82, IrisSize, radius);
    irisBase.rgb *= lerp(1.0, 0.4, limbusDarken);

    float irisEdge = smoothstep(IrisSize, IrisSize - 0.01, radius);
    float4 withIris = lerp(scleraFinal, irisBase, irisEdge);

    float pupilEdge = smoothstep(PupilBaseSize + PupilFeather, PupilBaseSize - PupilFeather, radius);

    PixelOut output;
    output.Color = lerp(withIris, PupilColor, pupilEdge);

    float hL = HeightField(uv - float2(TexelSize, 0));
    float hR = HeightField(uv + float2(TexelSize, 0));
    float hD = HeightField(uv - float2(0, TexelSize));
    float hU = HeightField(uv + float2(0, TexelSize));

    float3 normal = normalize(float3((hL - hR) * NormalStrength, (hD - hU) * NormalStrength, 1.0));

    float corneaHeight = saturate(1.0 - radius / max(IrisSize, 0.0001)) * CorneaBulgeStrength * irisEdge;
    float specular = lerp(ScleraSpecular, IrisSpecular, irisEdge);

    output.Data = float4(normal.xy * 0.5 + 0.5, corneaHeight, specular);

    return output;
}

technique EyeGen
{
    pass Main
    {
        VertexShader = compile VS_SHADERMODEL VSMain();
        PixelShader = compile PS_SHADERMODEL PSMain();
    }
}
uniform float Seed;
uniform vec4 ScleraColor;
uniform vec4 VesselColor;
uniform vec4 IrisColor;
uniform vec4 IrisRimColor;
uniform float IrisSize;
uniform vec4 PupilColor;
uniform float PupilBaseSize;
uniform float PupilFeather;
uniform float FiberDensity;
uniform float VeinDensity;
uniform float VeinDistortion;
uniform vec4 IrisFleckColor;
uniform float IrisFleckAmount;
uniform float NormalStrength;
uniform float CorneaBulgeStrength;
uniform float ScleraSpecular;
uniform float IrisSpecular;
uniform float TexelSize;

varying vec2 UV;

float Hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float Noise2D(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = Hash21(i);
    float b = Hash21(i + vec2(1.0, 0.0));
    float c = Hash21(i + vec2(0.0, 1.0));
    float d = Hash21(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

vec2 Warp(vec2 coord, vec2 seedOffset, float strength, vec2 offset)
{
    vec2 warp = vec2(
        Noise2D(coord * 3.0 + seedOffset + offset) - 0.5,
        Noise2D(coord * 3.0 + seedOffset + offset + vec2(50.0, 17.0)) - 0.5
    );
    return warp * strength;
}

float HeightField(vec2 uv)
{
    vec2 centered = uv - 0.5;
    float radius = length(centered) * 2.0;
    vec2 dir = radius > 0.0001 ? centered / (radius * 0.5) : vec2(1.0, 0.0);
    vec2 seedOffset = vec2(Seed * 17.31, Seed * 91.7);

    vec2 veinWarp = Warp(centered, seedOffset, VeinDistortion, vec2(0.0, 0.0));
    vec2 veinCoord = dir * VeinDensity + vec2(radius * 1.5, 0.0) + seedOffset + veinWarp;
    float veinNoise = Noise2D(veinCoord);

    vec2 fiberWarp = Warp(centered, seedOffset, VeinDistortion * 0.5, vec2(200.0, 200.0));
    vec2 fiberCoord = dir * FiberDensity + vec2(radius * 3.0, 0.0) + seedOffset + vec2(100.0, 100.0) + fiberWarp;
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

void main()
{
    vec2 centered = UV - 0.5;
    float radius = length(centered) * 2.0;
    vec2 dir = radius > 0.0001 ? centered / (radius * 0.5) : vec2(1.0, 0.0);

    vec2 seedOffset = vec2(Seed * 17.31, Seed * 91.7);

    vec2 veinWarp = Warp(centered, seedOffset, VeinDistortion, vec2(0.0, 0.0));
    vec2 veinCoord = dir * VeinDensity + vec2(radius * 1.5, 0.0) + seedOffset + veinWarp;
    float veinNoise = Noise2D(veinCoord);
    float veinMask = smoothstep(IrisSize, 1.0, radius) * clamp(veinNoise * 1.6 - 0.3, 0.0, 1.0);
    vec4 scleraFinal = mix(ScleraColor, VesselColor, veinMask);

    vec2 fiberWarp = Warp(centered, seedOffset, VeinDistortion * 0.5, vec2(200.0, 200.0));
    vec2 fiberCoord = dir * FiberDensity + vec2(radius * 3.0, 0.0) + seedOffset + vec2(100.0, 100.0) + fiberWarp;
    float fiberNoise = Noise2D(fiberCoord);

    float colorVarNoise = Noise2D(fiberCoord * 0.5 + vec2(300.0, 300.0));
    float irisT = clamp(radius / max(IrisSize, 0.0001) + (colorVarNoise - 0.5) * 0.3, 0.0, 1.0);
    vec4 irisBase = mix(IrisColor, IrisRimColor, irisT);
    irisBase.rgb *= mix(0.7, 1.0, fiberNoise);

    vec2 fleckCoord = dir * (FiberDensity * 3.0) + vec2(radius * 6.0, 0.0) + seedOffset + vec2(700.0, 700.0);
    float fleckNoise = Noise2D(fleckCoord);
    float fleckMask = smoothstep(1.0 - IrisFleckAmount, 1.0, fleckNoise);
    irisBase.rgb = mix(irisBase.rgb, IrisFleckColor.rgb, fleckMask);

    float limbusDarken = smoothstep(IrisSize * 0.82, IrisSize, radius);
    irisBase.rgb *= mix(1.0, 0.4, limbusDarken);

    float irisEdge = smoothstep(IrisSize, IrisSize - 0.01, radius);
    vec4 withIris = mix(scleraFinal, irisBase, irisEdge);

    float pupilEdge = smoothstep(PupilBaseSize + PupilFeather, PupilBaseSize - PupilFeather, radius);

    vec4 outColor = mix(withIris, PupilColor, pupilEdge);

    float hL = HeightField(UV - vec2(TexelSize, 0.0));
    float hR = HeightField(UV + vec2(TexelSize, 0.0));
    float hD = HeightField(UV - vec2(0.0, TexelSize));
    float hU = HeightField(UV + vec2(0.0, TexelSize));

    vec3 normal = normalize(vec3((hL - hR) * NormalStrength, (hD - hU) * NormalStrength, 1.0));

    float corneaHeight = clamp(1.0 - radius / max(IrisSize, 0.0001), 0.0, 1.0) * CorneaBulgeStrength * irisEdge;
    float specular = mix(ScleraSpecular, IrisSpecular, irisEdge);

    vec4 outData = vec4(normal.xy * 0.5 + 0.5, corneaHeight, specular);

    gl_FragData[0] = outColor;
    gl_FragData[1] = outData;
}

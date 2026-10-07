#include <ModelCommon.glsl>

#define MAXEYES 4

uniform vec4 EyeCenterRadius[4]; // xyz = eye center (world), w = eyeball radius
uniform vec3 EyeForward[4];
uniform vec3 EyeRight[4];
uniform vec3 EyeUp[4];
uniform float ThetaFOV[4];

const float IrisPlaneDepthRatio = 0.9;
const float LimbusBlendRatio = 0.08;

// Refractive index ratio air -> cornea (~1.336)
const float CorneaEta = 1.0 / 1.336;

uniform float IrisSize;

uniform sampler2D MainTex;
uniform sampler2D DataTex;

varying vec4 WorldPos;
varying float EyeIndex;

void main()
{
    vec3 eyeCenter = mix(EyeCenterRadius[0].xyz, EyeCenterRadius[1].xyz, EyeIndex);
    float eyeRadius = mix(EyeCenterRadius[0].w, EyeCenterRadius[1].w, EyeIndex);

    if (eyeRadius <= 0.0001)
    {
        gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec3 eyeForward = normalize(mix(EyeForward[0], EyeForward[1], EyeIndex));
    vec3 eyeRight = normalize(mix(EyeRight[0], EyeRight[1], EyeIndex));
    vec3 eyeUp = normalize(mix(EyeUp[0], EyeUp[1], EyeIndex));
    float thetaFOV = mix(ThetaFOV[0], ThetaFOV[1], EyeIndex);

    float corneaLimbusAngle = atan(IrisSize * tan(thetaFOV));
    float limbusBlend = corneaLimbusAngle * LimbusBlendRatio;
    float CorneaRadiusRatio = IrisSize + 0.1;
    float corneaRadius = eyeRadius * CorneaRadiusRatio;
    float irisPlaneDepth = eyeRadius * IrisPlaneDepthRatio;
    float irisPhysicalRadius = irisPlaneDepth * tan(corneaLimbusAngle);

    // Surface normal at this fragment, purely geometric, no view-dependence yet.
    vec3 eyeNormal = normalize(WorldPos.xyz - eyeCenter);

    // View ray from camera to surface: everything view-dependent (refraction, Fresnel) is built off this.
    vec3 V = normalize(WorldPos.xyz - cameraPos);

    // Whole-eye gnomonic projection, used for the sclera/data texture.
    // tan(theta) diverges as theta approaches 90 degrees, so projScale rescales things such that
    // ThetaFOV (the angular radius of the visible eye opening) maps exactly to the UV edge.
    float projScale = 1.0 / tan(thetaFOV);
    float denom = max(dot(eyeNormal, eyeForward), 0.0001);
    float x = dot(eyeNormal, eyeRight) * projScale / denom;
    float y = dot(eyeNormal, eyeUp) * projScale / denom;
    vec2 eyeTexUV = vec2(x, y) * 0.5 + 0.5;
    eyeTexUV.y = 1.0 - eyeTexUV.y;

    // Limbus boundary: how far into the iris/cornea region this fragment is.
    float theta = acos(clamp(dot(eyeNormal, eyeForward), 0.0, 1.0));
    float irisMask = 1.0 - smoothstep(corneaLimbusAngle - limbusBlend, corneaLimbusAngle, theta);

    // A smaller-radius sphere reaches the same angular opening faster than the eyeball itself,
    // so the same physical point maps to a larger angle on the cornea than on the sclera.
    float sinTheta = sin(theta);
    float corneaCurvatureRatio = eyeRadius / corneaRadius;
    float thetaCornea = asin(clamp(sinTheta * corneaCurvatureRatio, 0.0, 1.0));

    float cosPhi = dot(eyeNormal, eyeRight) / max(sinTheta, 0.0001);
    float sinPhi = dot(eyeNormal, eyeUp) / max(sinTheta, 0.0001);

    vec3 corneaNormal = eyeForward * cos(thetaCornea)
                        + (eyeRight * cosPhi + eyeUp * sinPhi) * sin(thetaCornea);

    // Refract the view ray through the cornea (Snell's law) and hit the flat iris plane.
    // This is what makes the iris look like a flat disc seen through curved glass, rather than
    // a texture wrapped onto the sphere: the UV now depends on view angle, not just surface position.
    vec3 T = refract(V, corneaNormal, CorneaEta);

    // refract() returns exactly (0,0,0) on total internal reflection, and any
    // near-degenerate ray/plane angle blows tHit up toward Inf/NaN once divided
    // through. That used to be invisible when the eye had no real lighting
    // multiplying it, now it shows up as scattered garbage pixels. Detect it
    // and fall back to the plain sclera projection for that fragment.
    vec3 irisPlanePoint = eyeCenter + eyeForward * irisPlaneDepth;
    float denomPlane = dot(T, eyeForward);

    bool validRefraction = dot(T, T) > 0.0001 && abs(denomPlane) > 0.0001;

    float tHit = dot(irisPlanePoint - WorldPos.xyz, eyeForward) / (validRefraction ? denomPlane : 1.0);
    vec3 hitPoint = WorldPos.xyz + T * tHit;

    vec3 localHit = hitPoint - irisPlanePoint;
    vec2 irisUV = vec2(dot(localHit, eyeRight), dot(localHit, eyeUp)) / irisPhysicalRadius;
    irisUV = irisUV * (IrisSize * 0.5) + 0.5;
    irisUV.y = 1.0 - irisUV.y;

    float safeIrisMask = validRefraction ? irisMask : 0.0;

    // Blend between the refracted iris lookup and the plain sclera lookup at the limbus.
    vec2 finalUV = mix(eyeTexUV, irisUV, safeIrisMask);

    vec4 color = texture2D(MainTex, finalUV);
    vec4 eyeData = texture2D(DataTex, finalUV);

    vec2 bumpXY = eyeData.rg * 2.0 - 1.0;
    float bumpZ = sqrt(clamp(1.0 - dot(bumpXY, bumpXY), 0.0, 1.0));
    vec3 tangentBump = vec3(bumpXY, bumpZ);
    float specularMask = eyeData.a;

    // Base shading normal: plain sphere on the sclera, tighter corneal curvature on the iris,
    // then bump-perturbed by the baked fiber/vein detail either way.
    vec3 baseNormal = normalize(mix(eyeNormal, corneaNormal, irisMask));
    vec3 N = normalize(baseNormal + tangentBump.x * eyeRight + tangentBump.y * eyeUp);

    vec3 diffuse;
    vec3 specularBase = CalculateCombinedLighting(WorldPos.xyz, corneaNormal, -V, specularMask, 1.0, diffuse);

    vec3 diffuseUnused;
    vec3 specularDetail = CalculateCombinedLighting(WorldPos.xyz, N, -V, specularMask, 1.0, diffuseUnused);

    vec3 ambient = EvaluateSH(indirectSH, N);

    vec3 lit = ApplyLight(color.xyz, diffuse + ambient) + specularBase;

    gl_FragColor = vec4(ApplyFog(vec4(lit, color.a), WorldPos.xyz).rgb, 1.0);
}

#include <ModelCommon.glsl>
#include <PosFixup.glsl>

attribute vec4 in_Position0;
attribute vec4 in_Normal0;
attribute vec4 in_Tangent0;
attribute vec4 in_Binormal0;
attribute vec2 in_TextureCoordinate0;

varying vec4 ScreenPosition;
varying vec4 WorldPos;
varying vec4 Color;
varying vec3 Normal;
varying vec3 Tangent;
varying vec3 Binormal;
varying vec2 TextureCoordinate;

void main()
{
    vec4 worldPosition = in_Position0 * World;
    vec4 viewPosition = worldPosition * View;
    gl_Position = viewPosition * Projection;
    ScreenPosition = gl_Position;

    vec4 normal = vec4(normalize((in_Normal0 * WorldInverseTranspose).xyz), 0.0);
    Normal = normal.xyz;
    Tangent = normalize((in_Tangent0 * WorldInverseTranspose).xyz);
    Binormal = normalize((in_Binormal0 * WorldInverseTranspose).xyz);

    vec3 addLightColor = CalculateStaticLighting(worldPosition.xyz, normal.xyz);

    Color = vec4(addLightColor, 1.0);

    float lightIntensity = dot(normal.xyz, DiffuseLightDirection) * 0.5 + 0.5;

    Color = vec4(max(DiffuseColor.rgb * DiffuseIntensity * lightIntensity, 0.0) + Color.rgb, 1.0);

    vec3 ambientColor = EvaluateSH(indirectSH, normal.xyz);
    Color += vec4(ambientColor, 0.0);

    TextureCoordinate = in_TextureCoordinate0;
    WorldPos = worldPosition;
    WorldPos.w = gl_Position.w;

    ApplyPosFixup();
}

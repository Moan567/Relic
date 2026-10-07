// ported from Particles.fx's DecalPS (technique Decals)
#include <Common.glsl>

uniform sampler2D MainTex;
uniform sampler2D LightmapB1;
uniform sampler2D LightmapB2;
uniform sampler2D LightmapB3;
uniform bool UseVertColor;

varying vec4 Color;
varying vec2 UV;
varying vec2 LightmapUV;
varying vec4 WorldPos;
varying vec3 Normal;
varying vec3 Basis1;
varying vec3 Basis2;
varying vec3 Basis3;

void main()
{
    vec4 texColor = texture2D(MainTex, UV, -1.0);
    texColor.rgb = ConvertSRGB(texColor.rgb);

    if (!UseVertColor)
    {
        gl_FragColor = texColor;
        return;
    }

    vec3 lm1 = texture2D(LightmapB1, LightmapUV).rgb;
    vec3 lm2 = texture2D(LightmapB2, LightmapUV).rgb;
    vec3 lm3 = texture2D(LightmapB3, LightmapUV).rgb;

    vec3 lightColor = lm1 * dot(Basis1, Normal)
                     + lm2 * dot(Basis2, Normal)
                     + lm3 * dot(Basis3, Normal);

    lightColor = ApplyRealtimeLights(lightColor, Normal, WorldPos.xyz).rgb;

    gl_FragColor = vec4(ApplyLight(texColor.rgb, lightColor), texColor.a);
}

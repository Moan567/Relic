// ported from RTShadows.fx's PSShader (technique Textured)
//
// Three render targets (Color, Depth, LightOutput), matching the original's
// PS_OUTPUT{Color:SV_Target0, Depth:SV_Target1, LightOutput:SV_Target2} via
// gl_FragData.

uniform vec2 TextureSize;
uniform vec3 ShadowColor;

uniform sampler2D Texture;
uniform sampler2D BlobTex;

varying vec2 texcoord;
varying vec4 color;

float CalcShadowTermSoftPCF(vec2 vTexCoord)
{
    float shadowMapSize = 1.0 / TextureSize.x;

    float shadowIntensity = 0.0;
    shadowIntensity += texture2D(Texture, vTexCoord + vec2(-shadowMapSize, -shadowMapSize)).r;
    shadowIntensity += texture2D(Texture, vTexCoord + vec2(shadowMapSize, -shadowMapSize)).r;
    shadowIntensity += texture2D(Texture, vTexCoord + vec2(-shadowMapSize, shadowMapSize)).r;
    shadowIntensity += texture2D(Texture, vTexCoord + vec2(shadowMapSize, shadowMapSize)).r;
    shadowIntensity /= 4.0;
    return shadowIntensity;
}

void main()
{
    float texcolor = CalcShadowTermSoftPCF(texcoord * vec2(1.0, -1.0));
    vec4 blobcolor = texture2D(BlobTex, texcoord);
    float shadowDepth = 1.0 - color.w;
    float shadowIntensity = ((1.0 - texcolor) * (blobcolor.a * 0.5 + 0.5)) * shadowDepth;
    vec3 shadowColor = mix(ShadowColor, vec3(1.0, 1.0, 1.0), 1.0 - shadowIntensity);

    gl_FragData[0] = vec4(shadowColor, 1.0);
    gl_FragData[1] = vec4(1.0, 1.0, 1.0, 1.0);
    gl_FragData[2] = vec4(1.0, 1.0, 1.0, 1.0);
}

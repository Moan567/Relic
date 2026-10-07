// ported from LightGroupComposite.fx's MainPS (technique Composite)
//
// Three render targets (B1, B2, B3), matching the original's
// PSOutput{B1:SV_Target0, B2:SV_Target1, B3:SV_Target2} via gl_FragData.

uniform sampler2D Group0;
uniform sampler2D Group1;
uniform sampler2D Group2;
uniform sampler2D Group3;

// xyz = style color, w = style intensity multiplier. w<=0 means the slot is unused.
uniform vec4 Tint0;
uniform vec4 Tint1;
uniform vec4 Tint2;
uniform vec4 Tint3;

varying vec4 Color;
varying vec2 TexCoord;

void main()
{
    vec4 B1 = vec4(0.0, 0.0, 0.0, 1.0);
    vec4 B2 = vec4(0.0, 0.0, 0.0, 1.0);
    vec4 B3 = vec4(0.0, 0.0, 0.0, 1.0);

    vec3 g = texture2D(Group0, TexCoord).rgb;
    B1.rgb += g.r * Tint0.rgb * Tint0.w;
    B2.rgb += g.g * Tint0.rgb * Tint0.w;
    B3.rgb += g.b * Tint0.rgb * Tint0.w;

    g = texture2D(Group1, TexCoord).rgb;
    B1.rgb += g.r * Tint1.rgb * Tint1.w;
    B2.rgb += g.g * Tint1.rgb * Tint1.w;
    B3.rgb += g.b * Tint1.rgb * Tint1.w;

    g = texture2D(Group2, TexCoord).rgb;
    B1.rgb += g.r * Tint2.rgb * Tint2.w;
    B2.rgb += g.g * Tint2.rgb * Tint2.w;
    B3.rgb += g.b * Tint2.rgb * Tint2.w;

    g = texture2D(Group3, TexCoord).rgb;
    B1.rgb += g.r * Tint3.rgb * Tint3.w;
    B2.rgb += g.g * Tint3.rgb * Tint3.w;
    B3.rgb += g.b * Tint3.rgb * Tint3.w;

    gl_FragData[0] = B1;
    gl_FragData[1] = B2;
    gl_FragData[2] = B3;
}

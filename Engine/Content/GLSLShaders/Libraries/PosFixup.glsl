uniform vec4 posFixup;

void ApplyPosFixup()
{
    gl_Position.y = gl_Position.y * posFixup.y;
    gl_Position.xy += posFixup.zw * gl_Position.ww;
    gl_Position.z = gl_Position.z * 2.0 - gl_Position.w;
}
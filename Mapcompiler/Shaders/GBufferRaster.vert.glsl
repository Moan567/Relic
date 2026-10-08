#version 430
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inBasis1;
layout(location = 3) in vec3 inBasis2;
layout(location = 4) in vec3 inBasis3;
layout(location = 5) in vec2 inLightmapUV;
layout(location = 6) in int inSourceBrush;
layout(location = 7) in int inEntityGroup;

out vec3 vPosition; out vec3 vNormal; out vec3 vBasis1; out vec3 vBasis2; out vec3 vBasis3;
flat out int vSourceBrush; flat out int vEntityGroup;

void main()
{
    vPosition = inPosition; vNormal = inNormal;
    vBasis1 = inBasis1; vBasis2 = inBasis2; vBasis3 = inBasis3;
    vSourceBrush = inSourceBrush; vEntityGroup = inEntityGroup;
    gl_Position = vec4(inLightmapUV * 2.0 - 1.0, 0.0, 1.0);
}
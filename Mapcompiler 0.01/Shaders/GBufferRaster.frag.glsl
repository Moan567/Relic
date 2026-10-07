#version 430
in vec3 gPosition; in vec3 gNormal; in vec3 gBasis1; in vec3 gBasis2; in vec3 gBasis3;
flat in int gSourceBrush; flat in int gEntityGroup;

layout(location = 0) out vec4 outPosition;
layout(location = 1) out vec4 outNormal;
layout(location = 2) out vec4 outBasis1;
layout(location = 3) out vec4 outBasis2;
layout(location = 4) out vec4 outBasis3;
layout(location = 5) out int outSourceBrush;
layout(location = 6) out int outEntityGroup;

void main()
{
    outPosition = vec4(gPosition, 1.0);
    outNormal = vec4(normalize(gNormal), 0.0);
    outBasis1 = vec4(normalize(gBasis1), 0.0);
    outBasis2 = vec4(normalize(gBasis2), 0.0);
    outBasis3 = vec4(normalize(gBasis3), 0.0);
    outSourceBrush = gSourceBrush;
    outEntityGroup = gEntityGroup;
}
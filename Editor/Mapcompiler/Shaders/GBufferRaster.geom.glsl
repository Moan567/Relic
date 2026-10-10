#version 430
layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

uniform ivec2 targetResolution;

in vec3 vPosition[]; in vec3 vNormal[]; in vec3 vBasis1[]; in vec3 vBasis2[]; in vec3 vBasis3[];
flat in int vSourceBrush[]; flat in int vEntityGroup[];

out vec3 gPosition; out vec3 gNormal; out vec3 gBasis1; out vec3 gBasis2; out vec3 gBasis3;
flat out int gSourceBrush; flat out int gEntityGroup;

float PointSegmentDist(vec2 p, vec2 a, vec2 b, out vec2 closest)
{
    vec2 ab = b - a;
    float denom = max(dot(ab, ab), 1e-12);
    float t = clamp(dot(p - a, ab) / denom, 0.0, 1.0);
    closest = a + ab * t;
    return distance(p, closest);
}

void main()
{
    vec2 texelSize = 2.0 / vec2(targetResolution);
    float minTexel = min(texelSize.x, texelSize.y);

    vec2 p0 = gl_in[0].gl_Position.xy;
    vec2 p1 = gl_in[1].gl_Position.xy;
    vec2 p2 = gl_in[2].gl_Position.xy;
    vec2 positions[3] = vec2[3](p0, p1, p2);

    vec2 e0, e1, e2;
    float d0 = PointSegmentDist(p0, p1, p2, e0);
    float d1 = PointSegmentDist(p1, p2, p0, e1);
    float d2 = PointSegmentDist(p2, p0, p1, e2);

    float minAltitude = min(d0, min(d1, d2));

    if (minAltitude < minTexel)
    {
        float push = minTexel - minAltitude;

        vec2 dir0 = length(p0 - e0) > 1e-8 ? normalize(p0 - e0) : vec2(1.0, 0.0);
        vec2 dir1 = length(p1 - e1) > 1e-8 ? normalize(p1 - e1) : vec2(1.0, 0.0);
        vec2 dir2 = length(p2 - e2) > 1e-8 ? normalize(p2 - e2) : vec2(1.0, 0.0);

        positions[0] = p0 + dir0 * push * 0.5;
        positions[1] = p1 + dir1 * push * 0.5;
        positions[2] = p2 + dir2 * push * 0.5;
    }

    for (int i = 0; i < 3; i++)
    {
        gl_Position = vec4(positions[i], gl_in[i].gl_Position.zw);
        gPosition = vPosition[i]; gNormal = vNormal[i];
        gBasis1 = vBasis1[i]; gBasis2 = vBasis2[i]; gBasis3 = vBasis3[i];
        gSourceBrush = vSourceBrush[i]; gEntityGroup = vEntityGroup[i];
        EmitVertex();
    }
    EndPrimitive();
}
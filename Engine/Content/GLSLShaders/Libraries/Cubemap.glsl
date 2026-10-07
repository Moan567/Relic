uniform float cubemapSize;
uniform samplerCube cubemap;

uniform float cubemapBlendFactor;
uniform samplerCube cubemapBlend;

vec4 CubemapSample(vec3 dir)
{
    float M = max(max(abs(dir.x), abs(dir.y)), abs(dir.z));
    float scale = (cubemapSize - 1.0) / cubemapSize;

    vec3 notMax = vec3(
        abs(dir.x) != M ? 1.0 : 0.0,
        abs(dir.y) != M ? 1.0 : 0.0,
        abs(dir.z) != M ? 1.0 : 0.0);
    dir = mix(dir, dir * scale, notMax);

    return textureCube(cubemap, dir);
}

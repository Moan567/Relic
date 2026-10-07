layout(std430, binding = 27) readonly buffer TriAlbedoBuffer { float triAlbedo[]; };

vec3 GetTriAlbedo(uint triIdx)
{
    uint i = triIdx * 3u;
    return vec3(triAlbedo[i], triAlbedo[i + 1u], triAlbedo[i + 2u]);
}
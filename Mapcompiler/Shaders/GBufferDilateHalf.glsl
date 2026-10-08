#version 430
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba16f, binding = 0) uniform readonly image2D srcChannel;
layout(r32i, binding = 1) uniform readonly iimage2D srcMask;
layout(rgba16f, binding = 2) uniform writeonly image2D dstChannel;
const int OffsetsX[8] = int[8](-1, 0, 1, -1, 1, -1, 0, 1);
const int OffsetsY[8] = int[8](-1, -1, -1, 0, 0, 1, 1, 1);
void main()
{
    ivec2 texel = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(srcChannel);
    if (texel.x >= size.x || texel.y >= size.y) return;
    int mask = imageLoad(srcMask, texel).r;
    if (mask == -2)
    {
        imageStore(dstChannel, texel, imageLoad(srcChannel, texel));
    }
    else if (mask == -1)
    {
        imageStore(dstChannel, texel, vec4(0.0));
    }
    else
    {
        ivec2 n = texel + ivec2(OffsetsX[mask], OffsetsY[mask]);
        imageStore(dstChannel, texel, imageLoad(srcChannel, n));
    }
}
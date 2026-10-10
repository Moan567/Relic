#version 430
layout(local_size_x = 8, local_size_y = 8) in;

layout(rgba32f, binding = 0) uniform readonly image2D srcPosition;
layout(r32i, binding = 1) uniform writeonly iimage2D dstMask;

const int OffsetsX[8] = int[8](-1, 0, 1, -1, 1, -1, 0, 1);
const int OffsetsY[8] = int[8](-1, -1, -1, 0, 0, 1, 1, 1);

void main()
{
    ivec2 texel = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(srcPosition);
    if (texel.x >= size.x || texel.y >= size.y) return;

    if (imageLoad(srcPosition, texel).a >= 0.5)
    {
        imageStore(dstMask, texel, ivec4(-2, 0, 0, 0));
        return;
    }

    for (int i = 0; i < 8; i++)
    {
        ivec2 n = texel + ivec2(OffsetsX[i], OffsetsY[i]);
        if (n.x < 0 || n.x >= size.x || n.y < 0 || n.y >= size.y) continue;
        if (imageLoad(srcPosition, n).a >= 0.5)
        {
            imageStore(dstMask, texel, ivec4(i, 0, 0, 0));
            return;
        }
    }

    imageStore(dstMask, texel, ivec4(-1, 0, 0, 0));
}
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Buffers;

namespace Engine.Utils
{
    public static class CubemapMipmapGenerator
    {
        public static TextureCube ScaleCube(RenderTargetCube renderTargetCube, int targetSize)
        {
            int size = renderTargetCube.Size;
            var format = renderTargetCube.Format;
            TextureCube resultCube = new TextureCube(MainEngine.Instance.GraphicsDevice, targetSize, mipMap: false, format);

            Color[] topLevelData = new Color[size * size];
            Color[] colorMipData = new Color[targetSize * targetSize];

            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                CubeMapFace face = (CubeMapFace)faceIndex;

                renderTargetCube.GetData(face, topLevelData);
                BilinearResize(topLevelData, size, size, colorMipData, targetSize, targetSize);
                resultCube.SetData(face, 0, null, colorMipData, 0, colorMipData.Length);
            }

            return resultCube;
        }

        private static void BilinearResize(
            Color[] src, int srcW, int srcH,
            Color[] dst, int dstW, int dstH)
        {
            float xScale = (float)srcW / dstW;
            float yScale = (float)srcH / dstH;

            for (int dstY = 0; dstY < dstH; dstY++)
            {
                // Map dst pixel centre into src space
                float srcY = (dstY + 0.5f) * yScale - 0.5f;
                int y0 = (int)MathF.Floor(srcY);
                int y1 = y0 + 1;
                float fy = srcY - y0;

                y0 = Math.Clamp(y0, 0, srcH - 1);
                y1 = Math.Clamp(y1, 0, srcH - 1);

                for (int dstX = 0; dstX < dstW; dstX++)
                {
                    float srcX = (dstX + 0.5f) * xScale - 0.5f;
                    int x0 = (int)MathF.Floor(srcX);
                    int x1 = x0 + 1;
                    float fx = srcX - x0;

                    x0 = Math.Clamp(x0, 0, srcW - 1);
                    x1 = Math.Clamp(x1, 0, srcW - 1);

                    Color c00 = src[y0 * srcW + x0];
                    Color c10 = src[y0 * srcW + x1];
                    Color c01 = src[y1 * srcW + x0];
                    Color c11 = src[y1 * srcW + x1];

                    dst[dstY * dstW + dstX] = new Color(
                        (byte)Lerp2D(c00.R, c10.R, c01.R, c11.R, fx, fy),
                        (byte)Lerp2D(c00.G, c10.G, c01.G, c11.G, fx, fy),
                        (byte)Lerp2D(c00.B, c10.B, c01.B, c11.B, fx, fy),
                        (byte)Lerp2D(c00.A, c10.A, c01.A, c11.A, fx, fy)
                    );
                }
            }
        }

        private static float Lerp2D(byte v00, byte v10, byte v01, byte v11, float fx, float fy)
        {
            float top = v00 + (v10 - v00) * fx;
            float bottom = v01 + (v11 - v01) * fx;
            return top + (bottom - top) * fy;
        }
    }
}
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Rockwall;
using SimpleImageIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MapCompiler
{
    /// <summary>
    /// Small HDR color accumulator used throughout the lightmap pipeline.
    /// Stored as raw float values (not normalized to [0,1]).
    /// </summary>
    public struct LightmapColor
    {
        public float R, G, B;

        public LightmapColor(float r, float g, float b)
        {
            // Constructor accepts values in [0,1] and scales to [0,255] internally
            R = r * 255f;
            G = g * 255f;
            B = b * 255f;
        }

        public LightmapColor(uint r, uint g, uint b) { R = r; G = g; B = b; }
        public LightmapColor(int r, int g, int b) { R = r; G = g; B = b; }

        internal Vector3 ToVector3()
        {
            return new(R, G, B);
        }

        public static LightmapColor operator *(LightmapColor a, float b)
        {
            return a with { R = a.R * b, G = a.G * b, B = a.B * b };
        }
    }

    public static class LightmapWriter
    {
        public readonly struct GroupLayer
        {
            public readonly string Name;
            public readonly RgbImage Image;
            public readonly Vector2 UvMin, UvMax;

            public GroupLayer(string name, RgbImage image, Vector2 uvMin, Vector2 uvMax)
            {
                Name = name;
                Image = image;
                UvMin = uvMin;
                UvMax = uvMax;
            }
        }

        public static GroupLayer PackGroupLayer(
            string groupName,
            LightmapColor[] b1, LightmapColor[] b2, LightmapColor[] b3,
            int resolution)
        {
            var image = new RgbImage(resolution, resolution);

            Vector2 uvMin = new(1f, 1f), uvMax = new(0f, 0f);
            bool touchedAny = false;

            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    int idx = y * resolution + x;

                    float r = b1[idx].R / 255f;
                    float g = b2[idx].G / 255f;
                    float b = b3[idx].B / 255f;

                    image.SetPixel(x, y, new RgbColor(r, g, b));

                    if (r > 0f || g > 0f || b > 0f)
                    {
                        touchedAny = true;
                        var uv = new Vector2((x + 0.5f) / resolution, (y + 0.5f) / resolution);
                        uvMin = Vector2.Min(uvMin, uv);
                        uvMax = Vector2.Max(uvMax, uv);
                    }
                }
            }

            if (!touchedAny)
            {
                CompilerConsole.Warn($"LightGroup '{groupName}' baked to an all-black layer. Check its lights are actually reaching a surface.");
                uvMin = Vector2.Zero;
                uvMax = Vector2.One;
            }

            using var filtered = BilateralFilter(image, radius: 1, sigmaSpatial: 1.0f, sigmaRange: 0.1f);
            image.Dispose();

            using var encoded = filtered.ApplyOpInPlace(Map);
            var packed = new RgbImage(resolution, resolution);
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    packed.SetPixel(x, y, new RgbColor(encoded.GetPixelChannel(x, y, 0), encoded.GetPixelChannel(x, y, 1), encoded.GetPixelChannel(x, y, 2)));
                }
            }

            return new GroupLayer(groupName, packed, uvMin, uvMax);
        }

        private static float Map(float v)
        {
            return v;

            if (v <= 0.0031308f)
                return v * 12.92f;
            if (v <= 1f)
                return 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;

            const float slopeAt1 = 1.055f / 2.4f;
            return 1f + slopeAt1 * (v - 1f);
        }

        public static (RgbImage normal, RgbImage tangent, RgbImage binormal) WritePixels(
            LightmapColor[] b1, LightmapColor[] b2, LightmapColor[] b3,
            int resolution)
        {
            var lmNormal = new RgbImage(resolution, resolution);
            var lmTangent = new RgbImage(resolution, resolution);
            var lmBinormal = new RgbImage(resolution, resolution);

            for (int x = 0; x < resolution; x++)
                for (int y = 0; y < resolution; y++)
                {
                    int idx = y * resolution + x;
                    lmNormal.SetPixel(x, y, ToRgb(b1[idx], resolution));
                    lmTangent.SetPixel(x, y, ToRgb(b2[idx], resolution));
                    lmBinormal.SetPixel(x, y, ToRgb(b3[idx], resolution));
                }

            return (lmNormal, lmTangent, lmBinormal);
        }

        public static (RgbImage, RgbImage, RgbImage) ApplyBilateralFilter(
            RgbImage normal, RgbImage tangent, RgbImage binormal,
            int radius = 1, float sigmaSpatial = 1.0f, float sigmaRange = 0.1f)
        {
            return (
                BilateralFilter(normal, radius, sigmaSpatial, sigmaRange),
                BilateralFilter(tangent, radius, sigmaSpatial, sigmaRange),
                BilateralFilter(binormal, radius, sigmaSpatial, sigmaRange));
        }

        public static void SaveLightmapArchive(
            RgbImage normal, RgbImage tangent, RgbImage binormal,
            List<GroupLayer> groupLayers,
            string mapPath)
        {
            string rootDir = Path.GetDirectoryName(mapPath)!;
            string filename = Path.GetFileNameWithoutExtension(mapPath);

            var lightmaps = new List<(string Name, byte[] Data)>();

            using (var img = normal.ApplyOpInPlace(Map)) lightmaps.Add(("index-b1.hdr", img.WriteToMemory(".hdr")));
            using (var img = tangent.ApplyOpInPlace(Map)) lightmaps.Add(("index-b2.hdr", img.WriteToMemory(".hdr")));
            using (var img = binormal.ApplyOpInPlace(Map)) lightmaps.Add(("index-b3.hdr", img.WriteToMemory(".hdr")));

            normal.Dispose();
            tangent.Dispose();
            binormal.Dispose();

            var groupBounds = new List<LightGroupBounds>(groupLayers.Count);
            foreach (var layer in groupLayers)
            {
                lightmaps.Add(($"lightgroup-{layer.Name}.hdr", layer.Image.WriteToMemory(".hdr")));
                groupBounds.Add(new LightGroupBounds { Name = layer.Name, UvMin = layer.UvMin, UvMax = layer.UvMax });
                layer.Image.Dispose();
            }

            byte[] groupBoundsJson = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(groupBounds, Formatting.Indented));

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, data) in lightmaps)
                {
                    var entry = zip.CreateEntry(name);
                    using var es = entry.Open();
                    es.Write(data, 0, data.Length);
                }

                var jsonEntry = zip.CreateEntry("lightgroups.json");
                using var jsonStream = jsonEntry.Open();
                jsonStream.Write(groupBoundsJson, 0, groupBoundsJson.Length);
            }

            File.WriteAllBytes(Path.Combine(rootDir, filename + ".clm"), ms.ToArray());
        }

        private static RgbColor ToRgb(LightmapColor c, int _)
            => new RgbColor(c.R / 255f, c.G / 255f, c.B / 255f);

        /// <summary>
        /// Standard bilateral filter: preserves edges by weighting neighbors
        /// by both spatial closeness and color similarity.
        /// </summary>
        private static RgbImage BilateralFilter(
            RgbImage src, int radius, float sigmaSpatial, float sigmaRange)
        {
            int w = src.Width, h = src.Height;
            var dst = new RgbImage(w, h);

            float twoSS2 = 2f * sigmaSpatial * sigmaSpatial;
            float twoSR2 = 2f * sigmaRange * sigmaRange;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var center = src.GetPixel(x, y);
                    float sumR = 0, sumG = 0, sumB = 0, wSum = 0;

                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int sx = Math.Clamp(x + dx, 0, w - 1);
                            int sy = Math.Clamp(y + dy, 0, h - 1);
                            var s = src.GetPixel(sx, sy);

                            float spatialW = MathF.Exp(-(dx * dx + dy * dy) / twoSS2);
                            float dr = s.R - center.R, dg = s.G - center.G, db = s.B - center.B;
                            float rangeW = MathF.Exp(-(dr * dr + dg * dg + db * db) / twoSR2);

                            float w_ = spatialW * rangeW;
                            sumR += s.R * w_; sumG += s.G * w_; sumB += s.B * w_;
                            wSum += w_;
                        }

                    dst.SetPixel(x, y, new RgbColor(sumR / wSum, sumG / wSum, sumB / wSum));
                }

            return dst;
        }

        // Additive blend, normalized to [0,1]
        public static float Mix(float a, float b) => (a + b) / 255f;
    }
}
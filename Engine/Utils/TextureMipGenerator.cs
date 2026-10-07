using Engine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Engine.MainEngine;

namespace Engine.Utils
{
    public static class TextureMipGenerator
    {
        private static Dictionary<int, int> refCounts = new Dictionary<int, int>();
        private static HashSet<int> mapBaseIndices = new HashSet<int>();
        private static HashSet<int> permanentIndices = new HashSet<int>();

        public static void SetMapMaterials(IEnumerable<int> usedIndices)
        {
            var wanted = new HashSet<int>(usedIndices);

            foreach (int index in mapBaseIndices)
            {
                Release(index);
            }

            mapBaseIndices = wanted;

            foreach (int index in wanted)
            {
                Retain(index);
            }
        }

        public static void Retain(int index)
        {
            if (index < 0 || index >= GlobalMapData.LoadedMaterials.Length) return;

            if (refCounts.TryGetValue(index, out int count))
            {
                refCounts[index] = count + 1;
            }
            else
            {
                refCounts[index] = 1;
                if (!permanentIndices.Contains(index))
                {
                    LoadMaterialTextures(index);
                }
            }
        }

        public static void Release(int index)
        {
            if (index < 0 || index >= GlobalMapData.LoadedMaterials.Length) return;
            if (permanentIndices.Contains(index)) return;
            if (!refCounts.TryGetValue(index, out int count)) return;

            count--;
            if (count <= 0)
            {
                refCounts.Remove(index);
                UnloadMaterialTextures(index);
            }
            else
            {
                refCounts[index] = count;
            }
        }

        public static void ReserveMaterial(int index)
        {
            if (index < 0 || index >= GlobalMapData.LoadedMaterials.Length) return;
            if (permanentIndices.Contains(index)) return;

            permanentIndices.Add(index);

            if (!refCounts.ContainsKey(index))
            {
                LoadMaterialTextures(index);
            }
        }

        public static void ReserveMaterial(string materialName)
        {
            if (GlobalMapData.MaterialNameToIndex.TryGetValue(materialName, out int index))
            {
                ReserveMaterial(index);
            }
        }

        public static void ReloadActiveTextures()
        {
            foreach (int index in refCounts.Keys)
            {
                LoadMaterialTextures(index);
            }
            foreach (int index in permanentIndices)
            {
                if (!refCounts.ContainsKey(index))
                {
                    LoadMaterialTextures(index);
                }
            }
        }

        private static void UnloadMaterialTextures(int index)
        {
            if (GlobalMapData.LoadedMaterials[index].Textures != null)
            {
                foreach (var val in GlobalMapData.LoadedMaterials[index].Textures)
                {
                    val.Value?.Dispose();
                }
                GlobalMapData.LoadedMaterials[index].Textures.Clear();
            }
        }

        private static void LoadMaterialTextures(int index)
        {
            UnloadMaterialTextures(index);

            foreach (var (mapName, path) in GlobalMapData.LoadedMaterials[index].TexturePaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                GlobalMapData.LoadedMaterials[index].SetTexture(mapName, GenerateSingleChain(Instance.Content.Load<Texture2D>($"{path}")));
            }
        }

        static SurfaceFormat ToLinearFormat(SurfaceFormat format)
        {
            switch (format)
            {
                case SurfaceFormat.ColorSRgb: return SurfaceFormat.Color;
                case SurfaceFormat.Bgra32SRgb: return SurfaceFormat.Bgra32;
                case SurfaceFormat.Bgr32SRgb: return SurfaceFormat.Bgr32;
                case SurfaceFormat.Dxt1SRgb: return SurfaceFormat.Dxt1;
                case SurfaceFormat.Dxt3SRgb: return SurfaceFormat.Dxt3;
                case SurfaceFormat.Dxt5SRgb: return SurfaceFormat.Dxt5;
                default: return format;
            }
        }
        static Texture2D GenerateSingleChain(Texture2D source, bool isNormalMap = false)
        {
            SurfaceFormat format = isNormalMap ? ToLinearFormat(source.Format) : source.Format;
            int mipLevel = (int)(2 - RenderEngine.TextureQuality);
            mipLevel = Math.Clamp(mipLevel, 0, source.LevelCount - 1);

            int texWidth = Math.Max(1, source.Width >> mipLevel);
            int texHeight = Math.Max(1, source.Height >> mipLevel);

            int levelsToCopy = source.LevelCount - mipLevel;
            bool generateMips = levelsToCopy > 1;

            Texture2D newTexture = new Texture2D(MainEngine.Instance.GraphicsDevice, texWidth, texHeight, generateMips, format);

            for (int level = 0; level < newTexture.LevelCount; level++)
            {
                int srcLevel = mipLevel + level;
                int w = Math.Max(1, source.Width >> srcLevel);
                int h = Math.Max(1, source.Height >> srcLevel);

                var pixels = new Color[w * h];
                source.GetData(srcLevel, null, pixels, 0, pixels.Length);
                newTexture.SetData(level, null, pixels, 0, pixels.Length);
            }

            return newTexture;
        }
    }
}
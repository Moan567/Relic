using Microsoft.Xna.Framework;
using Rockwall;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MapCompiler
{
    public static class TextureLoader
    {
        public static (System.Drawing.Bitmap[] textures, Color[] matColors) Load(string texturePath, Brush[] brushes)
        {
            var textures = new System.Drawing.Bitmap[GlobalMapData.LoadedMaterials.Length];
            var matColors = new Color[GlobalMapData.LoadedMaterials.Length];

            var usedTextures = brushes.SelectMany(b => b.Faces).Select(f => f.Surface).Distinct().ToArray();
            CompilerConsole.Stat("Unique textures", usedTextures.Length);

            string[] rawExtensions = { ".png", ".jpg", ".jpeg", ".tga" };
            var needsXnb = new List<int>();

            foreach (int i in usedTextures)
            {
                var name = GlobalMapData.LoadedMaterials[i].TextureName;
                var raw = rawExtensions.Select(ext => $"{texturePath}/{name}{ext}").FirstOrDefault(File.Exists);

                if (raw != null)
                    textures[i] = (System.Drawing.Bitmap)System.Drawing.Bitmap.FromFile(raw);
                else
                    needsXnb.Add(i);
            }

            if (needsXnb.Count > 0)
            {
                var names = needsXnb.Select(i => GlobalMapData.LoadedMaterials[i].TextureName).ToArray();
                using var host = new HeadlessTextureHost(texturePath, names);
                host.Run();

                foreach (int i in needsXnb)
                    textures[i] = host.Results[GlobalMapData.LoadedMaterials[i].TextureName];
            }

            Parallel.ForEach(usedTextures, i =>
                matColors[i] = GetDominantColor(textures[i]));

            return (textures, matColors);
        }

        public static Color GetDominantColor(System.Drawing.Bitmap bmp)
        {
            long r = 0, g = 0, b = 0, total = 0;

            for (int x = 0; x < bmp.Width; x += 2)
                for (int y = 0; y < bmp.Height; y += 2)
                {
                    var c = bmp.GetPixel(x, y);
                    r += c.R; g += c.G; b += c.B;
                    total++;
                }

            return new Color((int)(r / total), (int)(g / total), (int)(b / total), 255);
        }
    }
}
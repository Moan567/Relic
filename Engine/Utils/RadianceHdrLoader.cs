using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using System;
using System.IO;
using System.Text;

namespace Engine.Utils;

public static class RadianceHdrLoader
{
    public static Texture2D Load(GraphicsDevice graphicsDevice, string path)
    {
        using (FileStream stream = File.OpenRead(path))
        {
            return Load(graphicsDevice, stream);
        }
    }

    public static Texture2D Load(GraphicsDevice graphicsDevice, Stream stream)
    {
        using (BufferedStream buffered = new BufferedStream(stream))
        {
            ReadHeader(buffered);

            int width;
            int height;
            ReadResolution(buffered, out width, out height);

            Vector4[] pixels = new Vector4[width * height];
            byte[] scanline = new byte[width * 4];

            for (int y = 0; y < height; y++)
            {
                ReadScanline(buffered, scanline, width);

                for (int x = 0; x < width; x++)
                {
                    int offset = x * 4;
                    pixels[y * width + x] = RgbeToFloat(scanline[offset], scanline[offset + 1], scanline[offset + 2], scanline[offset + 3]);
                }
            }

            Texture2D texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Vector4);
            texture.SetData(pixels);
            return texture;
        }
    }

    static void ReadHeader(Stream stream)
    {
        string line = ReadLine(stream);
        if (!line.StartsWith("#?"))
        {
            throw new InvalidDataException("Not a valid Radiance HDR file");
        }

        while (true)
        {
            line = ReadLine(stream);
            if (line.Length == 0)
            {
                break;
            }
        }
    }

    static void ReadResolution(Stream stream, out int width, out int height)
    {
        string line = ReadLine(stream);
        string[] tokens = line.Split(' ');

        if (tokens.Length != 4 || tokens[0] != "-Y" || tokens[2] != "+X")
        {
            throw new NotSupportedException("Only top-down, left-to-right HDR files are supported: " + line);
        }

        height = int.Parse(tokens[1]);
        width = int.Parse(tokens[3]);
    }

    static string ReadLine(Stream stream)
    {
        StringBuilder builder = new StringBuilder();
        int b;

        while ((b = stream.ReadByte()) != -1 && b != '\n')
        {
            builder.Append((char)b);
        }

        return builder.ToString();
    }

    static void ReadScanline(Stream stream, byte[] scanline, int width)
    {
        if (width < 8 || width > 0x7fff)
        {
            ReadFlatScanline(stream, scanline, width, 0);
            return;
        }

        int b0 = stream.ReadByte();
        int b1 = stream.ReadByte();
        int b2 = stream.ReadByte();
        int b3 = stream.ReadByte();

        if (b0 != 2 || b1 != 2 || (b2 & 0x80) != 0)
        {
            scanline[0] = (byte)b0;
            scanline[1] = (byte)b1;
            scanline[2] = (byte)b2;
            scanline[3] = (byte)b3;
            ReadFlatScanline(stream, scanline, width, 1);
            return;
        }

        int scanlineWidth = (b2 << 8) | b3;
        if (scanlineWidth != width)
        {
            throw new InvalidDataException("Scanline width mismatch in HDR file");
        }

        for (int channel = 0; channel < 4; channel++)
        {
            int x = 0;

            while (x < width)
            {
                int count = stream.ReadByte();

                if (count > 128)
                {
                    count -= 128;
                    byte value = (byte)stream.ReadByte();

                    for (int i = 0; i < count; i++)
                    {
                        scanline[(x + i) * 4 + channel] = value;
                    }

                    x += count;
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        scanline[(x + i) * 4 + channel] = (byte)stream.ReadByte();
                    }

                    x += count;
                }
            }
        }
    }
    public static Texture2D FromStream(GraphicsDevice graphicsDevice, Stream stream)
    {
        if (!stream.CanSeek)
        {
            using (MemoryStream buffered = new MemoryStream())
            {
                stream.CopyTo(buffered);
                buffered.Position = 0;
                return LoadHalf(graphicsDevice, buffered);
            }
        }

        return LoadHalf(graphicsDevice, stream);
    }

    static Texture2D LoadHalf(GraphicsDevice graphicsDevice, Stream stream)
    {
        using (BufferedStream buffered = new BufferedStream(stream))
        {
            ReadHeader(buffered);

            int width;
            int height;
            ReadResolution(buffered, out width, out height);

            HalfVector4[] pixels = new HalfVector4[width * height];
            byte[] scanline = new byte[width * 4];

            for (int y = 0; y < height; y++)
            {
                ReadScanline(buffered, scanline, width);

                for (int x = 0; x < width; x++)
                {
                    int offset = x * 4;
                    Vector4 color = RgbeToFloat(scanline[offset], scanline[offset + 1], scanline[offset + 2], scanline[offset + 3]);
                    pixels[y * width + x] = new HalfVector4(color);
                }
            }

            Texture2D texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.HalfVector4);
            texture.SetData(pixels);
            return texture;
        }
    }
    static void ReadFlatScanline(Stream stream, byte[] scanline, int width, int startPixel)
    {
        for (int x = startPixel; x < width; x++)
        {
            scanline[x * 4] = (byte)stream.ReadByte();
            scanline[x * 4 + 1] = (byte)stream.ReadByte();
            scanline[x * 4 + 2] = (byte)stream.ReadByte();
            scanline[x * 4 + 3] = (byte)stream.ReadByte();
        }
    }

    static Vector4 RgbeToFloat(byte r, byte g, byte b, byte e)
    {
        if (e == 0)
        {
            return Vector4.Zero;
        }

        float scale = (float)Math.Pow(2, e - 128 - 8);
        return new Vector4(r * scale, g * scale, b * scale, 1.0f);
    }
}
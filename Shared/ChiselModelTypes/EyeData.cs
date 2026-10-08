using MessagePack;
using MessagePack.Formatters;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Chisel.Models.Data;

public record struct EyeTextureRecipe(
    int Seed,
    Color ScleraColor,
    Color VesselColor,
    Color IrisColor,
    Color IrisRimColor,
    float IrisSize,
    Color PupilColor,
    float PupilBaseSize,
    float PupilFeather,
    float FiberDensity,
    float VeinDensity,
    float VeinDistortion,
    Color IrisFleckColor,
    float IrisFleckAmount,
    float NormalStrength,
    float CorneaBulgeStrength,
    float ScleraSpecular,
    float IrisSpecular)
{
    public static EyeTextureRecipe Default => new(
        Seed: 0,
        ScleraColor: new Color(255, 250, 245),
        VesselColor: new Color(190, 60, 55),
        IrisColor: new Color(90, 60, 40),
        IrisRimColor: new Color(35, 25, 18),
        IrisSize: 0.42f,
        PupilColor: Color.Black,
        PupilBaseSize: 0.18f,
        PupilFeather: 0.02f,
        FiberDensity: 24f,
        VeinDensity: 10f,
        VeinDistortion: 0.35f,
        IrisFleckColor: new Color(150, 110, 70),
        IrisFleckAmount: 0.12f,
        NormalStrength: 6f,
        CorneaBulgeStrength: 0.08f,
        ScleraSpecular: 0.15f,
        IrisSpecular: 0.85f);
}
public class ColorFormatter : IMessagePackFormatter<Color>
{
    public void Serialize(ref MessagePackWriter writer, Color value, MessagePackSerializerOptions options)
    {
        writer.WriteUInt32(value.PackedValue);
    }

    public Color Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        uint packed = reader.ReadUInt32();
        Color result = default;
        result.PackedValue = packed;
        return result;
    }
}


public struct EyeTextureSet
{
    public Texture2D Color;
    public Texture2D Data;
}

public static class EyeTextureGenerator
{
    private static readonly VertexPositionTexture[] quadVerts =
    {
            new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1)),
            new VertexPositionTexture(new Vector3(-1,  1, 0), new Vector2(0, 0)),
            new VertexPositionTexture(new Vector3( 1,  1, 0), new Vector2(1, 0)),
            new VertexPositionTexture(new Vector3( 1, -1, 0), new Vector2(1, 1)),
        };
    private static readonly short[] quadIndices = { 0, 1, 2, 0, 2, 3 };

    private static SpriteBatch mipSpriteBatch;

#if CHISEL_GLSL
    public static EyeTextureSet Generate(GraphicsDevice device, Engine.ShaderHandle shader, EyeTextureRecipe recipe, int resolution = 128)
#else
    public static EyeTextureSet Generate(GraphicsDevice device, Effect shader, EyeTextureRecipe recipe, int resolution = 128)
#endif
    {
        var colorTarget = new RenderTarget2D(device, resolution, resolution, true, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var dataTarget = new RenderTarget2D(device, resolution, resolution, true, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

        ApplyRecipeParameters(shader, recipe);
#if CHISEL_GLSL
        shader.Param("TexelSize").SetValue(1f / resolution);
#else
        shader.Parameters["TexelSize"]?.SetValue(1f / resolution);
#endif

        var previousTargets = device.GetRenderTargets();
        var previousViewport = device.Viewport;
        var previousBlend = device.BlendState;
        var previousDepth = device.DepthStencilState;
        var previousRasterizer = device.RasterizerState;

        device.SetRenderTargets(colorTarget, dataTarget);
        device.Viewport = new Viewport(0, 0, resolution, resolution);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.Clear(Color.Black);

#if CHISEL_GLSL
        shader.SetTechnique("EyeGen");
        shader.RenderEachPass(() =>
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, quadVerts, 0, 4, quadIndices, 0, 2));
#else
        shader.CurrentTechnique = shader.Techniques["EyeGen"];
        foreach (var pass in shader.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, quadVerts, 0, 4, quadIndices, 0, 2);
        }
#endif

        device.SetRenderTargets(previousTargets);
        device.Viewport = previousViewport;
        device.BlendState = previousBlend;
        device.DepthStencilState = previousDepth;
        device.RasterizerState = previousRasterizer;

        mipSpriteBatch ??= new SpriteBatch(device);
        GenerateMipChain(device, mipSpriteBatch, colorTarget);
        GenerateMipChain(device, mipSpriteBatch, dataTarget);

        return new EyeTextureSet { Color = colorTarget, Data = dataTarget };
    }

    private static void GenerateMipChain(GraphicsDevice device, SpriteBatch spriteBatch, RenderTarget2D texture)
    {
        int levels = texture.LevelCount;
        if (levels <= 1) return;

        var previousTargets = device.GetRenderTargets();
        var previousViewport = device.Viewport;
        var previousBlend = device.BlendState;
        var previousDepth = device.DepthStencilState;
        var previousRasterizer = device.RasterizerState;

        Texture2D sourceLevel = texture;
        Texture2D disposableSource = null;
        int width = texture.Width;
        int height = texture.Height;

        for (int level = 1; level < levels; level++)
        {
            int nextWidth = Math.Max(1, width / 2);
            int nextHeight = Math.Max(1, height / 2);

            using (var temp = new RenderTarget2D(device, nextWidth, nextHeight, false, texture.Format, DepthFormat.None, 0, RenderTargetUsage.PreserveContents))
            {
                device.SetRenderTarget(temp);
                device.Viewport = new Viewport(0, 0, nextWidth, nextHeight);
                device.BlendState = BlendState.Opaque;
                device.DepthStencilState = DepthStencilState.None;
                device.RasterizerState = RasterizerState.CullNone;
                device.Clear(Color.Transparent);

                spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp);
                spriteBatch.Draw(sourceLevel, new Rectangle(0, 0, nextWidth, nextHeight), Color.White);
                spriteBatch.End();

                var pixelData = new Color[nextWidth * nextHeight];
                temp.GetData(pixelData);
                texture.SetData(level, null, pixelData, 0, pixelData.Length);

                disposableSource?.Dispose();
                disposableSource = new Texture2D(device, nextWidth, nextHeight);
                disposableSource.SetData(pixelData);
                sourceLevel = disposableSource;
            }

            width = nextWidth;
            height = nextHeight;
        }

        disposableSource?.Dispose();

        device.SetRenderTargets(previousTargets);
        device.Viewport = previousViewport;
        device.BlendState = previousBlend;
        device.DepthStencilState = previousDepth;
        device.RasterizerState = previousRasterizer;
    }

#if CHISEL_GLSL
    private static void ApplyRecipeParameters(Engine.ShaderHandle shader, EyeTextureRecipe recipe)
    {
        shader.Param("Seed").SetValue((float)recipe.Seed);
        shader.Param("ScleraColor").SetValue(recipe.ScleraColor.ToVector4());
        shader.Param("VesselColor").SetValue(recipe.VesselColor.ToVector4());
        shader.Param("IrisColor").SetValue(recipe.IrisColor.ToVector4());
        shader.Param("IrisRimColor").SetValue(recipe.IrisRimColor.ToVector4());
        shader.Param("IrisSize").SetValue(recipe.IrisSize);
        shader.Param("PupilColor").SetValue(recipe.PupilColor.ToVector4());
        shader.Param("PupilBaseSize").SetValue(recipe.PupilBaseSize);
        shader.Param("PupilFeather").SetValue(recipe.PupilFeather);
        shader.Param("FiberDensity").SetValue(recipe.FiberDensity);
        shader.Param("VeinDensity").SetValue(recipe.VeinDensity);
        shader.Param("VeinDistortion").SetValue(recipe.VeinDistortion);
        shader.Param("IrisFleckColor").SetValue(recipe.IrisFleckColor.ToVector4());
        shader.Param("IrisFleckAmount").SetValue(recipe.IrisFleckAmount);
        shader.Param("NormalStrength").SetValue(recipe.NormalStrength);
        shader.Param("CorneaBulgeStrength").SetValue(recipe.CorneaBulgeStrength);
        shader.Param("ScleraSpecular").SetValue(recipe.ScleraSpecular);
        shader.Param("IrisSpecular").SetValue(recipe.IrisSpecular);
    }
#else
    private static void ApplyRecipeParameters(Effect shader, EyeTextureRecipe recipe)
    {
        shader.Parameters["Seed"]?.SetValue((float)recipe.Seed);
        shader.Parameters["ScleraColor"]?.SetValue(recipe.ScleraColor.ToVector4());
        shader.Parameters["VesselColor"]?.SetValue(recipe.VesselColor.ToVector4());
        shader.Parameters["IrisColor"]?.SetValue(recipe.IrisColor.ToVector4());
        shader.Parameters["IrisRimColor"]?.SetValue(recipe.IrisRimColor.ToVector4());
        shader.Parameters["IrisSize"]?.SetValue(recipe.IrisSize);
        shader.Parameters["PupilColor"]?.SetValue(recipe.PupilColor.ToVector4());
        shader.Parameters["PupilBaseSize"]?.SetValue(recipe.PupilBaseSize);
        shader.Parameters["PupilFeather"]?.SetValue(recipe.PupilFeather);
        shader.Parameters["FiberDensity"]?.SetValue(recipe.FiberDensity);
        shader.Parameters["VeinDensity"]?.SetValue(recipe.VeinDensity);
        shader.Parameters["VeinDistortion"]?.SetValue(recipe.VeinDistortion);
        shader.Parameters["IrisFleckColor"]?.SetValue(recipe.IrisFleckColor.ToVector4());
        shader.Parameters["IrisFleckAmount"]?.SetValue(recipe.IrisFleckAmount);
        shader.Parameters["NormalStrength"]?.SetValue(recipe.NormalStrength);
        shader.Parameters["CorneaBulgeStrength"]?.SetValue(recipe.CorneaBulgeStrength);
        shader.Parameters["ScleraSpecular"]?.SetValue(recipe.ScleraSpecular);
        shader.Parameters["IrisSpecular"]?.SetValue(recipe.IrisSpecular);
    }
#endif
}
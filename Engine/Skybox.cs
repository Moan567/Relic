using Engine.Utils;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Engine
{
    public static class Skybox
    {
        static Microsoft.Xna.Framework.Graphics.VertexPosition[] skyCubeStrip = {
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(-1.0f, 1.0f,  1.0f)),     // Front-top-left
            new VertexPosition(new Vector3(1.0f,  1.0f,  1.0f)),      // Front-top-right
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(-1.0f,-1.0f,  1.0f)),    // Front-bottom-left
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(1.0f, -1.0f,  1.0f)),     // Front-bottom-right
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(1.0f, -1.0f, -1.0f)),    // Back-bottom-right
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(1.0f,  1.0f,  1.0f)),      // Front-top-right
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(1.0f,  1.0f, -1.0f)),     // Back-top-right
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(-1.0f, 1.0f,  1.0f)),     // Front-top-left
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(-1.0f, 1.0f, -1.0f)),    // Back-top-left
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(-1.0f,-1.0f,  1.0f)),    // Front-bottom-left
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(-1.0f,-1.0f, -1.0f)),   // Back-bottom-left
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(1.0f, -1.0f, -1.0f)),    // Back-bottom-right
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(-1.0f, 1.0f, -1.0f)),    // Back-top-left
            new Microsoft.Xna.Framework.Graphics.VertexPosition(new Vector3(1.0f,  1.0f, -1.0f))      // Back-top-right
        };
        static Microsoft.Xna.Framework.Graphics.VertexBuffer cubeBuffer;

        private static Microsoft.Xna.Framework.Graphics.TextureCube skyBoxTexture;
        private static ShaderHandle skyBoxEffect;
        public static Microsoft.Xna.Framework.Graphics.TextureCube GetSkyTexture() => skyBoxTexture;
        public static void Init(Microsoft.Xna.Framework.Content.ContentManager Content)
        {
            skyBoxEffect = ShaderBuilder.BuildSkyboxShader(MainEngine.Instance.GraphicsDevice);
            skyBoxEffect.Param("World").SetValue(Matrix.Identity);

            if (cubeBuffer == null)
            {
                cubeBuffer = new Microsoft.Xna.Framework.Graphics.VertexBuffer(MainEngine.Instance.GraphicsDevice, typeof(Microsoft.Xna.Framework.Graphics.VertexPosition), skyCubeStrip.Length, Microsoft.Xna.Framework.Graphics.BufferUsage.WriteOnly);

                cubeBuffer.SetData(skyCubeStrip);
            }
        }
        public static void SetSkyTexture(Microsoft.Xna.Framework.Graphics.TextureCube tex)
        {
            skyBoxTexture = tex;
        }
        public static Microsoft.Xna.Framework.Graphics.TextureCube LoadSkybox(Microsoft.Xna.Framework.Content.ContentManager content, Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice, string path)
        {
            string fullPath = Path.Combine(content.RootDirectory, path);
            if (path.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.ChangeExtension(fullPath,"hdr")))
            {
                fullPath = Path.ChangeExtension(fullPath,"hdr");
                var equirectTexture = RadianceHdrLoader.Load(graphicsDevice, fullPath);
                ShaderHandle equirectToCubeEffect = ShaderBuilder.BuildEquirectToCubeShader(graphicsDevice);
                return EquirectToCubemapConverter.Convert(graphicsDevice, equirectToCubeEffect, equirectTexture, 1024);
            }

            return content.Load<Microsoft.Xna.Framework.Graphics.TextureCube>(path);
        }
        public static void Draw(Matrix view, Matrix projection)
        {
            Matrix v = view;
            v.Translation = Vector3.Zero;

            skyBoxEffect.Param("View").SetValue(v);
            skyBoxEffect.Param("Projection").SetValue(projection);
            skyBoxEffect.Param("SkyBoxTexture").SetValue(skyBoxTexture);

            MainEngine.Instance.GraphicsDevice.SetVertexBuffer(cubeBuffer);

            skyBoxEffect.RenderEachPass(() =>
                MainEngine.Instance.GraphicsDevice.DrawPrimitives(Microsoft.Xna.Framework.Graphics.PrimitiveType.TriangleStrip, 0, 12));

            MainEngine.Instance.GraphicsDevice.SetVertexBuffer(null);
        }
    }
}

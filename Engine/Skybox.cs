using Engine.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
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
        static VertexPosition[] skyCubeStrip = {
            new VertexPosition(new Vector3(-1.0f, 1.0f,  1.0f)),     // Front-top-left
            new VertexPosition(new Vector3(1.0f,  1.0f,  1.0f)),      // Front-top-right
            new VertexPosition(new Vector3(-1.0f,-1.0f,  1.0f)),    // Front-bottom-left
            new VertexPosition(new Vector3(1.0f, -1.0f,  1.0f)),     // Front-bottom-right
            new VertexPosition(new Vector3(1.0f, -1.0f, -1.0f)),    // Back-bottom-right
            new VertexPosition(new Vector3(1.0f,  1.0f,  1.0f)),      // Front-top-right
            new VertexPosition(new Vector3(1.0f,  1.0f, -1.0f)),     // Back-top-right
            new VertexPosition(new Vector3(-1.0f, 1.0f,  1.0f)),     // Front-top-left
            new VertexPosition(new Vector3(-1.0f, 1.0f, -1.0f)),    // Back-top-left
            new VertexPosition(new Vector3(-1.0f,-1.0f,  1.0f)),    // Front-bottom-left
            new VertexPosition(new Vector3(-1.0f,-1.0f, -1.0f)),   // Back-bottom-left
            new VertexPosition(new Vector3(1.0f, -1.0f, -1.0f)),    // Back-bottom-right
            new VertexPosition(new Vector3(-1.0f, 1.0f, -1.0f)),    // Back-top-left
            new VertexPosition(new Vector3(1.0f,  1.0f, -1.0f))      // Back-top-right
        };
        static VertexBuffer cubeBuffer;

        private static TextureCube skyBoxTexture;
        private static ShaderHandle skyBoxEffect;
        public static TextureCube GetSkyTexture() => skyBoxTexture;
        public static void Init(ContentManager Content)
        {
            skyBoxEffect = ShaderBuilder.BuildSkyboxShader(MainEngine.Instance.GraphicsDevice);
            skyBoxEffect.Param("World").SetValue(Matrix.Identity);

            if (cubeBuffer == null)
            {
                cubeBuffer = new VertexBuffer(MainEngine.Instance.GraphicsDevice, typeof(VertexPosition), skyCubeStrip.Length, BufferUsage.WriteOnly);
                
                cubeBuffer.SetData(skyCubeStrip);
            }
        }
        public static void SetSkyTexture(TextureCube tex)
        {
            skyBoxTexture = tex;
        }
        public static TextureCube LoadSkybox(ContentManager content, GraphicsDevice graphicsDevice, string path)
        {
            string fullPath = Path.Combine(content.RootDirectory, path);
            if (path.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.ChangeExtension(fullPath,"hdr")))
            {
                fullPath = Path.ChangeExtension(fullPath,"hdr");
                Texture2D equirectTexture = RadianceHdrLoader.Load(graphicsDevice, fullPath);
                ShaderHandle equirectToCubeEffect = ShaderBuilder.BuildEquirectToCubeShader(graphicsDevice);
                return EquirectToCubemapConverter.Convert(graphicsDevice, equirectToCubeEffect, equirectTexture, 1024);
            }

            return content.Load<TextureCube>(path);
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
                MainEngine.Instance.GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 12));

            MainEngine.Instance.GraphicsDevice.SetVertexBuffer(null);
        }
    }
}

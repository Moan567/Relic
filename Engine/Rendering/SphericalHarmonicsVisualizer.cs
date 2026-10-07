using Engine.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Chisel.Models.CModel;
using static Engine.Utils.ProceduralMeshes;

namespace Engine.Rendering;
public class SphericalHarmonicsVisualizer : Displayable
{
    private VertexBuffer vertexBuffer;
    private IndexBuffer indexBuffer;
    private int indexCount;
    public SphericalHarmonicsVisualizer() : base((ShaderHandle)AssetManager.GetAsset("modelDefaultShader"), 0)
    {
        CreateSphere(MainEngine.Instance.GraphicsDevice, 0.25f, 12, 6, out vertexBuffer, out indexBuffer, out indexCount);
    }
    public void DrawSphere(Vector3 pos)
    {
        Transform = Matrix.CreateTranslation(pos);
        CheckForLights(pos,2f);
        PrepareShaderParamsForRendering(RenderEngine.WorldMatrix*Transform,RenderEngine.ViewMatrix,RenderEngine.ProjectionMatrix);

        Shader.Param("MainTex").SetValue(RenderEngine.WhiteTexture);
        Shader.Param("SpecTex").SetValue(RenderEngine.WhiteTexture);
        Shader.Param("NormalTex").SetValue(RenderEngine.PurpleTexture);

        Shader.SetTechnique("Low");

        Shader.Param("static_lightaffectingcount").SetValue(0);
        Shader.Param("realtimeLightCount").SetValue(0);
        Shader.Param("DiffuseIntensity").SetValue(0);

        MainEngine.Instance.GraphicsDevice.SetVertexBuffer(vertexBuffer);
        MainEngine.Instance.GraphicsDevice.Indices = indexBuffer;

        Shader.RenderEachPass(() =>
            MainEngine.Instance.GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, indexCount / 3));
    }
}

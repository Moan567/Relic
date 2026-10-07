using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils;

public static class EquirectToCubemapConverter
{
    static readonly CubeMapFace[] faces =
    {
        CubeMapFace.PositiveX,
        CubeMapFace.NegativeX,
        CubeMapFace.PositiveY,
        CubeMapFace.NegativeY,
        CubeMapFace.NegativeZ,
        CubeMapFace.PositiveZ,
    };

    static readonly Vector3[] faceForward =
    {
        Vector3.Right,
        Vector3.Left,
        Vector3.Up,
        Vector3.Down,
        Vector3.Backward,
        Vector3.Forward
    };

    static readonly Vector3[] faceUp =
    {
        Vector3.Up,
        Vector3.Up,
        Vector3.Backward,
        Vector3.Forward,
        Vector3.Up,
        Vector3.Up
    };

    static readonly Vector3[] faceRight =
    {
        Vector3.Backward,
        Vector3.Forward,
        Vector3.Right,
        Vector3.Right,
        Vector3.Left,
        Vector3.Right
    };

    public static TextureCube Convert(GraphicsDevice graphicsDevice, ShaderHandle equirectToCubeEffect, Texture2D equirectTexture, int faceSize)
    {
        for (var i = 0; i < 15; i++)
            graphicsDevice.Textures[i] = null;

        RenderTargetCube renderTarget = new RenderTargetCube(graphicsDevice, faceSize, false, equirectTexture.Format, DepthFormat.None);

        VertexPosition[] quadVertices =
        {
            new VertexPosition(new Vector3(-1, -1, 0)),
            new VertexPosition(new Vector3(-1, 3, 0)),
            new VertexPosition(new Vector3(3, -1, 0))
        };

        equirectToCubeEffect.Param("EquirectTexture").SetValue(equirectTexture);

        for (int i = 0; i < faces.Length; i++)
        {
            graphicsDevice.SetRenderTarget(renderTarget, faces[i]);
            graphicsDevice.Clear(Color.Black);

            equirectToCubeEffect.Param("FaceForward").SetValue(faceForward[i]);
            equirectToCubeEffect.Param("FaceUp").SetValue(faceUp[i]);
            equirectToCubeEffect.Param("FaceRight").SetValue(faceRight[i]);

            equirectToCubeEffect.RenderEachPass(() =>
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, quadVertices, 0, 1));
        }

        graphicsDevice.SetRenderTarget(null);
        return renderTarget;
    }
}
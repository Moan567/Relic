using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class Shapes : IDisposable
{
    const string InvalidBatch_NBgun = "batch never starter";
    const string InvalidBatch_Multi = "batch already begun.";
    const string InvalidFlush_Empty = "cannot flush empty batch.";

    private bool isDisposed = false;
    private GraphicsDevice device;
    private BasicEffect effect;
    private VertexPositionColor[] vertices;
    private short[] indices;

    private short shapeCount = 0;
    private short vertexCount = 0;
    private short indexCount = 0;

    private bool isStarted = false;

    public Shapes(GraphicsDevice device)
    {
        this.device = device ?? throw new ArgumentNullException("device");

        this.effect = new BasicEffect(device);
        this.effect.TextureEnabled = false;
        this.effect.FogEnabled = false;
        this.effect.LightingEnabled = false;
        this.effect.VertexColorEnabled = true;

        this.effect.View = Matrix.Identity;
        this.effect.World = Matrix.Identity;
        this.effect.Projection = Matrix.Identity;

        const int MaxVertexCount = 2048;
        const int MaxIndexCount = MaxVertexCount * 3;

        this.vertices = new VertexPositionColor[MaxVertexCount];
        this.indices = new short[MaxIndexCount];
        effect.Projection = Matrix.CreateOrthographicOffCenter(0, 500, 0, 500, 0, 1);
    }

    public void Resize(int w, int h)
    {
        effect.Projection = Matrix.CreateOrthographicOffCenter(0, w,0,h, 0, 1);
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        effect?.Dispose();
    }

    public void Begin()
    {
        if(isStarted)
        {
            throw new Exception(InvalidBatch_Multi);
        }
        isStarted = true;
    }

    public void End()
    {
        this.Flush();
        isStarted = false;
    }

    public void Flush()
    {
        if (shapeCount == 0) return;

        EnsureStart();
        foreach(EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserIndexedPrimitives<VertexPositionColor>(
                PrimitiveType.TriangleList,
                vertices,
                0,
                vertexCount,
                indices,
                0,
                indexCount/3);
        }

        this.shapeCount = 0;
        this.vertexCount = 0;
        this.indexCount = 0;
    }
    private void EnsureStart()
    {
        if (!isStarted)
        {
            throw new Exception(InvalidBatch_NBgun);
        }
    }
    private void EnsureSpaceFree(int shapeVertexCount, int shapeIndexCount)
    {
        if(shapeVertexCount > this.vertices.Length)
        {
            throw new Exception($"max vertice count is {this.vertices.Length}, shape vertices is {shapeVertexCount}. Discarding shape.");
        }
        if (vertexCount + shapeVertexCount > this.vertices.Length || indexCount + shapeIndexCount > this.indices.Length)
        {
            Flush();
        }
    }
    public void DrawRectangle(float x, float y, float width, float height, float line, Color color)
    {
        DrawLine(new Vector2(x, y), new Vector2(x, y + height), line, color);
        DrawLine(new Vector2(x, y), new Vector2(x+width, y), line, color);

        DrawLine(new Vector2(x+width, y+height), new Vector2(x + width, y), line, color);
        DrawLine(new Vector2(x+width, y+height), new Vector2(x, y+height), line, color);
    }

    public void DrawLine(Vector2 a, Vector2 b, float thickness,Color color)
    {
        EnsureStart();

        const int shapeVertexCount = 4;
        const int shapeIndexCount = 6;

        EnsureSpaceFree(shapeVertexCount, shapeIndexCount);

        float halfThick = thickness / 2;

        Vector2 e1 = b - a;
        e1.Normalize();
        e1 *= halfThick;

        Vector2 e2 = -e1;

        Vector2 n1 = new Vector2(-e1.Y, e1.X);

        Vector2 n2 = -n1;

        Vector2 q1 = a + n1 + e2;
        Vector2 q2 = b + n1 + e1;
        Vector2 q3 = b + n2 + e1;
        Vector2 q4 = a + n2 + e2;

        this.indices[this.indexCount++] = (short)(0 + vertexCount);
        this.indices[this.indexCount++] = (short)(1 + vertexCount);
        this.indices[this.indexCount++] = (short)(2 + vertexCount);

        this.indices[this.indexCount++] = (short)(0 + vertexCount);
        this.indices[this.indexCount++] = (short)(2 + vertexCount);
        this.indices[this.indexCount++] = (short)(3 + vertexCount);

        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(q1, 0), color);
        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(q2, 0), color);
        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(q3, 0), color);
        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(q4, 0), color);
        this.shapeCount++;
    }

    public void FillRectangle(float x, float y, float width, float height, Color color)
    {
        EnsureStart();

        const int shapeVertexCount = 4;
        const int shapeIndexCount = 6;

        EnsureSpaceFree(shapeVertexCount,shapeIndexCount);

        float left   = x;
        float right  = x + width;
        float bottom = y;
        float top    = y + height;

        Vector2 a, b, c, d;

        a = new Vector2(left,  top);
        b = new Vector2(right, top);
        c = new Vector2(right, bottom);
        d = new Vector2(left, bottom);

        this.indices[this.indexCount++] = (short)(0+vertexCount);
        this.indices[this.indexCount++] = (short)(1+vertexCount);
        this.indices[this.indexCount++] = (short)(2+vertexCount);

        this.indices[this.indexCount++] = (short)(0+vertexCount);
        this.indices[this.indexCount++] = (short)(2+vertexCount);
        this.indices[this.indexCount++] = (short)(3+vertexCount);

        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(a,0),color);
        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(b,0),color);
        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(c,0),color);
        this.vertices[this.vertexCount++] = new VertexPositionColor(new Vector3(d,0),color);

        this.shapeCount++;
    }
}
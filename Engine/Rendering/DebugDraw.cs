using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Engine.Rendering;

public static class DebugDraw
{
    public struct Entry
    {
        public Vector3 A, B;
        public Color Color;
        public float Remaining;
        public bool IsSphere;
        public float Radius;
        public bool PersistsOneFrame;
        public bool DrawnOnce;
    }

    private struct TextEntry
    {
        public Vector3 Position;
        public string Text;
        public Color Color;
        public float Remaining;
        public bool PersistsOneFrame;
        public bool DrawnOnce;
    }

    private static readonly List<Entry> lines = new();
    private static readonly List<TextEntry> texts = new();

    private static BasicEffect shader;
    private static VertexPositionColor[] scratch = new VertexPositionColor[256];

    private static void EnsureShader()
    {
        if (shader != null) return;
        shader = new BasicEffect(MainEngine.Instance.GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };
    }

    /// <summary>duration &lt;= 0 draws for one frame only.</summary>
    public static void Line(Vector3 a, Vector3 b, Color color, float duration = 0f)
    {
        lines.Add(new Entry { A = a, B = b, Color = color, Remaining = duration, IsSphere = false, PersistsOneFrame = duration <= 0f });
    }

    public static void Point(Vector3 position, Color color, float size = 0.08f, float duration = 0f)
    {
        Line(position - Vector3.Up * size, position + Vector3.Up * size, color, duration);
        Line(position - Vector3.Right * size, position + Vector3.Right * size, color, duration);
        Line(position - Vector3.Forward * size, position + Vector3.Forward * size, color, duration);
    }

    public static void Sphere(Vector3 center, float radius, Color color, float duration = 0f)
    {
        lines.Add(new Entry { A = center, Color = color, Remaining = duration, IsSphere = true, Radius = radius, PersistsOneFrame = duration <= 0f });
    }

    public static void Text(Vector3 position, string text, Color color, float duration = 0f)
    {
        texts.Add(new TextEntry { Position = position, Text = text, Color = color, Remaining = duration, PersistsOneFrame = duration <= 0f });
    }

    internal static void Update(float dt)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var e = lines[i];

            if (e.PersistsOneFrame)
            {
                if (e.DrawnOnce) { lines.RemoveAt(i); continue; }
                lines[i] = e;
                continue;
            }

            e.Remaining -= dt;
            if (e.Remaining < 0f) { lines.RemoveAt(i); continue; }
            lines[i] = e;
        }

        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var e = texts[i];

            if (e.PersistsOneFrame)
            {
                if (e.DrawnOnce) { texts.RemoveAt(i); continue; }
                texts[i] = e;
                continue;
            }

            e.Remaining -= dt;
            if (e.Remaining < 0f) { texts.RemoveAt(i); continue; }
            texts[i] = e;
        }
    }

    internal static void Draw()
    {
        if (lines.Count == 0 && texts.Count == 0) return;

        EnsureShader();

        var gd = MainEngine.Instance.GraphicsDevice;
        var oldDepth = gd.DepthStencilState;
        var oldBlend = gd.BlendState;
        var oldRasterizer = gd.RasterizerState;

        gd.BlendState = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.DepthRead;
        gd.RasterizerState = RasterizerState.CullNone;

        shader.World = RenderEngine.WorldMatrix;
        shader.View = RenderEngine.ViewMatrix;
        shader.Projection = RenderEngine.ProjectionMatrix;

        int needed = 0;
        foreach (var e in lines) needed += e.IsSphere ? 3 * 32 * 2 : 2;
        if (scratch.Length < needed) scratch = new VertexPositionColor[Math.Max(needed, scratch.Length * 2)];

        int count = 0;
        foreach (var e in lines)
        {
            if (!e.IsSphere)
            {
                scratch[count++] = new VertexPositionColor(e.A, e.Color);
                scratch[count++] = new VertexPositionColor(e.B, e.Color);
                continue;
            }

            AppendRing(ref count, e.A, e.Radius, Vector3.Right, Vector3.Forward, e.Color);
            AppendRing(ref count, e.A, e.Radius, Vector3.Up, Vector3.Forward, e.Color);
            AppendRing(ref count, e.A, e.Radius, Vector3.Right, Vector3.Up, e.Color);
        }

        if (count > 0)
        {
            foreach (var pass in shader.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.LineList, scratch, 0, count / 2);
            }
        }

        if (texts.Count > 0)
        {
            MainEngine.Instance.SpriteBatch.Begin(blendState: BlendState.NonPremultiplied);
            foreach (var t in texts)
            {
                RenderEngine.DrawDebugString(t.Position, t.Text, alpha: 1f);
            }
            MainEngine.Instance.SpriteBatch.End();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].PersistsOneFrame)
            {
                var e = lines[i];
                e.DrawnOnce = true;
                lines[i] = e;
            }
        }
        for (int i = 0; i < texts.Count; i++)
        {
            if (texts[i].PersistsOneFrame)
            {
                var e = texts[i];
                e.DrawnOnce = true;
                texts[i] = e;
            }
        }

        gd.DepthStencilState = oldDepth;
        gd.BlendState = oldBlend;
        gd.RasterizerState = oldRasterizer;
    }

    private static void AppendRing(ref int count, Vector3 center, float radius, Vector3 axisA, Vector3 axisB, Color color)
    {
        const int segments = 32;
        Vector3 prev = center + axisA * radius;

        for (int i = 1; i <= segments; i++)
        {
            float t = (i / (float)segments) * MathHelper.TwoPi;
            Vector3 next = center + (axisA * MathF.Cos(t) + axisB * MathF.Sin(t)) * radius;
            scratch[count++] = new VertexPositionColor(prev, color);
            scratch[count++] = new VertexPositionColor(next, color);
            prev = next;
        }
    }
}
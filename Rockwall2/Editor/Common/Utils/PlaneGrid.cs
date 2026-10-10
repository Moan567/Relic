using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Utils;

public class PlaneGrid
{
    private GraphicsDevice _graphicsDevice;
    private BasicEffect _effect;

    private float _spacing;
    private int _halfLines;
    private Plane _plane;

    public PlaneGrid(GraphicsDevice graphicsDevice, float spacing, int halfLines, Plane plane)
    {
        _graphicsDevice = graphicsDevice;
        _spacing = spacing;
        _halfLines = halfLines;
        _plane = plane;

        _effect = new BasicEffect(graphicsDevice)
        {
            VertexColorEnabled = true
        };
    }

    public void SetPlane(Plane plane) => _plane = plane;
    public void SetSpacing(float space) => _spacing = space;
    public void SetColor(Vector3 color) => _effect.DiffuseColor = color;

    public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition, float alpha = 1f, bool fade = true, Vector3 scrollOffset = default)
    {
        _effect.Alpha = alpha;
        _effect.View = view;
        _effect.Projection = projection;

        // Calculate a rotation matrix to align XY grid with the target plane
        Vector3 planeNormal = _plane.Normal;
        Vector3 tangent = Vector3.Cross(planeNormal, Vector3.Up);
        if (tangent.LengthSquared() < 0.01f)
            tangent = Vector3.Cross(planeNormal, Vector3.Right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(tangent, planeNormal);

        Matrix toPlane = new Matrix(
            tangent.X, tangent.Y, tangent.Z, 0,
            bitangent.X, bitangent.Y, bitangent.Z, 0,
            planeNormal.X, planeNormal.Y, planeNormal.Z, 0,
            0, 0, 0, 1
        ) * Matrix.CreateTranslation(-_plane.Normal * _plane.D);

        Vector3 gridCenter = cameraPosition - planeNormal * Vector3.Dot(cameraPosition, planeNormal);
        Vector3 localCenter = Vector3.Transform(gridCenter, Matrix.Invert(toPlane));
        localCenter.X = (float)Math.Round(localCenter.X / _spacing) * _spacing;
        localCenter.Y = (float)Math.Round(localCenter.Y / _spacing) * _spacing;

        // Treadmill scroll: shifts the line pattern within a single grid cell so it appears
        // to flow underneath a moving model, without changing the snapped footprint above.
        // Wrapping to one period is what makes it loop seamlessly instead of drifting.
        float scrollX = Wrap(Vector3.Dot(scrollOffset, tangent), _spacing);
        float scrollY = Wrap(Vector3.Dot(scrollOffset, bitangent), _spacing);
        localCenter.X += scrollX;
        localCenter.Y += scrollY;

        Vector3 snappedCenter = Vector3.Transform(localCenter, toPlane);

        var vertices = new List<VertexPositionColor>();
        Color majorColor = Color.White * 1;
        Color minorColor = Color.White * 0.5f;

        for (int i = -_halfLines; i <= _halfLines; i++)
        {
            float offset = i * _spacing;

            float dist = (_halfLines - int.Abs(i)) / (float)_halfLines;

            Vector3 x0 = Vector3.Transform(new Vector3(-_halfLines * _spacing, offset, 0), toPlane) + snappedCenter;
            Vector3 x1 = Vector3.Transform(new Vector3(_halfLines * _spacing, offset, 0), toPlane) + snappedCenter;

            Vector3 z0 = Vector3.Transform(new Vector3(offset, -_halfLines * _spacing, 0), toPlane) + snappedCenter;
            Vector3 z1 = Vector3.Transform(new Vector3(offset, _halfLines * _spacing, 0), toPlane) + snappedCenter;

            Color col = i == 0 ? majorColor : minorColor;
            if (fade) col.A = (byte)(dist * 255);

            vertices.Add(new VertexPositionColor(x0, col));
            vertices.Add(new VertexPositionColor(x1, col));
            vertices.Add(new VertexPositionColor(z0, col));
            vertices.Add(new VertexPositionColor(z1, col));
        }

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, vertices.ToArray(), 0, vertices.Count / 2);
        }
    }

    private static float Wrap(float value, float period)
    {
        float result = value % period;
        if (result < 0) result += period;
        return result;
    }
    /// <summary>
    /// Draws a grid filling the rectangle between two points on the plane.
    /// </summary>
    public void DrawBoundedGrid(Matrix view, Matrix projection, Vector3 p0, Vector3 p1, float alpha = 1, bool fade = false)
    {
        _effect.View = view;
        _effect.Projection = projection;

        // Calculate plane axes
        Vector3 up = _plane.Normal;
        Vector3 right = Vector3.Cross(up, Vector3.Up);
        if (right.LengthSquared() < 0.01f)
            right = Vector3.Cross(up, Vector3.Right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, up);

        // Project p0 and p1 onto the plane and get local coordinates
        Vector3 local0 = ToLocal(p0, right, forward, up);
        Vector3 local1 = ToLocal(p1, right, forward, up);

        float minX = MathF.Min(local0.X, local1.X);
        float maxX = MathF.Max(local0.X, local1.X);
        float minY = MathF.Min(local0.Y, local1.Y);
        float maxY = MathF.Max(local0.Y, local1.Y);

        // Snap bounds to grid
        minX = MathF.Floor(minX / _spacing) * _spacing;
        maxX = MathF.Ceiling(maxX / _spacing) * _spacing;
        minY = MathF.Floor(minY / _spacing) * _spacing;
        maxY = MathF.Ceiling(maxY / _spacing) * _spacing;

        var vertices = new List<VertexPositionColor>();
        Color majorColor = Color.White;
        Color minorColor = Color.White * 0.5f;

        // Draw lines parallel to X (varying Y)
        for (float y = minY; y <= maxY + 0.001f; y += _spacing)
        {
            float f = 1;
            float center = (minY + maxY)/2;
            float size = maxY - center;
            float yDiff = y - center;
            f = (size-float.Abs(yDiff)) / size;

            Vector3 a = ToWorld(minX, y, right, forward, up);
            Vector3 b = ToWorld(maxX, y, right, forward, up);
            Color col = Math.Abs(y) < 0.001f ? majorColor : minorColor;

            if (fade) col.A = (byte)(alpha*255*f);

            vertices.Add(new VertexPositionColor(a, col));
            vertices.Add(new VertexPositionColor(b, col));
        }

        // Draw lines parallel to Y (varying X)
        for (float x = minX; x <= maxX + 0.001f; x += _spacing)
        {
            float f = 1;
            float center = (minX + maxX) / 2;
            float size = maxX - center;
            float xDiff = x - center;
            f = (size - float.Abs(xDiff)) / size;

            Vector3 a = ToWorld(x, minY, right, forward, up);
            Vector3 b = ToWorld(x, maxY, right, forward, up);
            Color col = Math.Abs(x) < 0.001f ? majorColor : minorColor;

            if (fade) col.A = (byte)(alpha * 255 * f);

            vertices.Add(new VertexPositionColor(a, col));
            vertices.Add(new VertexPositionColor(b, col));
        }

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, vertices.ToArray(), 0, vertices.Count / 2);
        }
    }

    private Vector3 ToLocal(Vector3 point, Vector3 right, Vector3 forward, Vector3 up)
    {
        // Project point onto plane
        float d = _plane.DotCoordinate(point);
        Vector3 projected = point - up * d;
        // Get local coordinates
        float x = Vector3.Dot(projected, right);
        float y = Vector3.Dot(projected, forward);
        return new Vector3(x, y, 0);
    }

    private Vector3 ToWorld(float x, float y, Vector3 right, Vector3 forward, Vector3 up)
    {
        // Reconstruct world position from local plane coordinates
        Vector3 world = right * x + forward * y;
        // Project onto plane (z=0 in local plane)
        float d = _plane.D;
        world -= up * d;
        return world + up * 0.002f;
    }
}
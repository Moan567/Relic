using Avalonia.Input;
using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper.ViewportManagement;
using System;
using System.Globalization;
using System.Linq;

namespace Rockwall2.Editor.Mapper.Utils;

public static class DecalBoundsGizmo
{
    public const float BoundsSnap = 0.125f;
    const float MinExtent = 0.125f;
    const float HandleSize = 0.06f;
    public static bool IsDragging { get; private set; }
    public static bool IsHovering => hoveredFace != -1;
    static float time;

    static int activeFace = -1;
    static int hoveredFace = -1;
    static Vector3 dragMin, dragMax;
    static Vector2 projectedAxis;
    static float pixelsPerWorldUnit;
    static float floatingValue;
    static AllEntitySnapshot undoSnapshot;

    static readonly Vector3[] faceAxes =
    {
        -Vector3.UnitX, Vector3.UnitX,
        -Vector3.UnitY, Vector3.UnitY,
        -Vector3.UnitZ, Vector3.UnitZ,
    };

    public static bool TryGetSingleSelectedDecal(out int entityIndex)
    {
        entityIndex = -1;
        if (Toolbelt.SelectedObjects.Count != 1)
        {
            return false;
        }
        if (Toolbelt.SelectedObjects[0] is not EntityMoveable em)
        {
            return false;
        }
        if (MapTools.Entities[em.entity].EntityName != "EnvDecal")
        {
            return false;
        }
        entityIndex = em.entity;
        return true;
    }

    static Vector3 ReadPosition(EntityReference ent, string name, Vector3 fallback)
    {
        var raw = ent.Properties?.FirstOrDefault(p => p.Name == name).Value;
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }
        var parts = raw.Split(',');
        return new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    static void WritePosition(EntityReference ent, string name, Vector3 value)
    {
        string str = value.X.ToString(CultureInfo.InvariantCulture) + "," +
                     value.Y.ToString(CultureInfo.InvariantCulture) + "," +
                     value.Z.ToString(CultureInfo.InvariantCulture);

        int i = Array.FindIndex(ent.Properties ?? Array.Empty<EntityProperty>(), p => p.Name == name);
        if (i != -1)
        {
            ent.Properties[i].Value = str;
            return;
        }
        ent.Properties = (ent.Properties ?? Array.Empty<EntityProperty>()).Append(new EntityProperty { Name = name, Value = str }).ToArray();
    }

    static Matrix EntityWorld(EntityReference ent) =>
        Matrix.CreateFromYawPitchRoll(
            MathHelper.ToRadians(ent.SpawnRotation.X),
            MathHelper.ToRadians(ent.SpawnRotation.Y),
            MathHelper.ToRadians(ent.SpawnRotation.Z)) * Matrix.CreateWorld(ent.Position, Vector3.Forward, Vector3.Up);

    static Vector3 FaceCenterLocal(Vector3 min, Vector3 max, int face)
    {
        Vector3 mid = (min + max) * 0.5f;
        return face switch
        {
            0 => new Vector3(min.X, mid.Y, mid.Z),
            1 => new Vector3(max.X, mid.Y, mid.Z),
            2 => new Vector3(mid.X, min.Y, mid.Z),
            3 => new Vector3(mid.X, max.Y, mid.Z),
            4 => new Vector3(mid.X, mid.Y, min.Z),
            _ => new Vector3(mid.X, mid.Y, max.Z),
        };
    }

    public static void Update()
    {
        time += 1f / 60f;

        if (!TryGetSingleSelectedDecal(out int entity))
        {
            hoveredFace = -1;
            return;
        }

        var ent = MapTools.Entities[entity];
        Vector3 min = ReadPosition(ent, "Decal Min Bounds", -Vector3.One);
        Vector3 max = ReadPosition(ent, "Decal Max Bounds", Vector3.One);
        Matrix world = EntityWorld(ent);

        if (IsDragging)
        {
            if (!MouseManager.IsDown(MouseButton.Left))
            {
                EndDrag();
                return;
            }

            Vector2 mouseDelta = new Vector2((float)MouseManager.Delta.X, (float)MouseManager.Delta.Y);
            float move = Vector2.Dot(projectedAxis, mouseDelta) / pixelsPerWorldUnit;
            floatingValue += move * ((activeFace % 2 == 0) ? -1f : 1f);
            float snapped = MathF.Round(floatingValue / BoundsSnap) * BoundsSnap;

            Vector3 newMin = dragMin, newMax = dragMax;
            switch (activeFace)
            {
                case 0: newMin.X = MathF.Min(snapped, dragMax.X - MinExtent); break;
                case 1: newMax.X = MathF.Max(snapped, dragMin.X + MinExtent); break;
                case 2: newMin.Y = MathF.Min(snapped, dragMax.Y - MinExtent); break;
                case 3: newMax.Y = MathF.Max(snapped, dragMin.Y + MinExtent); break;
                case 4: newMin.Z = MathF.Min(snapped, dragMax.Z - MinExtent); break;
                case 5: newMax.Z = MathF.Max(snapped, dragMin.Z + MinExtent); break;
            }

            WritePosition(ent, "Decal Min Bounds", newMin);
            WritePosition(ent, "Decal Max Bounds", newMax);
            return;
        }

        var sceneRay = ViewportManager.ActiveRay(EditorHost.Instance.GraphicsDevice);
        hoveredFace = -1;
        float bestT = float.MaxValue;

        for (int f = 0; f < 6; f++)
        {
            Vector3 worldPos = Vector3.Transform(FaceCenterLocal(min, max, f), world);
            var box = new BoundingBox(worldPos - Vector3.One * HandleSize, worldPos + Vector3.One * HandleSize);
            float? t = sceneRay.Intersects(box);
            if (t.HasValue && t.Value < bestT)
            {
                bestT = t.Value;
                hoveredFace = f;
            }
        }

        if (hoveredFace != -1 && MouseManager.IsDown(MouseButton.Left))
        {
            BeginDrag(hoveredFace, min, max, world);
        }
    }

    static void BeginDrag(int face, Vector3 min, Vector3 max, Matrix world)
    {
        activeFace = face;
        IsDragging = true;
        dragMin = min;
        dragMax = max;

        Vector3 worldAxis = Vector3.Normalize(Vector3.TransformNormal(faceAxes[face], world));
        Vector3 handleWorldPos = Vector3.Transform(FaceCenterLocal(min, max, face), world);

        (projectedAxis, pixelsPerWorldUnit) = OtherMath.ProjectAxisToScreen(handleWorldPos, worldAxis);

        floatingValue = face switch
        {
            0 => min.X,
            1 => max.X,
            2 => min.Y,
            3 => max.Y,
            4 => min.Z,
            _ => max.Z,
        };

        undoSnapshot = new AllEntitySnapshot(MapTools.Entities);
    }

    static void EndDrag()
    {
        IsDragging = false;
        var snap = undoSnapshot;
        Toolbelt.UndoManager.DoOnUndo(() => snap.Restore());
    }

    public static void Draw(GraphicsDevice graphicsDevice, BasicEffect basicEffect, SpriteBatch spriteBatch)
    {
        if (!TryGetSingleSelectedDecal(out int entity))
        {
            return;
        }

        var ent = MapTools.Entities[entity];
        Vector3 min = ReadPosition(ent, "Decal Min Bounds", -Vector3.One);
        Vector3 max = ReadPosition(ent, "Decal Max Bounds", Vector3.One);
        Matrix world = EntityWorld(ent);

        var ds = graphicsDevice.DepthStencilState;
        graphicsDevice.DepthStencilState = DepthStencilState.Default;

        basicEffect.World = world;
        basicEffect.VertexColorEnabled = false;
        basicEffect.DiffuseColor = Vector3.One * 0.9f;
        basicEffect.Alpha = 1f;

        var edges = BrushOperations.GetDebugEdges(new BoundingBox(min, max));
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, edges, 0, edges.Length / 2);
        }

        graphicsDevice.DepthStencilState = DepthStencilState.None;

        spriteBatch.Begin(blendState: BlendState.NonPremultiplied);
        for (int f = 0; f < 6; f++)
        {
            Vector3 worldPos = Vector3.Transform(FaceCenterLocal(min, max, f), world);
            var screen = graphicsDevice.Viewport.Project(worldPos, Viewport3DCamera.projectionMatrix, Viewport3DCamera.viewMatrix, Viewport3DCamera.worldMatrix);
            if (screen.Z < 0f || screen.Z > 1f)
            {
                continue;
            }

            Vector2 center = new Vector2(screen.X, screen.Y);
            bool active = (IsDragging && f == activeFace) || (!IsDragging && f == hoveredFace);
            var color = active ? new Color(255, 178, 26) : Color.White;

            if (active)
            {
                spriteBatch.DrawCircle(new CircleF(center, 4f), 16, color);
            }
            else
            {
                RenderUtils.DrawDashedCircle(spriteBatch, center, 4f, color, 1.5f, time);
            }
        }
        spriteBatch.End();

        graphicsDevice.DepthStencilState = ds;
    }
}
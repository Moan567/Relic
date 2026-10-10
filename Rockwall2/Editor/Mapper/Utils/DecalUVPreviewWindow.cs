using Avalonia.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Mapper.ViewportManagement;
using System;
using System.Globalization;
using System.Linq;

namespace Rockwall2.Editor.Mapper.Utils;

public static class DecalUVPreviewWindow
{
    const float BoxSize = 140f;
    const float Margin = 20;
    const float HandleSize = 8f;

    enum DragMode { None, Move, Scale }

    public static bool IsDragging { get; private set; }
    public static bool IsHovering { get; private set; }

    static DragMode dragMode = DragMode.None;
    static Vector2 dragStartMouse;
    static Vector2 dragStartOffset;
    static Vector2 dragStartScale;

    static RectangleF BoxRect(EditorViewport vp) => new RectangleF(vp.Width - Margin - BoxSize, Margin, BoxSize, BoxSize);

    static bool ContainsPoint(RectangleF r, Vector2 p) =>
        p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

    static RectangleF RectFromPoints(Vector2 a, Vector2 b)
    {
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        return new RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);
    }

    static Vector2 ReadUv(EntityReference ent, string xName, string yName, Vector2 fallback)
    {
        var rawX = ent.Properties?.FirstOrDefault(p => p.Name == xName).Value;
        var rawY = ent.Properties?.FirstOrDefault(p => p.Name == yName).Value;
        float x = string.IsNullOrEmpty(rawX) ? fallback.X : float.Parse(rawX, CultureInfo.InvariantCulture);
        float y = string.IsNullOrEmpty(rawY) ? fallback.Y : float.Parse(rawY, CultureInfo.InvariantCulture);
        return new Vector2(x, y);
    }

    static void WriteFloat(EntityReference ent, string name, float value)
    {
        string str = value.ToString(CultureInfo.InvariantCulture);
        int i = Array.FindIndex(ent.Properties ?? Array.Empty<EntityProperty>(), p => p.Name == name);
        if (i != -1)
        {
            ent.Properties[i].Value = str;
            return;
        }
        ent.Properties = (ent.Properties ?? Array.Empty<EntityProperty>()).Append(new EntityProperty { Name = name, Value = str }).ToArray();
    }

    public static void Update()
    {
        if (!DecalBoundsGizmo.TryGetSingleSelectedDecal(out int entity))
        {
            IsHovering = false;
            IsDragging = false;
            dragMode = DragMode.None;
            return;
        }

        var ent = MapTools.Entities[entity];
        var vp = ViewportManager.Perspective;
        var mouseLocal = vp.GlobalToLocal(ViewportManager.MouseGlobal);

        if (dragMode != DragMode.None)
        {
            IsHovering = true;
            IsDragging = true;

            if (!MouseManager.IsDown(MouseButton.Left) || mouseLocal == null)
            {
                dragMode = DragMode.None;
                IsDragging = false;
                return;
            }

            Vector2 uvDelta = (mouseLocal.Value - dragStartMouse) / BoxSize;

            if (dragMode == DragMode.Move)
            {
                Vector2 newOffset = dragStartOffset + uvDelta;
                WriteFloat(ent, "Decal UV Offset X", newOffset.X);
                WriteFloat(ent, "Decal UV Offset Y", newOffset.Y);
            }
            else
            {
                Vector2 newScale = dragStartScale + uvDelta;
                WriteFloat(ent, "Decal UV Scale X", newScale.X);
                WriteFloat(ent, "Decal UV Scale Y", newScale.Y);
            }
            return;
        }

        IsHovering = false;

        if (mouseLocal == null)
        {
            return;
        }

        var box = BoxRect(vp);
        if (!ContainsPoint(box, mouseLocal.Value))
        {
            return;
        }

        IsHovering = true;

        Vector2 offset = ReadUv(ent, "Decal UV Offset X", "Decal UV Offset Y", Vector2.Zero);
        Vector2 scale = ReadUv(ent, "Decal UV Scale X", "Decal UV Scale Y", Vector2.One);

        Vector2 boxOrigin = new Vector2(box.X, box.Y);
        Vector2 handlePixel = boxOrigin + (offset + scale) * BoxSize;
        var rectPx = RectFromPoints(boxOrigin + Vector2.Min(offset, offset + scale) * BoxSize, boxOrigin + Vector2.Max(offset, offset + scale) * BoxSize);

        bool overHandle = Vector2.Distance(mouseLocal.Value, handlePixel) < HandleSize;
        bool overRect = ContainsPoint(rectPx, mouseLocal.Value);

        if (MouseManager.IsDown(MouseButton.Left) && (overHandle || overRect))
        {
            dragMode = overHandle ? DragMode.Scale : DragMode.Move;
            dragStartMouse = mouseLocal.Value;
            dragStartOffset = offset;
            dragStartScale = scale;
            IsDragging = true;
        }

        WriteFloat(ent, "Decal UV Offset X", offset.X);
        WriteFloat(ent, "Decal UV Offset Y", offset.Y);

        WriteFloat(ent, "Decal UV Scale X", scale.X);
        WriteFloat(ent, "Decal UV Scale Y", scale.Y);
    }

    public static void Draw(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
    {
        if (!ViewportManager.Rendering.IsPerspective)
        {
            return;
        }
        if (!DecalBoundsGizmo.TryGetSingleSelectedDecal(out int entity))
        {
            return;
        }

        var ent = MapTools.Entities[entity];
        string materialName = ent.Properties?.FirstOrDefault(p => p.Name == "Decal Material").Value;
        if (string.IsNullOrEmpty(materialName) || !GlobalMapData.MaterialNameToIndex.TryGetValue(materialName, out int matIdx))
        {
            return;
        }

        Texture2D texture = GlobalMapData.LoadedMaterials[matIdx].Texture;
        if (texture == null)
        {
            return;
        }

        var vp = ViewportManager.Perspective;
        var box = BoxRect(vp);
        Vector2 boxOrigin = new Vector2(box.X, box.Y);

        Vector2 offset = ReadUv(ent, "Decal UV Offset X", "Decal UV Offset Y", Vector2.Zero);
        Vector2 scale = ReadUv(ent, "Decal UV Scale X", "Decal UV Scale Y", Vector2.One);

        spriteBatch.Begin(blendState: BlendState.NonPremultiplied);

        spriteBatch.FillRectangle(new RectangleF(box.X - 4, box.Y - 4, box.Width + 8, box.Height + 8), new Color(0, 0, 0, 255));
        spriteBatch.Draw(texture, box.ToRectangle(), Color.White);
        spriteBatch.DrawRectangle(box, Color.White, 1f);

        var rectPx = RectFromPoints(boxOrigin + Vector2.Min(offset, offset + scale) * BoxSize, boxOrigin + Vector2.Max(offset, offset + scale) * BoxSize);
        spriteBatch.DrawRectangle(rectPx, Color.Yellow, IsDragging && dragMode == DragMode.Move ? 2.5f : 1.5f);

        Vector2 handlePixel = boxOrigin + (offset + scale) * BoxSize;
        var handleRect = new RectangleF(handlePixel.X - HandleSize * 0.5f, handlePixel.Y - HandleSize * 0.5f, HandleSize, HandleSize);
        spriteBatch.FillRectangle(handleRect, dragMode == DragMode.Scale ? new Color(255, 178, 26) : Color.Yellow);

        spriteBatch.End();
    }
}
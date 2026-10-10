using Avalonia.Input;
using Microsoft.Xna.Framework;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.ViewportManagement;
public class ViewportLayout
{
    public readonly EditorViewport Perspective; // top-left
    public readonly EditorViewport Top;         // top-right
    public readonly EditorViewport Front;       // bottom-left
    public readonly EditorViewport Side;        // bottom-right
    public EditorViewport[] All { get; }

    public float SplitX = 0.5f;  // normalised horizontal divider
    public float SplitY = 0.5f;  // normalised vertical divider

    const int ST = 4;   // splitter thickness px
    const int PAD = 6;   // extra grab padding

    bool _dragX, _dragY;
    int _W, _H;

    public ViewportLayout()
    {
        Perspective = new EditorViewport(ViewportType.Perspective,
            Vector3.Right, Vector3.Up, Vector3.Backward);

        Top = new EditorViewport(ViewportType.Top,
            Vector3.Right, new Vector3(0, 0, -1), Vector3.Up);

        Front = new EditorViewport(ViewportType.Front,
            Vector3.Right, Vector3.Up, Vector3.Backward);

        Side = new EditorViewport(ViewportType.Side,
            Vector3.Forward, Vector3.Up, Vector3.Right);

        All = new[] { Perspective, Top, Front, Side };
    }

    public void Recompute(int w, int h)
    {
        if (w == _W && h == _H) return;
        _W = Math.Max(w, 32);
        _H = Math.Max(h, 32);
        Apply();
    }

    void Apply()
    {
        int lW = Math.Max((int)(_W * SplitX) - ST / 2, 16);
        int rW = Math.Max(_W - lW - ST, 16);
        int tH = Math.Max((int)(_H * SplitY) - ST / 2, 16);
        int bH = Math.Max(_H - tH - ST, 16);

        Perspective.PixelRect = new Rectangle(0, 0, lW, tH);
        Top.PixelRect = new Rectangle(lW + ST, 0, rW, tH);
        Front.PixelRect = new Rectangle(0, tH + ST, lW, bH);
        Side.PixelRect = new Rectangle(lW + ST, tH + ST, rW, bH);

        Viewport3DCamera.RebuildMatrix();
    }

    public int SplitterXPx => (int)(_W * SplitX) - ST / 2;
    public int SplitterYPx => (int)(_H * SplitY) - ST / 2;

    public EditorViewport HitTest(Vector2 pos)
    {
        foreach (var vp in All)
            if (vp.PixelRect.Contains(pos.ToPoint())) return vp;
        return null;
    }

    public bool UpdateDrag(Vector2 mouse, bool down, bool wasDown)
    {
        int mx = (int)mouse.X, my = (int)mouse.Y;
        int sx = SplitterXPx + ST / 2;
        int sy = SplitterYPx + ST / 2;

        if (down && !wasDown)
        {
            if (Math.Abs(mx - sx) <= PAD + ST / 2) _dragX = true;
            if (Math.Abs(my - sy) <= PAD + ST / 2) _dragY = true;
        }
        if (!down) { _dragX = false; _dragY = false; }

        bool changed = false;
        if (_dragX) { SplitX = Math.Clamp((float)mx / _W, 0.1f, 0.9f); changed = true; }
        if (_dragY) { SplitY = Math.Clamp((float)my / _H, 0.1f, 0.9f); changed = true; }
        if (changed) Apply();
        return changed;
    }

    public bool IsDraggingSplitter => _dragX || _dragY;

    public StandardCursorType CursorFor(Vector2 mouse)
    {
        int mx = (int)mouse.X, my = (int)mouse.Y;
        bool nearX = Math.Abs(mx - (SplitterXPx + ST / 2)) <= PAD + ST / 2;
        bool nearY = Math.Abs(my - (SplitterYPx + ST / 2)) <= PAD + ST / 2;
        if (nearX && nearY) return StandardCursorType.SizeAll;
        if (nearX) return StandardCursorType.SizeWestEast;
        if (nearY) return StandardCursorType.SizeNorthSouth;
        return StandardCursorType.Arrow;
    }
}
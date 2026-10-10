using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;
using Relic.Models;
using Relic.Utils;
using System;
using System.Linq;

namespace Rockwall2.Editor.Model;

public class Blend2DCanvas
{
    public event Action<int> EntrySelected;
    public event Action Changed;

    public bool SnapEnabled { get; set; } = false;

    private int selectedIndex = -1;
    private int draggingIndex = -1;

    private const double Padding = 20;
    private const int GridDivisions = 8;
    private const double HitRadius = 11;

    public void Redraw(Canvas canvas, BlendSource node)
    {
        canvas.Children.Clear();
        double w = canvas.Bounds.Width;
        double h = canvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        if (node == null)
        {
            AddHint(canvas, w, h, "Select a sequence first.");
            return;
        }

        var (size, offX, offY) = ComputeSquare(w, h);
        Point ToCanvas(float x, float y) => ToCanvasPoint(node, x, y, size, offX, offY);

        var panelBg = new Rectangle { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromRgb(16, 16, 16)) };
        Canvas.SetLeft(panelBg, offX);
        Canvas.SetTop(panelBg, offY);
        canvas.Children.Add(panelBg);

        var gridBrush = new SolidColorBrush(Color.FromRgb(36, 36, 36));
        var frameBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48));
        var labelBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));

        for (int i = 0; i <= GridDivisions; i++)
        {
            double t = i / (double)GridDivisions;
            double gx = offX + t * size;
            canvas.Children.Add(new Line { StartPoint = new Point(gx, offY), EndPoint = new Point(gx, offY + size), Stroke = gridBrush, StrokeThickness = 1 });
            double gy = offY + t * size;
            canvas.Children.Add(new Line { StartPoint = new Point(offX, gy), EndPoint = new Point(offX + size, gy), Stroke = gridBrush, StrokeThickness = 1 });
        }

        var frame = new Rectangle { Width = size, Height = size, Stroke = frameBrush, StrokeThickness = 1 };
        Canvas.SetLeft(frame, offX);
        Canvas.SetTop(frame, offY);
        canvas.Children.Add(frame);

        AddLabel(canvas, node.MinX.ToString("0.#"), offX, offY + size + 2, labelBrush);
        AddLabel(canvas, node.MaxX.ToString("0.#"), offX + size - 18, offY + size + 2, labelBrush);
        AddLabel(canvas, node.MaxY.ToString("0.#"), offX + size + 3, offY - 2, labelBrush);
        AddLabel(canvas, node.MinY.ToString("0.#"), offX + size + 3, offY + size - 10, labelBrush);

        var originBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90));
        if (node.MinX <= 0 && node.MaxX >= 0)
        {
            double x = ToCanvas(0, node.MinY).X;
            canvas.Children.Add(new Line { StartPoint = new Point(x, offY), EndPoint = new Point(x, offY + size), Stroke = originBrush, StrokeThickness = 1.5 });
        }
        if (node.MinY <= 0 && node.MaxY >= 0)
        {
            double y = ToCanvas(node.MinX, 0).Y;
            canvas.Children.Add(new Line { StartPoint = new Point(offX, y), EndPoint = new Point(offX + size, y), Stroke = originBrush, StrokeThickness = 1.5 });
        }

        if (node.Entries2D.Count == 0)
        {
            AddHint(canvas, w, h, "Click inside the square to place the first point.");
            return;
        }

        if (node.Entries2D.Count >= 3)
        {
            Microsoft.Xna.Framework.Vector2[] points = node.Entries2D.Select(e => new Microsoft.Xna.Framework.Vector2(e.X, e.Y)).ToArray();
            var tris = DelaunayTriangulator.Triangulate(points);
            var lineBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));

            for (int i = 0; i < tris.Count; i++)
            {
                var a = ToCanvas(node.Entries2D[tris[i].a].X, node.Entries2D[tris[i].a].Y);
                var b = ToCanvas(node.Entries2D[tris[i].b].X, node.Entries2D[tris[i].b].Y);
                var c = ToCanvas(node.Entries2D[tris[i].c].X, node.Entries2D[tris[i].c].Y);

                canvas.Children.Add(new Line { StartPoint = a, EndPoint = b, Stroke = lineBrush, StrokeThickness = 1 });
                canvas.Children.Add(new Line { StartPoint = b, EndPoint = c, Stroke = lineBrush, StrokeThickness = 1 });
                canvas.Children.Add(new Line { StartPoint = c, EndPoint = a, Stroke = lineBrush, StrokeThickness = 1 });
            }
        }

        for (int i = 0; i < node.Entries2D.Count; i++)
        {
            var entry = node.Entries2D[i];
            var pos = ToCanvas(entry.X, entry.Y);
            bool selected = i == selectedIndex;

            // The visible dot is only 6-8px, way too small to reliably click. This invisible
            // circle underneath is the actual hit target, roughly 3x the visible radius.
            var hitTarget = new Ellipse { Width = HitRadius * 2, Height = HitRadius * 2, Fill = Brushes.Transparent };
            Canvas.SetLeft(hitTarget, pos.X - HitRadius);
            Canvas.SetTop(hitTarget, pos.Y - HitRadius);

            int captured = i;
            hitTarget.PointerPressed += (s, e) =>
            {
                selectedIndex = captured;
                draggingIndex = captured;
                EntrySelected?.Invoke(captured);
                e.Pointer.Capture(canvas);
                e.Handled = true;
            };
            canvas.Children.Add(hitTarget);

            var dot = new Ellipse
            {
                Width = selected ? 12 : 8,
                Height = selected ? 12 : 8,
                Fill = selected ? new SolidColorBrush(Color.FromRgb(255, 165, 60)) : new SolidColorBrush(Color.FromRgb(80, 170, 230)),
                Stroke = Brushes.White,
                StrokeThickness = selected ? 1.5 : 0.5,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(dot, pos.X - dot.Width / 2);
            Canvas.SetTop(dot, pos.Y - dot.Height / 2);
            canvas.Children.Add(dot);

            var label = new TextBlock { Text = entry.Source?.AnimationName ?? "(none)", FontSize = 9, Foreground = Brushes.White, IsHitTestVisible = false };
            Canvas.SetLeft(label, pos.X + 8);
            Canvas.SetTop(label, pos.Y - 6);
            canvas.Children.Add(label);
        }
    }

    public void AttachInput(Canvas canvas, Func<BlendSource> getNode, Action redraw)
    {
        canvas.PointerPressed += (s, e) =>
        {
            if (draggingIndex >= 0) return;

            var n = getNode();
            if (n == null) return;
            var pt = e.GetPosition(canvas);

            var (x, y) = FromCanvasPoint(n, pt, canvas.Bounds.Width, canvas.Bounds.Height);
            if (x == null) return;

            n.Entries2D.Add(new Blend2DEntry { X = x.Value, Y = y.Value, Source = new BlendSource { Type = BlendSourceType.Clip } });

            selectedIndex = n.Entries2D.Count - 1;
            EntrySelected?.Invoke(selectedIndex);
            Changed?.Invoke();
            redraw();
        };

        canvas.PointerMoved += (s, e) =>
        {
            if (draggingIndex < 0) return;
            var n = getNode();
            if (n == null || draggingIndex >= n.Entries2D.Count) return;

            var pt = e.GetPosition(canvas);
            var (x, y) = FromCanvasPoint(n, pt, canvas.Bounds.Width, canvas.Bounds.Height, clamp: true);
            if (x == null) return;

            var entry = n.Entries2D[draggingIndex];
            entry.X = x.Value;
            entry.Y = y.Value;

            redraw();
        };

        canvas.PointerReleased += (s, e) =>
        {
            if (draggingIndex < 0) return;
            draggingIndex = -1;
            e.Pointer.Capture(null);
            Changed?.Invoke();
        };

        canvas.GetObservable(Canvas.BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ => redraw()));
    }

    public void Select(int index) => selectedIndex = index;

    public void RemoveSelected(BlendSource node)
    {
        if (selectedIndex < 0 || selectedIndex >= node.Entries2D.Count) return;
        node.Entries2D.RemoveAt(selectedIndex);
        selectedIndex = -1;
    }

    private static (double size, double offsetX, double offsetY) ComputeSquare(double w, double h)
    {
        double avail = Math.Max(Math.Min(w, h) - Padding * 2, 1);
        return (avail, (w - avail) / 2, (h - avail) / 2);
    }

    private static Point ToCanvasPoint(BlendSource node, float x, float y, double size, double offX, double offY)
    {
        double spanX = node.MaxX - node.MinX; if (spanX <= 0) spanX = 1;
        double spanY = node.MaxY - node.MinY; if (spanY <= 0) spanY = 1;

        double nx = (x - node.MinX) / spanX;
        double ny = (y - node.MinY) / spanY;

        return new Point(offX + nx * size, offY + size - ny * size);
    }

    private (float? x, float? y) FromCanvasPoint(BlendSource node, Point pt, double w, double h, bool clamp = false)
    {
        var (size, offX, offY) = ComputeSquare(w, h);

        double nx = (pt.X - offX) / size;
        double ny = 1.0 - (pt.Y - offY) / size;

        if (!clamp && (nx < 0 || nx > 1 || ny < 0 || ny > 1)) return (null, null);

        nx = Math.Clamp(nx, 0, 1);
        ny = Math.Clamp(ny, 0, 1);

        if (SnapEnabled)
        {
            nx = Math.Round(nx * GridDivisions) / GridDivisions;
            ny = Math.Round(ny * GridDivisions) / GridDivisions;
        }

        return ((float)(node.MinX + nx * (node.MaxX - node.MinX)), (float)(node.MinY + ny * (node.MaxY - node.MinY)));
    }

    private static void AddHint(Canvas canvas, double w, double h, string text)
    {
        var hint = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)), FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = Math.Min(w - 20, 170), TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(hint, w / 2 - hint.Width / 2);
        Canvas.SetTop(hint, h / 2 - 8);
        canvas.Children.Add(hint);
    }

    private static void AddLabel(Canvas canvas, string text, double x, double y, IBrush brush)
    {
        var lbl = new TextBlock { Text = text, FontSize = 9, Foreground = brush };
        Canvas.SetLeft(lbl, x);
        Canvas.SetTop(lbl, y);
        canvas.Children.Add(lbl);
    }
}
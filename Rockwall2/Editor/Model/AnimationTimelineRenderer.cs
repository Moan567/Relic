using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;
using Relic.Models;
using System;
using System.Collections.Generic;
using static Relic.Models.CModel;

namespace Rockwall2.Editor.Model;

public class AnimationTimelineRenderer
{
    public int SelectedEventIndex { get; private set; } = -1;
    public event Action TimelineChanged;
    public event Action<int> EventSelected;
    public int SelectedIKEventIndex { get; private set; } = -1;
    public event Action<int> IKEventSelected;

    private bool dragging = false;

    private static readonly Dictionary<AnimEventType, Color> evtColors = new()
    {
        { AnimEventType.Attach,              Colors.LimeGreen },
        { AnimEventType.Detach,              Colors.OrangeRed },
        { AnimEventType.SetVisible,          Colors.DeepSkyBlue },
        { AnimEventType.SetHidden,           Colors.SlateGray },
        { AnimEventType.SetBodygroupVisible, Colors.DodgerBlue },
        { AnimEventType.SetBodygroupHidden,  Colors.DimGray },
        { AnimEventType.Custom,              Colors.Gold },
        { AnimEventType.PlaySequence,        Colors.MediumPurple },
    };

    public void SelectEvent(int index) => SelectedEventIndex = index;
    public void SelectIKEvent(int index) => SelectedIKEventIndex = index;

    public void Redraw(Canvas timelineCanvas, Canvas timelineRuler, CAnimDef animDef, int currentFrame)
    {
        timelineCanvas.Children.Clear();
        timelineRuler.Children.Clear();

        if (animDef == null) return;

        int totalFrames = animDef.Animation?.DurationInTicks ?? 1;
        double w = timelineCanvas.Bounds.Width;
        double h = timelineCanvas.Bounds.Height;
        if (w <= 0) return;

        double FrameToX(int f) => f / (double)totalFrames * w;

        DrawRuler(timelineRuler, totalFrames, w, FrameToX);
        DrawEvents(timelineCanvas, animDef, h, FrameToX);
        DrawIKEvents(timelineCanvas, animDef, totalFrames, h, FrameToX);
        DrawPlayhead(timelineCanvas, currentFrame, h, FrameToX);
    }

    private void DrawRuler(Canvas ruler, int totalFrames, double w, Func<int, double> frameToX)
    {
        int labelEvery = totalFrames <= 60 ? 5 : totalFrames <= 300 ? 30 : 60;
        var grayBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));

        for (int f = 0; f <= totalFrames; f += labelEvery)
        {
            double x = frameToX(f);

            ruler.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, 6),
                Stroke = grayBrush,
                StrokeThickness = 1
            });

            var lbl = new TextBlock
            {
                Text = f.ToString(),
                FontSize = 9,
                Foreground = grayBrush
            };
            Canvas.SetLeft(lbl, x + 1);
            Canvas.SetTop(lbl, 0);
            ruler.Children.Add(lbl);
        }
    }

    private static Color ChainColor(string chainName)
    {
        if (string.IsNullOrEmpty(chainName)) return Colors.Gray;

        int hash = 0;
        unchecked
        {
            foreach (char c in chainName) hash = hash * 31 + c;
        }

        double hue = Math.Abs(hash) % 360;
        return HsvToRgb(hue, 0.6, 0.95);
    }
    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r1, g1, b1;

        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return Color.FromRgb((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
    }

    private void DrawIKEvents(Canvas canvas, CAnimDef animDef, int totalFrames, double h, Func<int, double> frameToX)
    {
        if (animDef.IKEvents == null || animDef.IKEvents.Count == 0) return;

        var rowByChain = new Dictionary<string, int>();
        foreach (var ev in animDef.IKEvents)
        {
            string key = ev.ChainName ?? "";
            if (!rowByChain.ContainsKey(key)) rowByChain[key] = rowByChain.Count;
        }

        const double rowHeight = 12;
        const double rowGap = 2;

        for (int i = 0; i < animDef.IKEvents.Count; i++)
        {
            var ev = animDef.IKEvents[i];
            bool selected = i == SelectedIKEventIndex;
            int row = rowByChain[ev.ChainName ?? ""];
            double bandY = h - (row + 1) * (rowHeight + rowGap);

            int freeFrame = ev.FreeFrame < 0 ? totalFrames : ev.FreeFrame;
            double xLock = frameToX(ev.LockFrame);
            double xFree = Math.Max(xLock, frameToX(freeFrame));

            var baseColor = ChainColor(ev.ChainName);

            var band = new Avalonia.Controls.Shapes.Rectangle
            {
                Width = Math.Max(1, xFree - xLock),
                Height = rowHeight,
                Fill = new SolidColorBrush(baseColor) { Opacity = selected ? 0.85 : 0.5 },
                Stroke = new SolidColorBrush(selected ? Colors.White : baseColor),
                StrokeThickness = selected ? 1.5 : 0.75,
            };
            Canvas.SetLeft(band, xLock);
            Canvas.SetTop(band, bandY);
            canvas.Children.Add(band);

            if (ev.FadeInFrames > 0)
            {
                double xFadeInEnd = Math.Min(frameToX(ev.LockFrame + ev.FadeInFrames), xFree);
                var fadeIn = new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = Math.Max(0, xFadeInEnd - xLock),
                    Height = rowHeight,
                    Fill = new SolidColorBrush(Colors.Black) { Opacity = 0.35 },
                };
                Canvas.SetLeft(fadeIn, xLock);
                Canvas.SetTop(fadeIn, bandY);
                canvas.Children.Add(fadeIn);
            }

            if (ev.FadeOutFrames > 0 && ev.FreeFrame >= 0)
            {
                double xFadeOutStart = Math.Max(frameToX(Math.Max(ev.FreeFrame - ev.FadeOutFrames, ev.LockFrame)), xLock);
                var fadeOut = new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = Math.Max(0, xFree - xFadeOutStart),
                    Height = rowHeight,
                    Fill = new SolidColorBrush(Colors.Black) { Opacity = 0.35 },
                };
                Canvas.SetLeft(fadeOut, xFadeOutStart);
                Canvas.SetTop(fadeOut, bandY);
                canvas.Children.Add(fadeOut);
            }

            int captured = i;
            void HandleClick(object s, PointerPressedEventArgs e)
            {
                SelectedIKEventIndex = captured;
                IKEventSelected?.Invoke(captured);
                e.Handled = true;
            }
            band.PointerPressed += HandleClick;

            var lockMarker = new Polygon
            {
                Fill = new SolidColorBrush(baseColor),
                Stroke = selected ? Brushes.White : null,
                StrokeThickness = selected ? 1 : 0,
                Points = new Points
            {
                new Point(xLock - 4, bandY),
                new Point(xLock + 4, bandY),
                new Point(xLock,     bandY - 6),
            }
            };
            lockMarker.PointerPressed += HandleClick;
            canvas.Children.Add(lockMarker);

            if (ev.FreeFrame >= 0)
            {
                var freeMarker = new Polygon
                {
                    Fill = new SolidColorBrush(baseColor),
                    Stroke = selected ? Brushes.White : null,
                    StrokeThickness = selected ? 1 : 0,
                    Points = new Points
                {
                    new Point(xFree - 4, bandY),
                    new Point(xFree + 4, bandY),
                    new Point(xFree,     bandY - 6),
                }
                };
                freeMarker.PointerPressed += HandleClick;
                canvas.Children.Add(freeMarker);
            }

            var lbl = new TextBlock
            {
                Text = ev.ChainName,
                FontSize = 8,
                Foreground = Brushes.White,
            };
            Canvas.SetLeft(lbl, xLock + 2);
            Canvas.SetTop(lbl, bandY + 1);
            canvas.Children.Add(lbl);
        }
    }
    private void DrawEvents(Canvas canvas, CAnimDef animDef, double h, Func<int, double> frameToX)
    {
        if (animDef.Events == null) return;

        for (int i = 0; i < animDef.Events.Count; i++)
        {
            var ev = animDef.Events[i];
            double x = frameToX(ev.Frame);
            bool selected = i == SelectedEventIndex;

            evtColors.TryGetValue(ev.Type, out var col);
            var brush = new SolidColorBrush(col);

            canvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, h),
                Stroke = brush,
                StrokeThickness = selected ? 2 : 1,
                Opacity = selected ? 1.0 : 0.7
            });

            double d = selected ? 6 : 4;
            var diamond = new Polygon
            {
                Fill = brush,
                Stroke = selected ? Brushes.White : brush,
                StrokeThickness = selected ? 1 : 0,
                Points = new Points
                {
                    new Point(x,     0),
                    new Point(x + d, d),
                    new Point(x,     d * 2),
                    new Point(x - d, d),
                }
            };

            int captured = i;
            diamond.PointerPressed += (s, e) =>
            {
                SelectedEventIndex = captured;
                EventSelected?.Invoke(captured);
                e.Handled = true;
            };

            canvas.Children.Add(diamond);
        }
    }

    private void DrawPlayhead(Canvas canvas, int currentFrame, double h, Func<int, double> frameToX)
    {
        double px = frameToX(currentFrame);

        canvas.Children.Add(new Line
        {
            StartPoint = new Point(px, 0),
            EndPoint = new Point(px, h),
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        });

        canvas.Children.Add(new Polygon
        {
            Fill = Brushes.White,
            Points = new Points
            {
                new Point(px - 4, 0),
                new Point(px + 4, 0),
                new Point(px,     7),
            }
        });
    }

    public void AttachInput(Canvas timelineCanvas, Canvas timelineRuler, Func<CAnimDef> getAnimDef, Func<int> getCurrentFrame, Action<int> scrubTo, Action redraw)
    {
        timelineCanvas.PointerPressed += (s, e) =>
        {
            var animDef = getAnimDef();
            if (animDef == null) return;
            var pt = e.GetPosition(timelineCanvas);

            if (e.GetCurrentPoint(timelineCanvas).Properties.IsRightButtonPressed)
            {
                int frame = PosToFrame(pt.X, timelineCanvas.Bounds.Width, animDef);
                if (animDef.Events == null) animDef.Events = new();
                animDef.Events.Add(new CAnimEvent { Frame = frame, Type = AnimEventType.Attach, Slot = "" });
                SelectedEventIndex = animDef.Events.Count - 1;
                EventSelected?.Invoke(SelectedEventIndex);
                TimelineChanged?.Invoke();
                redraw();
                return;
            }

            dragging = true;
            timelineCanvas.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            scrubTo(PosToFrame(pt.X, timelineCanvas.Bounds.Width, animDef));
            e.Pointer.Capture(timelineCanvas);
        };

        timelineCanvas.PointerMoved += (s, e) =>
        {
            if (!dragging) return;
            var animDef = getAnimDef();
            if (animDef == null) return;
            var pt = e.GetPosition(timelineCanvas);
            scrubTo(PosToFrame(pt.X, timelineCanvas.Bounds.Width, animDef));
        };

        timelineCanvas.PointerReleased += (s, e) =>
        {
            dragging = false;
            timelineCanvas.Cursor = Cursor.Default;
            e.Pointer.Capture(null);
        };

        timelineCanvas.GetObservable(Canvas.BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ => redraw()));
    }

    private static int PosToFrame(double x, double width, CAnimDef animDef)
    {
        int total = animDef?.Animation?.DurationInTicks ?? 1;
        return (int)Math.Round(Math.Clamp(x / width * total, 0, total));
    }
}
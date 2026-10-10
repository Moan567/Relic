using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Reactive;
using NVorbis;
using System;
using System.Collections.Generic;

namespace Rockwall2.Editor;

public class SceneTimelineRenderer
{
    public const int FPS = 30;
    public float Duration { get; private set; } = 10f;
    public string AudioPath => audioPath;
    public bool HasAudio => hasWaveform;

    private string audioPath;
    private const int WaveformResolution = 2048;
    private float[] peakMin;
    private float[] peakMax;
    private bool hasWaveform;

    public int TotalFrames => (int)Math.Round(Duration * FPS);
    public float FrameToTime(int frame) => frame / (float)FPS;
    public int TimeToFrame(float t) => (int)Math.Round(t * FPS);
    public float XToTime(double x, double w) => (float)Math.Clamp(x / w * Duration, 0, Duration);
    public double TimeToX(float t, double w) => t / Duration * w;

    public void SetDuration(int startFrame, int endFrame)
    {
        int clamped = Math.Max(1, endFrame - startFrame);
        Duration = clamped / (float)FPS;
    }

    public void LoadAudio(string path)
    {
        hasWaveform = false;
        audioPath = path;

        try
        {
            using var vorbis = new VorbisReader(path);
            Duration = (float)vorbis.TotalTime.TotalSeconds;

            int channels = vorbis.Channels;
            var allFloats = new float[(int)(vorbis.TotalSamples * channels)];
            int read = vorbis.ReadSamples(allFloats, 0, allFloats.Length);
            int monoFrames = read / channels;

            int framesPerBucket = Math.Max(1, monoFrames / WaveformResolution);
            peakMin = new float[WaveformResolution];
            peakMax = new float[WaveformResolution];

            for (int px = 0; px < WaveformResolution; px++)
            {
                int start = px * framesPerBucket * channels;
                int end = Math.Min(start + framesPerBucket * channels, read);
                float min = 0f, max = 0f;
                for (int i = start; i < end; i++)
                {
                    if (allFloats[i] < min) min = allFloats[i];
                    if (allFloats[i] > max) max = allFloats[i];
                }
                peakMin[px] = min;
                peakMax[px] = max;
            }

            hasWaveform = true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Waveform] {e.Message}");
        }
    }

    public void ClearAudio()
    {
        hasWaveform = false;
        Duration = 10f;
    }

    public RenderTargetBitmap RenderWaveformBitmap(double width, double height)
    {
        var bmp = new RenderTargetBitmap(new PixelSize((int)width, (int)height));
        using var ctx = bmp.CreateDrawingContext();

        if (hasWaveform)
        {
            double mid = height / 2;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(70, 160, 90)));

            for (int px = 0; px < (int)width; px++)
            {
                int bucket = Math.Clamp((int)(px / width * WaveformResolution), 0, WaveformResolution - 1);
                double yTop = mid - peakMax[bucket] * mid;
                double yBot = mid - peakMin[bucket] * mid;
                ctx.DrawLine(pen, new Point(px, yTop), new Point(px, yBot));
            }
        }
        else
        {
            ctx.DrawLine(
                new Pen(new SolidColorBrush(Color.FromRgb(50, 50, 50))),
                new Point(0, height / 2),
                new Point(width, height / 2));
        }

        return bmp;
    }
    public void DrawRuler(Canvas ruler, double leftOffset = 0)
    {
        ruler.Children.Clear();
        double w = ruler.Bounds.Width;
        if (w <= 0) return;

        double available = w - leftOffset;
        int totalFrames = TotalFrames;
        int labelEvery = totalFrames <= 60 ? 5
                         : totalFrames <= 150 ? 10
                         : totalFrames <= 300 ? 15
                         : 30;

        var dimBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90));
        var secBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));

        for (int f = 0; f <= totalFrames; f += labelEvery)
        {
            bool isSecond = f % FPS == 0;
            double x = leftOffset + f / (double)totalFrames * available;

            ruler.Children.Add(new Line
            {
                StartPoint = new Point(x, isSecond ? 0 : 4),
                EndPoint = new Point(x, 14),
                Stroke = isSecond ? secBrush : dimBrush,
                StrokeThickness = 1,
            });

            var lbl = new TextBlock
            {
                Text = isSecond ? $"{f / FPS}s" : f.ToString(),
                FontSize = 9,
                Foreground = isSecond ? secBrush : dimBrush,
            };
            Canvas.SetLeft(lbl, x + 1);
            Canvas.SetTop(lbl, 0);
            ruler.Children.Add(lbl);
        }
    }

    public void DrawPlayhead(Canvas canvas, float currentTime, double h)
    {
        double x = TimeToX(currentTime, canvas.Bounds.Width);

        canvas.Children.Add(new Line
        {
            StartPoint = new Point(x, 0),
            EndPoint = new Point(x, h),
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
        });
    }

    public void AttachInput(Canvas canvas, Func<float> getTime, Action<float> scrubTo, Action redraw)
    {
        bool dragging = false;

        canvas.PointerPressed += (s, e) =>
        {
            dragging = true;
            var pt = e.GetPosition(canvas);
            scrubTo(XToTime(pt.X, canvas.Bounds.Width));
            e.Pointer.Capture(canvas);
        };

        canvas.PointerMoved += (s, e) =>
        {
            if (!dragging) return;
            var pt = e.GetPosition(canvas);
            scrubTo(XToTime(pt.X, canvas.Bounds.Width));
        };

        canvas.PointerReleased += (s, e) =>
        {
            dragging = false;
            e.Pointer.Capture(null);
        };

        canvas.GetObservable(Canvas.BoundsProperty)
              .Subscribe(new AnonymousObserver<Rect>(_ => redraw()));
    }
}
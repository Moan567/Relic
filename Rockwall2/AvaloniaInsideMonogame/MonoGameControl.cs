using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Xna.Framework.Graphics;
using Rockwall2.Editor;
using Rockwall2.Editor.Common;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AvaloniaInside.MonoGame;

public sealed class MonoGameControl : Control
{
    public static readonly DirectProperty<MonoGameControl, IBrush> FallbackBackgroundProperty =
        AvaloniaProperty.RegisterDirect<MonoGameControl, IBrush>(
            nameof(FallbackBackground),
            o => o.FallbackBackground,
            (o, v) => o.FallbackBackground = v);

    public static readonly StyledProperty<EditorHost?> HostProperty =
        AvaloniaProperty.Register<MonoGameControl, EditorHost?>(nameof(Host));

    public static readonly StyledProperty<IEditorScene?> SceneProperty =
        AvaloniaProperty.Register<MonoGameControl, IEditorScene?>(nameof(Scene));

    readonly Stopwatch stopwatch = new();

    byte[] bufferData = Array.Empty<byte>();
    WriteableBitmap? bitmap;
    RenderTarget2D? renderTarget;
    int currentWidth = 1;
    int currentHeight = 1;

    readonly DispatcherTimer renderTimer;
    const double TargetFps = 60.0;

    public MonoGameControl()
    {
        Focusable = true;
        renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1.0 / TargetFps)
        };
        renderTimer.Tick += (_, _) =>
        {
            if (IsVisible && IsEffectivelyVisible)
                InvalidateVisual();
        };
    }

    public IBrush FallbackBackground { get; set; } = Brushes.Purple;

    public EditorHost? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    public IEditorScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    private bool init;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!init)
        {
            Scene?.Attach(Host);
            init = true;
        }
        stopwatch.Start();
        RebuildSurface();
        renderTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Scene?.Detach();
        DisposeSurface();
        renderTimer.Stop();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        finalSize = base.ArrangeOverride(finalSize);

        var newWidth = Math.Max(1, (int)Math.Ceiling(finalSize.Width));
        var newHeight = Math.Max(1, (int)Math.Ceiling(finalSize.Height));

        if (newWidth != currentWidth || newHeight != currentHeight)
        {
            currentWidth = newWidth;
            currentHeight = newHeight;
            RebuildSurface();
        }

        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (!IsVisible || !IsEffectivelyVisible) return;

        var host = Host;
        var scene = Scene;

        if (Design.IsDesignMode || host is null || scene is null || renderTarget is null || bitmap is null)
        {
            context.DrawRectangle(FallbackBackground, null, new Rect(Bounds.Size));
            return;
        }

        if (host.GraphicsDevice.GraphicsDeviceStatus != GraphicsDeviceStatus.Normal)
        {
            context.DrawRectangle(FallbackBackground, null, new Rect(Bounds.Size));
            return;
        }

        host.RenderScene(scene, renderTarget, currentWidth, currentHeight);
        CopyToBitmap();
        context.DrawImage(bitmap, new Rect(bitmap.Size), Bounds);
    }

    void RebuildSurface()
    {
        DisposeSurface();

        var host = Host;
        if (host is null || host.GraphicsDevice.GraphicsDeviceStatus != GraphicsDeviceStatus.Normal)
            return;

        renderTarget = new RenderTarget2D(
            host.GraphicsDevice,
            currentWidth,
            currentHeight,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24,
            0,
            RenderTargetUsage.PlatformContents);

        bitmap = new WriteableBitmap(
            new PixelSize(currentWidth, currentHeight),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Opaque);

        Scene?.Resize(currentWidth, currentHeight);
        InvalidateVisual();
    }

    void DisposeSurface()
    {
        renderTarget?.Dispose();
        renderTarget = null;

        bitmap?.Dispose();
        bitmap = null;
    }

    void CopyToBitmap()
    {
        if (renderTarget is null || bitmap is null)
            return;

        using var bitmapLock = bitmap.Lock();

        var byteCount = currentWidth * currentHeight * 4;
        if (bufferData.Length < byteCount)
            Array.Resize(ref bufferData, byteCount);

        renderTarget.GetData(bufferData, 0, byteCount);
        Marshal.Copy(bufferData, 0, bitmapLock.Address, byteCount);
    }
}
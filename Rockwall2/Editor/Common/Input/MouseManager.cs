using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rockwall2.Editor.Common.Input.RawInput;
using SharpHook;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Rockwall2.Editor.Common.Input;

internal static class MouseManager
{
    private const float DragTimeThreshold = 0.15f; // seconds
    private const float DragDistThreshold = 4f;    // pixels
    private const float DoubleClickTime = 0.3f;   // seconds
    private const float DoubleClickDist = 6f;     // pixels

    private static readonly object mlock = new();
    private static Point uiPosition;
    private static readonly HashSet<MouseButton> uiDown = new();
    private static float uiScrollAccum;

    // Capture delta is computed on the UI thread but consumed on the game thread
    private static Vector pendingDelta;   // guarded by _lock
    private static volatile bool skipNextDelta;

    private static HashSet<MouseButton> current = new();
    private static HashSet<MouseButton> previous = new();
    private static HashSet<MouseButton> tripev = new();

    private static readonly Dictionary<MouseButton, DragState> drag = new()
    {
        { MouseButton.Left,   new DragState() },
        { MouseButton.Right,  new DragState() },
        { MouseButton.Middle, new DragState() },
    };
    private static Dictionary<MouseButton, DragState> wasDrag = new()
    {
        { MouseButton.Left,   new DragState() },
        { MouseButton.Right,  new DragState() },
        { MouseButton.Middle, new DragState() },
    };

    private static readonly Dictionary<MouseButton, (float time, Point pos)> lastClick = new();
    private static readonly HashSet<MouseButton> doubleClickedThisFrame = new();
    private static float totalTime;
    public static bool IsAnyDragging =>  IsDragging(MouseButton.Left) || IsDragging(MouseButton.Right) || IsDragging(MouseButton.Middle);

    // Cursor capture
    private static Window? captureWindow;
    private static Point captureCenter;       // window-relative
    private static PixelPoint captureCenterScreen; // cached screen coords for warp

    private static readonly EventSimulator sim = new();

    public static Point Position { get; private set; }
    public static Point PreviousPosition { get; private set; }
    public static Vector Delta { get; private set; }
    public static float ScrollDelta { get; private set; }
    public static int ScrollDirection => Math.Sign(ScrollDelta);
    public static bool IsCaptured = false;

    private static IRawMouseSource rawSource;
    private static bool useRawInput;

    public static void TryEnableRawInput(Window window)
    {
        // Temporary till I determine if it works
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle == null) return;

        var candidate = new WindowsRawMouseSource();
        if (candidate.TryInitialize(platformHandle.Handle))
        {
            rawSource = candidate;
            useRawInput = true;
        }
    }

    public static void Update(float dt)
    {
        totalTime += dt;
        doubleClickedThisFrame.Clear();

        if (useRawInput && IsCaptured)
        {
            tripev = new HashSet<MouseButton>(previous);
            previous = new HashSet<MouseButton>(current);
            PreviousPosition = Position;
            Position = captureCenter;
            Delta = rawSource.ConsumeDelta();

            HashSet<MouseButton> rawDownRaw;
            float rawScrollRaw;
            lock (mlock)
            {
                rawDownRaw = new HashSet<MouseButton>(uiDown);
                rawScrollRaw = uiScrollAccum;
                uiScrollAccum = 0f;
            }
            ScrollDelta = rawScrollRaw;

            foreach (var btn in drag.Keys)
            {
                bool wasDown = current.Contains(btn);
                bool isDown = rawDownRaw.Contains(btn);

                if (isDown && !wasDown)
                {
                    current.Add(btn);
                    drag[btn].Begin(Position);

                    if (lastClick.TryGetValue(btn, out var last))
                    {
                        float dx = (float)(Position.X - last.pos.X);
                        float dy = (float)(Position.Y - last.pos.Y);
                        float dist = MathF.Sqrt(dx * dx + dy * dy);

                        if (totalTime - last.time <= DoubleClickTime && dist <= DoubleClickDist)
                        {
                            doubleClickedThisFrame.Add(btn);
                            lastClick.Remove(btn);
                        }
                        else
                        {
                            lastClick[btn] = (totalTime, Position);
                        }
                    }
                    else
                    {
                        lastClick[btn] = (totalTime, Position);
                    }
                }
                else if (!isDown && wasDown) { current.Remove(btn); drag[btn].End(); }

                drag[btn].Tick(dt, Position);
            }

            return;
        }

        Point rawPos;
        HashSet<MouseButton> rawDown;
        float rawScroll;
        Vector rawDelta;

        lock (mlock)
        {
            rawPos = uiPosition;
            rawDown = new HashSet<MouseButton>(uiDown);
            rawScroll = uiScrollAccum;
            rawDelta = pendingDelta;
            uiScrollAccum = 0f;
            pendingDelta = default;
        }

        previous = new HashSet<MouseButton>(current);
        PreviousPosition = Position;
        ScrollDelta = rawScroll;

        if (IsCaptured)
        {
            // In capture mode, logical position stays pinned at center;
            // delta comes from how far the OS moved before we warped back.
            Position = captureCenter;
            Delta = rawDelta;
            WarpToCenter(); // fires SharpHook mouse-move event, UI thread will skip its delta
            skipNextDelta = true;
        }
        else
        {
            Delta = new Vector(rawPos.X - Position.X, rawPos.Y - Position.Y);
            Position = rawPos;
        }

        foreach (var btn in drag.Keys)
        {
            wasDrag[btn] = drag[btn];
            bool wasDown = current.Contains(btn);
            bool isDown = rawDown.Contains(btn);

            if (isDown && !wasDown)
            {
                current.Add(btn);
                drag[btn].Begin(Position);

                if (lastClick.TryGetValue(btn, out var last))
                {
                    float dx = (float)(Position.X - last.pos.X);
                    float dy = (float)(Position.Y - last.pos.Y);
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (totalTime - last.time <= DoubleClickTime && dist <= DoubleClickDist)
                    {
                        doubleClickedThisFrame.Add(btn);
                        lastClick.Remove(btn);
                    }
                    else
                    {
                        lastClick[btn] = (totalTime, Position);
                    }
                }
                else
                {
                    lastClick[btn] = (totalTime, Position);
                }
            }
            else if (!isDown && wasDown) { current.Remove(btn); drag[btn].End(); }

            drag[btn].Tick(dt, Position);
        }
    }
    public static void Clear()
    {
        if (captureWindow != null) ReleaseCursor(captureWindow);
        lock (mlock)
        {
            uiDown.Clear();
        }
    }
    public static void OnPointerMoved(PointerEventArgs e, Visual relativeTo)
    {
        var pos = e.GetCurrentPoint(relativeTo).Position;

        if (IsCaptured)
        {
            if (skipNextDelta)
            {
                skipNextDelta = false;
                return;
            }
            lock (mlock)
            {
                pendingDelta = new Vector(pos.X - captureCenter.X, pos.Y - captureCenter.Y);
            }
        }
        else
        {
            lock (mlock) { uiPosition = pos; }
        }
    }
    public static void OnPointerPressed(PointerPressedEventArgs e, Visual relativeTo)
    {
        var pt = e.GetCurrentPoint(relativeTo);
        lock (mlock)
        {
            if (pt.Properties.IsLeftButtonPressed) uiDown.Add(MouseButton.Left);
            if (pt.Properties.IsRightButtonPressed) uiDown.Add(MouseButton.Right);
            if (pt.Properties.IsMiddleButtonPressed) uiDown.Add(MouseButton.Middle);
            uiPosition = pt.Position;
        }
    }

    public static void OnPointerReleased(PointerReleasedEventArgs e, Visual relativeTo)
    {
        var kind = e.GetCurrentPoint(relativeTo).Properties.PointerUpdateKind;
        lock (mlock)
        {
            if (kind == PointerUpdateKind.LeftButtonReleased) uiDown.Remove(MouseButton.Left);
            if (kind == PointerUpdateKind.RightButtonReleased) uiDown.Remove(MouseButton.Right);
            if (kind == PointerUpdateKind.MiddleButtonReleased) uiDown.Remove(MouseButton.Middle);
        }
    }

    public static void OnScroll(PointerWheelEventArgs e)
    {
        lock (mlock) { uiScrollAccum += (float)e.Delta.Y; }
    }

    public static bool IsPressed(MouseButton b) => current.Contains(b) && !previous.Contains(b);
    public static bool IsReleased(MouseButton b) => !current.Contains(b) && previous.Contains(b);
    public static bool IsDown(MouseButton b) => current.Contains(b);
    public static bool WasDown(MouseButton b) => previous.Contains(b);
    public static bool IsDoubleClicked(MouseButton b) => doubleClickedThisFrame.Contains(b);
    public static bool IsDragging(MouseButton b) =>
        drag.TryGetValue(b, out var d) && d.IsDragging;
    public static bool WasDragging(MouseButton b) =>
       wasDrag.TryGetValue(b, out var d) && d.WasDragging;

    public static void CaptureCursor(Window window, Microsoft.Xna.Framework.Vector2? windowCenter = null)
    {
        captureWindow = window;
        captureCenter = windowCenter != null ? new Point(windowCenter.Value.X, windowCenter.Value.Y) :
                                               new Point(window.Bounds.Width / 2, window.Bounds.Height / 2);
        // Cache the screen-space position so WarpToCenter() is safe from game thread
        captureCenterScreen = window.PointToScreen(captureCenter);
        
        if (useRawInput)
        {
            rawSource.SetCaptureTarget(captureCenter);
            rawSource.SetRelativeMode(true);
            IsCaptured = true;
            return;
        }

        Dispatcher.UIThread.Post(() => window.Cursor = new Cursor(StandardCursorType.None));
        Dispatcher.UIThread.Post(() => IsCaptured = true);
        WarpToCenter();
        skipNextDelta = true;
    }

    public static void ReleaseCursor(Window window)
    {
        captureWindow = null;
        if (useRawInput) { rawSource.SetRelativeMode(false); IsCaptured = false; return; }

        Dispatcher.UIThread.Post(() => window.Cursor = Cursor.Default);
        Dispatcher.UIThread.Post(() => IsCaptured = false);
    }
    public static void SetCursor(Window window, StandardCursorType type) =>
        Dispatcher.UIThread.Post(() => window.Cursor = new Cursor(type));

    public static void ResetCursor(Window window) =>
        Dispatcher.UIThread.Post(() => window.Cursor = Cursor.Default);

    public static Point GetPositionRelativeTo(Visual relativeTo)
    {
        var root = relativeTo.GetVisualRoot() as Visual;
        if (root == null) return Position;
        var offset = relativeTo.TranslatePoint(new Point(0, 0), root);
        return offset == null ? Position : Position - (Vector)offset.Value;
    }

    public static Microsoft.Xna.Framework.Point GetXnaPositionRelativeTo(Visual relativeTo)
    {
        var p = GetPositionRelativeTo(relativeTo);
        return new Microsoft.Xna.Framework.Point((int)p.X, (int)p.Y);
    }

    private static void WarpToCenter()
    {
        if (captureWindow == null) return;
        sim.SimulateMouseMovement((short)captureCenterScreen.X, (short)captureCenterScreen.Y);
    }

    private class DragState
    {
        private float elapsed;
        private Point origin;
        private bool active;

        public bool IsDragging { get; private set; }
        public bool WasDragging { get; private set; }

        public void Begin(Point origin)
        {
            active = true;
            IsDragging = false;
            elapsed = 0f;
            this.origin = origin;
        }

        public void End()
        {
            active = false;
            IsDragging = false;
        }

        public void Tick(float dt, Point current)
        {
            WasDragging = IsDragging;

            if (!active || IsDragging) return;

            elapsed += dt;
            float dx = (float)(current.X - origin.X);
            float dy = (float)(current.Y - origin.Y);
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (elapsed > DragTimeThreshold || dist > DragDistThreshold)
                IsDragging = true;
        }
    }
}
using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace Rockwall2.Editor.Common.Input.RawInput;

internal sealed class WindowsRawMouseSource : IRawMouseSource
{
    IntPtr hwnd;
    IntPtr oldWndProc;
    WndProcDelegate newWndProc;
    readonly object deltaLock = new();
    int accumX, accumY;
    bool cursorHidden;

    delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    const int GWLP_WNDPROC = -4;
    const uint WM_INPUT = 0x00FF;
    const uint RID_INPUT = 0x10000003;
    const uint RIDEV_INPUTSINK = 0x00000100;
    const uint RIM_TYPEMOUSE = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWMOUSE { public ushort usFlags; public ushort usButtonFlags; public ushort usButtonData; public uint ulRawButtons; public int lLastX; public int lLastY; public uint ulExtraInformation; }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUT { public RAWINPUTHEADER header; public RAWMOUSE mouse; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint cbSize);

    [DllImport("user32.dll")]
    static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr newLong);

    [DllImport("user32.dll")]
    static extern IntPtr CallWindowProc(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int ShowCursor(bool show);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);

    static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newLong) : SetWindowLong32(hWnd, nIndex, newLong);
    POINT targetClient;
    bool hasTarget;

    public void SetCaptureTarget(Point windowRelative)
    {
        targetClient = new POINT { X = (int)windowRelative.X, Y = (int)windowRelative.Y };
        hasTarget = true;
    }

    void WarpToTarget()
    {
        POINT screen;
        if (hasTarget)
        {
            screen = targetClient;
        }
        else
        {
            GetClientRect(hwnd, out var rect);
            screen = new POINT { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
        }
        ClientToScreen(hwnd, ref screen);
        SetCursorPos(screen.X, screen.Y);
    }

    public bool TryInitialize(nint nativeWindowHandle)
    {
        hwnd = nativeWindowHandle;

        var rid = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd };
        if (!RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            return false;

        newWndProc = WndProcHook;
        oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(newWndProc));
        return oldWndProc != IntPtr.Zero;
    }

    IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT) HandleRawInput(lParam);
        return CallWindowProc(oldWndProc, hWnd, msg, wParam, lParam);
    }

    void HandleRawInput(IntPtr lParam)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size) return;

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (raw.header.dwType != RIM_TYPEMOUSE) return;

            lock (deltaLock)
            {
                accumX += raw.mouse.lLastX;
                accumY += raw.mouse.lLastY;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void SetRelativeMode(bool enabled)
    {
        if (enabled == cursorHidden) return;
        cursorHidden = enabled;

        if (enabled)
        {
            ShowCursor(false);
            WarpToTarget();
        }
        else
        {
            ShowCursor(true);
        }

        lock (deltaLock) { accumX = 0; accumY = 0; }
    }

    public Vector ConsumeDelta()
    {
        if (cursorHidden) WarpToTarget();

        lock (deltaLock)
        {
            var v = new Vector(accumX, accumY);
            accumX = 0; accumY = 0;
            return v;
        }
    }

    public void Dispose()
    {
        if (hwnd != IntPtr.Zero && oldWndProc != IntPtr.Zero)
            SetWindowLongPtr(hwnd, GWLP_WNDPROC, oldWndProc);
        if (cursorHidden) SetRelativeMode(false);
    }
}
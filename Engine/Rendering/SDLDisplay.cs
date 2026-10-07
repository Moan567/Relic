using System;
using System.Runtime.InteropServices;

public static class SDLDisplay
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SDL_DisplayMode
    {
        public uint format;
        public int w;
        public int h;
        public int refresh_rate;
        public IntPtr driverdata;
    }

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetNumVideoDisplays();

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetNumDisplayModes(int displayIndex);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetDisplayMode(int displayIndex, int modeIndex, out SDL_DisplayMode mode);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GetCurrentDisplayMode(int displayIndex, out SDL_DisplayMode mode);

    public static int GetMaxRefreshRateForCurrentResolution(int displayIndex = 0)
    {
        SDL_GetCurrentDisplayMode(displayIndex, out var current);
        int max = current.refresh_rate;

        int count = SDL_GetNumDisplayModes(displayIndex);
        for (int i = 0; i < count; i++)
        {
            SDL_GetDisplayMode(displayIndex, i, out var mode);
            if (mode.w == current.w && mode.h == current.h && mode.refresh_rate > max)
                max = mode.refresh_rate;
        }
        return max;
    }
}
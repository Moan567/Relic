using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils;

internal static class WinTimer
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    private static bool applied;

    public static void RequestHighResolution()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (applied) return;

        TimeBeginPeriod(1);
        applied = true;
    }

    public static void ReleaseHighResolution()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!applied) return;

        TimeEndPeriod(1);
        applied = false;
    }
}
internal static class PlatformTimer
{
    public static void RequestHighResolution()
    {
        if (OperatingSystem.IsWindows())
        {
            WinTimer.RequestHighResolution();
        }
        // Linux and macOS: Thread.Sleep is already backed by clock_nanosleep /
        // Mach's high-resolution timers respectively, no equivalent request needed.
    }

    public static void ReleaseHighResolution()
    {
        if (OperatingSystem.IsWindows())
        {
            WinTimer.ReleaseHighResolution();
        }
    }
}
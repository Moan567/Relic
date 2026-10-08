using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation;
internal static class SpinnerConsole
{
    private static readonly char[] Frames = { '|', '/', '-', '\\' };
    private static int frame = 0;
    private static int lastLineLength = 0;

    public static void Report(string label, float progress01)
    {
        char spin = Frames[frame % Frames.Length];
        frame++;

        int pct = (int)(Math.Clamp(progress01, 0f, 1f) * 100f);
        string line = $"{spin} {label} {pct,3}%";

        Console.Write("\r" + line.PadRight(lastLineLength));
        lastLineLength = line.Length;
    }

    public static void Finish()
    {
        Console.Write("\r" + new string(' ', lastLineLength) + "\r");
        lastLineLength = 0;
    }
}
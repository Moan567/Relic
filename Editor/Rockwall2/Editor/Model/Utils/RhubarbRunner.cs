using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Model.Utils;
public static class RhubarbRunner
{
    static string ExePath
    {
        get
        {
            string os = OperatingSystem.IsWindows() ? "win"
                      : OperatingSystem.IsMacOS() ? "mac"
                      : "linux";

            string exe = OperatingSystem.IsWindows() ? "rhubarb.exe" : "rhubarb";

            return Path.Combine(AppContext.BaseDirectory,
                "Assets", "Tools", "rhubarb", os, exe);
        }
    }

    public static bool IsAvailable => File.Exists(ExePath);

    // Returns list of (startTime, endTime, mouthCode) or null on failure
    public static async Task<List<(float start, float end, string value)>> Detect(
        string oggPath, string transcriptPath = null)
    {
        if (!IsAvailable) return null;

        string args = $"--extendedShapes GHX --exportFormat json \"{oggPath}\"";
        if (transcriptPath != null && File.Exists(transcriptPath))
            args += $" --dialogFile \"{transcriptPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
        };

        using var proc = Process.Start(psi);
        string json = proc.StandardOutput.ReadToEnd();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            Console.WriteLine($"[Rhubarb] {proc.StandardError.ReadToEnd()}");
            return null;
        }

        return ParseCues(json);
    }

    static List<(float, float, string)> ParseCues(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var cues = doc.RootElement.GetProperty("mouthCues");
        var result = new List<(float, float, string)>();

        foreach (var cue in cues.EnumerateArray())
        {
            float start = cue.GetProperty("start").GetSingle();
            float end = cue.GetProperty("end").GetSingle();
            string val = cue.GetProperty("value").GetString();
            result.Add((start, end, val));
        }

        return result;
    }
}
using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Rendering;

public enum TimingSection
{
    Update,
    Skybox3DRender,
    PlanarReflections,
    RenderMapDepth,
    RenderMap,
    Terrain,
    TransparentModels,
    Entities,
    DecalsAndParticles,
    RenderToRefraction,
    Present,
    Count
}

public static class RenderTimings
{
    private static readonly long[] accumTicks = new long[(int)TimingSection.Count];
    private static readonly long[] displayTicks = new long[(int)TimingSection.Count];
    private static readonly int[] accumCounts = new int[(int)TimingSection.Count];
    private static readonly int[] displayCounts = new int[(int)TimingSection.Count];

    private static int frameAccum;
    private static int displayFrameCount;

    private static readonly string[] names =
    {
        "Update",
        "3D Skybox",
        "Planar Reflections",
        "RenderMapDepth",
        "RenderMap",
        "Terrain",
        "Transparent Models",
        "Entities",
        "Decals & Particles",
        "RenderToRefraction",
        "Present",
    };

    private static double windowSeconds;

    public readonly struct Scope : IDisposable
    {
        private readonly TimingSection section;
        private readonly long startTicks;
        private readonly bool active;

        public Scope(TimingSection section)
        {
            active = RenderEngine.ShowTimings;
            this.section = section;
            startTicks = active ? Stopwatch.GetTimestamp() : 0;
        }

        public void Dispose()
        {
            if (!active)
            {
                return;
            }

            long endTicks = Stopwatch.GetTimestamp();
            accumTicks[(int)section] += endTicks - startTicks;
            accumCounts[(int)section]++;
        }
    }

    public static Scope Section(TimingSection section)
    {
        return new Scope(section);
    }

    public static void Tick(GameTime time)
    {
        windowSeconds += time.ElapsedGameTime.TotalSeconds;
        frameAccum++;

        if (windowSeconds >= 1.0)
        {
            for (int i = 0; i < accumTicks.Length; i++)
            {
                displayTicks[i] = accumTicks[i];
                displayCounts[i] = accumCounts[i];
                accumTicks[i] = 0;
                accumCounts[i] = 0;
            }

            displayFrameCount = frameAccum;
            frameAccum = 0;
            windowSeconds = 0;
        }
    }

    public static void DrawImGui(double currentFps)
    {
        double freq = Stopwatch.Frequency;
        long totalTicks = 0;
        int frames = Math.Max(displayFrameCount, 1);

        for (int i = 0; i < names.Length; i++)
        {
            double totalMs = (displayTicks[i] / freq) * 1000.0;
            double perFrameMs = totalMs / frames;
            double perCallMs = displayCounts[i] > 0 ? totalMs / displayCounts[i] : 0;
            double hypotheticalFps = perFrameMs > 0 ? 1000.0 / perFrameMs : 0;

            ImGui.Text($"{names[i]}: {perFrameMs:0.0000} ms/frame");

            totalTicks += displayTicks[i];
        }

        double sumMs = (totalTicks / freq) * 1000.0;
        double sumPerFrameMs = sumMs / frames;
        double sumHypotheticalFps = sumPerFrameMs > 0 ? 1000.0 / sumPerFrameMs : 0;

        ImGui.Separator();
        ImGui.Text($"Measured total: {sumPerFrameMs:0.0000} ms/frame ({frames} frames) -> {sumHypotheticalFps:0} fps alone");

        double actualFrameBudgetMs = currentFps > 0 ? 1000.0 / currentFps : 0;
        ImGui.Text($"Unaccounted: {(actualFrameBudgetMs - sumPerFrameMs):0.0000} ms/frame");

        ImGui.Separator();

        ImGui.Text($"Stats this frame:");
        ImGui.Text($"{MainEngine.CurrentRealtimeLights.Count} realtime lights active");
    }
}
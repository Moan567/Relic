using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Rendering;
public static class LightStyleRegistry
{
    private static readonly Dictionary<string, LightStyle> styles = new();

    static LightStyleRegistry()
    {
        Register(new LightStyle
        {
            Name = "flicker1",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 1.00f, blend: false),
                new StyleKeyframe(0.05f, 1.00f, blend: false),
                new StyleKeyframe(0.10f, 0.80f, blend: false),
                new StyleKeyframe(0.15f, 1.00f, blend: false),
                new StyleKeyframe(0.25f, 1.00f, blend: false),
                new StyleKeyframe(0.30f, 0.60f, blend: false),
                new StyleKeyframe(0.35f, 1.00f, blend: false),
                new StyleKeyframe(0.50f, 1.00f, blend: false),
            }
        });

        // Stronger flicker.
        Register(new LightStyle
        {
            Name = "flicker2",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 1.00f, blend: false),
                new StyleKeyframe(0.04f, 0.20f, blend: false),
                new StyleKeyframe(0.08f, 1.00f, blend: false),
                new StyleKeyframe(0.10f, 0.10f, blend: false),
                new StyleKeyframe(0.16f, 1.00f, blend: false),
                new StyleKeyframe(0.20f, 0.00f, blend: false),
                new StyleKeyframe(0.24f, 1.00f, blend: false),
                new StyleKeyframe(0.40f, 1.00f, blend: false),
            }
        });

        // Small gentle flame 
        Register(new LightStyle
        {
            Name = "candle",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 0.90f, new Color(255, 244, 214)),
                new StyleKeyframe(0.15f, 1.00f, new Color(255, 238, 200)),
                new StyleKeyframe(0.30f, 0.85f, new Color(255, 224, 180)),
                new StyleKeyframe(0.45f, 0.95f, new Color(255, 240, 205)),
                new StyleKeyframe(0.60f, 0.80f, new Color(255, 220, 175)),
                new StyleKeyframe(0.80f, 1.00f, new Color(255, 244, 214)),
            }
        });

        // Bonfire
        Register(new LightStyle
        {
            Name = "fire",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 1.00f, new Color(255, 160, 60),  blend: false),
                new StyleKeyframe(0.06f, 0.70f, new Color(255, 130, 40)),
                new StyleKeyframe(0.14f, 1.00f, new Color(255, 180, 70),  blend: false),
                new StyleKeyframe(0.20f, 0.50f, new Color(255, 100, 30)),
                new StyleKeyframe(0.30f, 1.00f, new Color(255, 170, 60),  blend: false),
                new StyleKeyframe(0.38f, 0.85f, new Color(255, 150, 50)),
                new StyleKeyframe(0.50f, 1.00f, new Color(255, 160, 60),  blend: false),
            }
        });

        // Smooth symmetric breathing pulse.
        Register(new LightStyle
        {
            Name = "pulse",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 0.25f),
                new StyleKeyframe(0.75f, 1.00f),
                new StyleKeyframe(1.50f, 0.25f),
            }
        });

        // Hard, fast on/off.
        Register(new LightStyle
        {
            Name = "strobe",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 1.00f, blend: false),
                new StyleKeyframe(0.05f, 0.00f, blend: false),
                new StyleKeyframe(0.10f, 1.00f, blend: false),
            }
        });

        // Fluorescent tube.
        Register(new LightStyle
        {
            Name = "buzz",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 0.00f, blend: false),
                new StyleKeyframe(0.05f, 1.00f, blend: false),
                new StyleKeyframe(0.08f, 0.00f, blend: false),
                new StyleKeyframe(0.10f, 1.00f, blend: false),
                new StyleKeyframe(0.11f, 0.30f, blend: false),
                new StyleKeyframe(0.16f, 1.00f, blend: false),
                new StyleKeyframe(0.20f, 0.00f, blend: false),
                new StyleKeyframe(0.35f, 1.00f, blend: false),
                new StyleKeyframe(0.40f, 0.40f, blend: false),
                new StyleKeyframe(0.55f, 1.00f),
                new StyleKeyframe(1.00f, 1.00f),
            }
        });

        // Alternating warning flash .
        Register(new LightStyle
        {
            Name = "emergency",
            Loop = true,
            Keyframes = new[]
            {
                new StyleKeyframe(0.00f, 1.00f, new Color(255, 40, 40), blend: false),
                new StyleKeyframe(0.15f, 0.00f, Color.White,            blend: false),
                new StyleKeyframe(0.30f, 1.00f, new Color(60, 80, 255), blend: false),
                new StyleKeyframe(0.45f, 0.00f, Color.White,            blend: false),
                new StyleKeyframe(0.60f, 1.00f, new Color(60, 80, 255), blend: false),
            }
        });

        Register(new LightStyle
        {
            Name = "fadein",
            Loop = false,
            Keyframes = new[]
            {
                new StyleKeyframe(0.0f, 0.0f),
                new StyleKeyframe(1.0f, 1.0f),
            }
        });

        Register(new LightStyle
        {
            Name = "fadeout",
            Loop = false,
            Keyframes = new[]
            {
                new StyleKeyframe(0.0f, 1.0f),
                new StyleKeyframe(1.0f, 0.0f),
            }
        });
    }

    public static void Register(LightStyle style) => styles[style.Name] = style;

    public static bool TryGet(string name, out LightStyle style)
    {
        if (string.IsNullOrEmpty(name)) { style = default; return false; }
        return styles.TryGetValue(name, out style);
    }
}
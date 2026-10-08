using System;
using System.Collections.Generic;
using System.Text;

namespace Chisel.Models
{
    public enum BlendSourceType
    {
        Clip,
        Blend1D,
        Blend2D
    }

    public class BlendSource
    {
        public string Name { get; set; }
        public BlendSourceType Type { get; set; }

        // Clip is just a single animation
        public string AnimationName { get; set; }

        // Blend 1D is just a flat array of anims with a single param
        public string ParamName { get; set; }
        public List<Blend1DEntry> Entries1D { get; set; } = new();

        // Blend 2D is 2 params
        public string ParamNameX { get; set; }
        public string ParamNameY { get; set; }
        public float MinX { get; set; } = -1;
        public float MaxX { get; set; } = 1;
        public float MinY { get; set; } = -1;
        public float MaxY { get; set; } = 1;
        public List<Blend2DEntry> Entries2D { get; set; } = new();
    }

    public class Blend1DEntry
    {
        public float Value { get; set; }
        public BlendSource Source { get; set; }
    }
    public class Blend2DEntry
    {
        public float X { get; set; }
        public float Y { get; set; }
        public BlendSource Source { get; set; }
    }
}

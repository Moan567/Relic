using System;

namespace Chisel.Models
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class EditorFieldAttribute : Attribute
    {
        public string Label { get; }
        public string Group { get; set; } = "General";
        public float Min { get; set; } = float.NaN;
        public float Max { get; set; } = float.NaN;
        public int Order { get; set; }

        public EditorFieldAttribute(string label = null) => Label = label;
    }
}
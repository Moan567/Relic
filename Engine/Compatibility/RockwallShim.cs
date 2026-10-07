using System;

namespace Rockwall
{
    // Minimal shim for Rockwall editor integration so the engine can compile
    // without depending on the external Rockwall package. These are no-op
    // placeholders and should be replaced by the new editor integration later.

    public enum EntityPropertyType
    {
        String,
        Bool,
        Float,
        Direction,
        Int
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    public class ExposeEntityPropertyAttribute : Attribute
    {
        public string Name { get; }
        public EntityPropertyType Type { get; }
        public string Description { get; }
        public string? defaultValue { get; set; }

        public ExposeEntityPropertyAttribute(string name, EntityPropertyType type, string description)
        {
            Name = name; Type = type; Description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class EntityDescriptorAttribute : Attribute
    {
        public EntityDescriptorAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RegisterEntityInputsAttribute : Attribute
    {
        public string[] Inputs { get; }
        public RegisterEntityInputsAttribute(params string[] inputs) { Inputs = inputs; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RegisterEntityOutputsAttribute : Attribute
    {
        public string[] Outputs { get; }
        public RegisterEntityOutputsAttribute(params string[] outputs) { Outputs = outputs; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class EntityVisualizeAttribute : Attribute
    {
        public Type VisualizerType { get; }
        public EntityVisualizeAttribute(Type visualizerType) { VisualizerType = visualizerType; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class VisualizerPropertyAttribute : Attribute
    {
        public string PropertyName { get; }
        public string Label { get; }
        public VisualizerPropertyAttribute(string propertyName, string label) { PropertyName = propertyName; Label = label; }
    }

    // Lightweight EntityManager facade used in a few places; implement as needed later.
    public static class EntityManager
    {
        public static object? FindSingleEntityByName(string? name) => null;
    }
}

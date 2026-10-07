using System;

namespace Rockwall
{
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
    public class EntityDescriptorAttribute : Attribute { public EntityDescriptorAttribute() { } }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RegisterEntityInputsAttribute : Attribute { public RegisterEntityInputsAttribute(params string[] inputs) { } }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RegisterEntityOutputsAttribute : Attribute { public RegisterEntityOutputsAttribute(params string[] outputs) { } }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class EntityVisualizeAttribute : Attribute { public EntityVisualizeAttribute(Type t) { } }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class VisualizerPropertyAttribute : Attribute { public VisualizerPropertyAttribute(string name, string label) { } }

    public static class EntityManager
    {
        public static object? FindSingleEntityByName(string? name) => null;
    }
}

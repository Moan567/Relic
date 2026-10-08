using Chisel;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ToolsUtilities;

namespace Engine.Compilation
{
    [System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public class RegisterEntityOutputs : Attribute
    {
        public string[] names;
        public RegisterEntityOutputs(params string[] names)
        {
            this.names = names;
        }
    }
    [System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public class RegisterEntityInputs : Attribute
    {
        public string[] names;
        public RegisterEntityInputs(params string[] names)
        {
            this.names = names;
        }
    }
    // Editor-only bounding box override, used by the placement gizmo and default selection box.
    [System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class EntityBounds : Attribute
    {
        public Vector3 min, max;
        public EntityBounds(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            min = new Vector3(minX, minY, minZ);
            max = new Vector3(maxX, maxY, maxZ);
        }
    }
    // Ctrl+D on an entity of this class auto-wires "next"/"previous" targetname properties
    // between the source and the duplicate, chaining them.
    [System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class AutoConnectOnDuplicate : Attribute
    {
        public string next, previous;
        public AutoConnectOnDuplicate(string next, string previous = null)
        {
            this.next = next;
            this.previous = previous;
        }
    }
    // Draws an EntityVisualizer gizmo for this class in the editor.
    [System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class EntityVisualize : Attribute
    {
        public Type visualizerType;
        public EntityVisualize(Type visualizerType)
        {
            this.visualizerType = visualizerType;
        }
    }
    // Feeds an entity property's value into one field of the class's EntityVisualize type.
    // fieldName must match a field on that visualizer type exactly (use nameof()).
    [System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public class VisualizerProperty : Attribute
    {
        public string fieldName, propertyName;
        public VisualizerProperty(string fieldName, string propertyName)
        {
            this.fieldName = fieldName;
            this.propertyName = propertyName;
        }
    }
    [System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public class VisualizerLiteral : Attribute
    {
        public string fieldName, value;
        public VisualizerLiteral(string fieldName, string value)
        {
            this.fieldName = fieldName;
            this.value = value;
        }
    }
    public static class EntityCompiler
    {
        public static Dictionary<string, Type> EntityLookupTable = new Dictionary<string, Type>();
        private static List<string> entityNames = new List<string>();
        private static Dictionary<string, EntityClassMetadata> classMetadata = [];

        static IEnumerable<Type> GetTypesWithEntityAttribute(Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(EntityDescriptor), true).Length > 0)
                {
                    yield return type;
                }
            }
        }
        public static void CompileAllEntities(Assembly assembly, string fullpath, bool write)
        {
            IEnumerable<Type> entities = GetTypesWithEntityAttribute(assembly);

            foreach (var entity in entities)
            {
                //Register the entities
                var entDesc = entity.GetCustomAttributes(typeof(EntityDescriptor), false);
                if (entDesc.Length == 0) continue;

                EntityLookupTable.Add(entity.Name, entity);
                entityNames.Add(entity.Name);

                var meta = new EntityClassMetadata();

                //Register their properties, in all three flavors. Yummy
                foreach (var descriptor in entity.GetCustomAttributes(typeof(ExposeEntityProperty), true).Cast<ExposeEntityProperty>())
                {
                    meta.Properties.Add(new EntityPropertyDescriptor
                    {
                        Name = descriptor.name,
                        Type = descriptor.type,
                        Hint = descriptor.hint,
                        Category = descriptor.category,
                        DefaultValue = descriptor.defaultValue,
                        Min = descriptor.min,
                        Max = descriptor.max
                    });
                }
                foreach (var descriptor in entity.GetCustomAttributes(typeof(ExposeEntityPropertyEnum), true).Cast<ExposeEntityPropertyEnum>())
                {
                    meta.Properties.Add(new EntityPropertyDescriptor
                    {
                        Name = descriptor.name,
                        Type = EntityPropertyType.Enum,
                        Hint = descriptor.hint,
                        Category = descriptor.category,
                        Options = descriptor.options
                    });
                }
                foreach (var descriptor in entity.GetCustomAttributes(typeof(ExposeEntityPropertyTarget), true).Cast<ExposeEntityPropertyTarget>())
                {
                    meta.Properties.Add(new EntityPropertyDescriptor
                    {
                        Name = descriptor.name,
                        Type = EntityPropertyType.EntityTarget,
                        Hint = descriptor.hint,
                        Category = descriptor.category,
                        TargetFilter = descriptor.classFilter
                    });
                }

                meta.DefaultProperties = meta.Properties
                    .Where(p => !string.IsNullOrEmpty(p.DefaultValue))
                    .Select(p => new EntityProperty { Name = p.Name, Value = p.DefaultValue })
                    .ToArray();

                //Register their inputs/outputs
                foreach (var reg in entity.GetCustomAttributes(typeof(RegisterEntityInputs), true).Cast<RegisterEntityInputs>())
                    meta.Inputs.AddRange(reg.names);

                foreach (var reg in entity.GetCustomAttributes(typeof(RegisterEntityOutputs), true).Cast<RegisterEntityOutputs>())
                    meta.Outputs.AddRange(reg.names);

                //Editor-only presentation metadata
                if (entity.GetCustomAttribute(typeof(EntityBounds), true) is EntityBounds bounds)
                {
                    meta.BoundsMin = bounds.min;
                    meta.BoundsMax = bounds.max;
                }

                if (entity.GetCustomAttribute(typeof(AutoConnectOnDuplicate), true) is AutoConnectOnDuplicate link)
                    meta.Link = new EntityLinkDescriptor { Next = link.next, Previous = link.previous };

                if (entity.GetCustomAttribute(typeof(EntityVisualize), true) is EntityVisualize visualize)
                {
                    var fieldToProperty = new Dictionary<string, string>();
                    foreach (var vp in entity.GetCustomAttributes(typeof(VisualizerProperty), true).Cast<VisualizerProperty>())
                        fieldToProperty[vp.fieldName] = vp.propertyName;

                    var fieldToLiteral = new Dictionary<string, string>();
                    foreach (var vl in entity.GetCustomAttributes(typeof(VisualizerLiteral), true).Cast<VisualizerLiteral>())
                        fieldToLiteral[vl.fieldName] = vl.value;

                    meta.Visualizer = new EntityVisualizerBinding
                    {
                        VisualizerType = visualize.visualizerType.Name,
                        FieldToProperty = fieldToProperty,
                        FieldToLiteral = fieldToLiteral
                    };
                }

                classMetadata[entity.Name] = meta;
            }
            if (!write) return;

            if (!Directory.Exists($"{fullpath}/Data")) Directory.CreateDirectory($"{fullpath}/Data");
            if (File.Exists($"{fullpath}/Data/entMETA.gff")) File.Delete($"{fullpath}/Data/entMETA.gff");
            if (File.Exists($"{fullpath}/Data/entnme.edt")) File.Delete($"{fullpath}/Data/entnme.edt");
            if (File.Exists($"{fullpath}/Data/entlid.edt")) File.Delete($"{fullpath}/Data/entlid.edt");

            File.WriteAllText($"{fullpath}/Data/entMETA.gff", JsonConvert.SerializeObject(classMetadata, Formatting.None));
            File.WriteAllText($"{fullpath}/Data/entlid.edt", JsonConvert.SerializeObject(EntityLookupTable, Formatting.None));
            File.WriteAllText($"{fullpath}/Data/entnme.edt", JsonConvert.SerializeObject(entityNames.ToArray(), Formatting.None));
        }
        public static void WriteDefPaths(string fullpath)
        {
            EntityDataIndex.Write($"{fullpath}/Data");
        }
        public static void ReadAllEntities(string path)
        {
            EntityLookupTable = JsonConvert.DeserializeObject<Dictionary<string, Type>>(File.ReadAllText($"{path}/Data/entlid.edt"));
            entityNames = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText($"{path}/Data/entnme.edt"));
        }
        public static WorldEntity GetFromClassname(string classname)
        {
            return (WorldEntity)Activator.CreateInstance(EntityLookupTable[classname]);
        }

        public record EntityEntry(string classname, Type type);

        public static EntityEntry[] GetAllRegisteredEntities()
        {
            return [.. EntityLookupTable.Select(kv=>new EntityEntry(kv.Key,kv.Value))];
        }
    }
}

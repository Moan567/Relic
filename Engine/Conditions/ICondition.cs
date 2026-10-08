using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Conditions;
public interface ICondition
{
    static virtual int ID { get; } = 0;
    static virtual string Name { get; }
    void Update(WorldEntity parent);
    void OnAdded(WorldEntity parent);
    void OnRemoved(WorldEntity parent);
}
public abstract class Condition<TSelf> : ICondition
    where TSelf : Condition<TSelf>, ICondition
{
    internal static void RegisterSelf()
    {
        ConditionManager.RegisterCondition<TSelf>(TSelf.ID, TSelf.Name);
    }
    public abstract void Update(WorldEntity parent);
    public abstract void OnAdded(WorldEntity parent);
    public abstract void OnRemoved(WorldEntity parent);
}
public static class ConditionManager
{
    private record ConditionEntry(int ID, Type Type);

    private static readonly Dictionary<Type, ConditionEntry> byType = new();
    private static readonly Dictionary<int, ConditionEntry> byID = new();
    private static readonly Dictionary<string, ConditionEntry> byName = new();

    public static void RegisterCondition<TSelf>(int id, string name)
        where TSelf : Condition<TSelf>, ICondition
    {
        var entry = new ConditionEntry(id, typeof(TSelf));
        byType[typeof(TSelf)] = entry;
        byID[id] = entry;
        if (!string.IsNullOrEmpty(name)) byName[name] = entry;
    }

    public static int GetID<TSelf>()
        where TSelf : Condition<TSelf>, ICondition
        => byType[typeof(TSelf)].ID;

    public static int GetID(Type type)
        => byType[type].ID;

    public static Type GetType(int id)
        => byID[id].Type;

    public static Type GetTypeByName(string name) =>
        byName.TryGetValue(name, out var entry) ? entry.Type : null;
    public static void RegisterAll()
    {
        var baseType = typeof(Condition<>);
        foreach (var type in Assembly.GetEntryAssembly().GetTypes())
        {
            if (type.IsAbstract) continue;

            var b = type.BaseType;
            if (b is { IsGenericType: true } && b.GetGenericTypeDefinition() == baseType)
            {
                var method = b.GetMethod("RegisterSelf",
                    BindingFlags.Static | BindingFlags.NonPublic);
                method.Invoke(null, null);
            }
        }
    }
}
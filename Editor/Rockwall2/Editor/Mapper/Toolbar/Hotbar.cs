using Newtonsoft.Json;
using Rockwall2.Editor.Common.Toolbar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Toolbar;
public static class Hotbar
{
    public const int SlotCount = 9;

    private static readonly Dictionary<string, ToolButtonDef> pool = new();
    private static readonly string?[] assignments = new string?[SlotCount];

    public static event Action<int>? SlotAssigned; // fires on assign/clear, for UI refresh
    public static event Action<int>? SlotActivated; // fires when a hotkey triggers a slot

    public static void RegisterAvailable(IEnumerable<ToolCategory> categories)
    {
        foreach (var def in categories.SelectMany(c => c.Buttons))
            pool[def.Id] = def;
    }

    public static IReadOnlyCollection<ToolButtonDef> AllAvailable => pool.Values;

    public static ToolButtonDef? GetSlot(int slot) =>
        slot is >= 1 and <= SlotCount && assignments[slot - 1] is { } id && pool.TryGetValue(id, out var def)
            ? def
            : null;

    public static void AssignSlot(int slot, string? toolId)
    {
        if (slot is < 1 or > SlotCount) return;
        assignments[slot - 1] = toolId;
        SlotAssigned?.Invoke(slot);
    }

    public static void ClearSlot(int slot) => AssignSlot(slot, null);

    public static void Activate(int slot)
    {
        if (GetSlot(slot)?.Command is { } cmd && cmd.CanExecute(null))
            cmd.Execute(null);
        SlotActivated?.Invoke(slot);
    }

    public static string SerializeAssignments() => JsonConvert.SerializeObject(assignments);
    public static void LoadAssignments(string json)
    {
        var loaded = JsonConvert.DeserializeObject<string?[]>(json);
        if (loaded == null) return;
        for (int i = 0; i < SlotCount && i < loaded.Length; i++)
        {
            assignments[i] = loaded[i];
            SlotAssigned?.Invoke(i + 1);
        }
    }
}
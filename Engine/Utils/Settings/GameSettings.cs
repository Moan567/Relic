using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Engine.Utils.Settings;

public static class OptionsTabs
{
    public const string Gameplay = "Gameplay";
    public const string Video = "Video";
    public const string Audio = "Audio";
    public const string Controls = "Controls";
}
public enum OptionsTab
{
    Gameplay,
    Video,
    Audio,
    Controls
}
public static class GameSettings
{
    public static string GameName = "ChiselGame";
    private static string savePath => $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,Environment.SpecialFolderOption.Create)}/{GameName}/game.cfg";

    private static readonly List<string> tabs = new();
    private static readonly Dictionary<string, List<ListedOption>> options = new();

    /// <summary>Ordered list of all registered tab names.</summary>
    public static IReadOnlyList<string> RegisteredTabs => tabs.AsReadOnly();

    static GameSettings()
    {
        foreach (OptionsTab t in Enum.GetValues<OptionsTab>())
            RegisterTab(t.ToString());
    }

    /// <summary>
    /// Register a custom tab. Built-in tabs are registered automatically.
    /// Tabs appear in the order they are registered.
    /// </summary>
    public static void RegisterTab(string tabName)
    {
        if (!tabs.Contains(tabName))
        {
            tabs.Add(tabName);
            options[tabName] = new List<ListedOption>();
        }
    }

    /// <summary>Convenience overload for built-in <see cref="OptionsTab"/> values.</summary>
    public static void RegisterTab(OptionsTab tab) => RegisterTab(tab.ToString());

    /// <summary>
    /// Add an option to a tab. If the tab doesn't exist yet it is created.
    /// Options appear in the order they are registered within their tab.
    /// </summary>
    public static void RegisterOption(string tabName, ListedOption option)
    {
        RegisterTab(tabName);
        options[tabName].Add(option);
    }
    /// <summary>Convenience overload for built-in <see cref="OptionsTab"/> values.</summary>
    public static void RegisterOption(OptionsTab tab, ListedOption option)
        => RegisterOption(tab.ToString(), option);

    /// <summary>Returns the ordered list of options for the given tab name.</summary>
    public static IReadOnlyList<ListedOption> GetOptions(string tabName)
        => options.TryGetValue(tabName, out var list)
            ? list.AsReadOnly()
            : (IReadOnlyList<ListedOption>)Array.Empty<ListedOption>();

    /// <summary>Convenience overload for built-in <see cref="OptionsTab"/> values.</summary>
    public static IReadOnlyList<ListedOption> GetOptions(OptionsTab tab)
        => GetOptions(tab.ToString());


    public static Dictionary<string, object> Settings { get; private set; } = new();

    /// <summary>
    /// Invoked once per setting key/value pair after settings are loaded.
    /// </summary>
    public static Action<string, object>? LoadSetting;

    public static void LoadSettings()
    {
        if (File.Exists(savePath))
        {
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                File.ReadAllText(savePath));

            if (loaded != null)
            {
                foreach (var kvp in loaded)
                    Settings[kvp.Key] = kvp.Value;
            }
        }

        if (LoadSetting == null) return;

        foreach (var setting in Settings)
            LoadSetting.Invoke(setting.Key, setting.Value);
    }

    public static void SaveSettings()
    {
        var dir = Path.GetDirectoryName(savePath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(savePath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
    }
}

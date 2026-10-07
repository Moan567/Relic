using Gum.DataTypes;
using Gum.Managers;
using Gum.Wireframe;
using MonoGameGum;
using MonoGameGum.GueDeriving;
using RenderingLibrary;
using System.Collections.Generic;
using System.Linq;

namespace Engine.UI;

public static class GumUtils
{
    private static readonly Dictionary<string, GraphicalUiElement> activeScreens = new();

    public static IReadOnlyDictionary<string, GraphicalUiElement> ActiveScreens => activeScreens;

    public static bool IsScreenActive(string screenName) => activeScreens.ContainsKey(screenName);

    public static GraphicalUiElement ShowScreen(string screenName)
    {
        if (activeScreens.TryGetValue(screenName, out var existing))
            return existing;

        var screenSave = ObjectFinder.Self.GumProjectSave.Screens
            .FirstOrDefault(s => s.Name == screenName);

        if (screenSave == null) return null;

        var screen = screenSave.ToGraphicalUiElement(SystemManagers.Default);
        screen.AddToRoot();
        activeScreens[screenName] = screen;
        return screen;
    }

    public static void RemoveScreen(string screenName)
    {
        if (!activeScreens.TryGetValue(screenName, out var screen)) return;
        screen.RemoveFromRoot();
        activeScreens.Remove(screenName);
    }

    public static void HideAllScreens()
    {
        foreach (var screen in activeScreens.Values)
            screen.RemoveFromRoot();
        activeScreens.Clear();
    }

    public static GraphicalUiElement GetScreen(string screenName) =>
        activeScreens.GetValueOrDefault(screenName);
}
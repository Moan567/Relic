using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Input;

public static class KeyboardManager
{
    private static readonly object _lock = new();
    private static readonly HashSet<Key> uiDown = new();
    private static HashSet<Key> current = new();
    private static HashSet<Key> previous = new();

    public static void Update()
    {
        previous = current;
        lock (_lock)
        {
            current = new HashSet<Key>(uiDown);
        }
    }

    public static void OnKeyPressed(Key key) { lock (_lock) uiDown.Add(key); }
    public static void OnKeyReleased(Key key) { lock (_lock) uiDown.Remove(key); }

    public static void ClearKeys() { lock (_lock) uiDown.Clear(); }

    public static bool IsPressed(Key key) => current.Contains(key) && !previous.Contains(key);
    public static bool IsHeld(Key key) => current.Contains(key) && previous.Contains(key);
    public static bool IsReleased(Key key) => !current.Contains(key) && previous.Contains(key);
    public static bool IsDown(Key key) => current.Contains(key);
}
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Input;
public static class InputCapture
{
    private static Action<SingleInputBinding>? onCaptured;
    private static Action? onCancelled;

    public static bool IsCapturing => onCaptured != null;

    public static void StartCapture(
        Action<SingleInputBinding> onCaptured,
        Action? onCancelled = null)
    {
        InputCapture.onCaptured = onCaptured;
        InputCapture.onCancelled = onCancelled;
    }

    public static void Update()
    {
        if (onCaptured == null) return;

        if (KeyboardManager.HasBeenPressed(Keys.Escape))
        {
            var cancel = onCancelled;
            onCaptured = null; onCancelled = null;
            cancel?.Invoke();
            return;
        }

        var pressed = KeyboardManager.GetPressedKeys()
            .Where(k => k != Keys.Escape)
            .ToArray();

        if (pressed.Length > 0)
        {
            var callback = onCaptured;
            onCaptured = null; onCancelled = null;
            callback(new BoundKey { Key = pressed[0] });
            return;
        }

        foreach (var btn in Enum.GetValues<MouseButton>())
        {
            var b = new BoundMouseButton { MouseButton = btn };
            if (b.HasBeenPressed())
            {
                var callback = onCaptured;
                onCaptured = null; onCancelled = null;
                callback(b);
                return;
            }
        }
    }
}
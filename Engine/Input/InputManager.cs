using Microsoft.Xna.Framework.Input;
using System.Linq;

namespace Engine.Input;
public class InputBinding
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Category { get; }

    private SingleInputBinding primary;
    private SingleInputBinding? secondary;

    private readonly SingleInputBinding defaultPrimary;
    private readonly SingleInputBinding? defaultSecondary;
    private readonly bool ignorePause;

    public InputBinding(
        string id, string displayName, string category,
        SingleInputBinding primary, SingleInputBinding? secondary = null,
        bool ignorePause = false)
    {
        Id = id;
        DisplayName = displayName;
        Category = category;
        this.primary = primary;
        this.secondary = secondary;
        this.defaultPrimary = primary;
        this.defaultSecondary = secondary;
        this.ignorePause = ignorePause;

        InputRegistry.Register(this);
    }

    public SingleInputBinding GetPrimary() => primary;
    public SingleInputBinding? GetSecondary() => secondary;

    public void Rebind(SingleInputBinding newBinding, bool isPrimary = true)
    {
        if (isPrimary) primary = newBinding;
        else secondary = newBinding;
    }

    public void ResetToDefault()
    {
        primary = defaultPrimary;
        secondary = defaultSecondary;
    }

    public float GetValue() =>
        (!MainEngine.IsPaused || ignorePause)
            ? (primary.GetValue() + (secondary?.GetValue() ?? 0f))
            : 0f;
    public bool IsPressed() =>
        (!MainEngine.IsPaused || ignorePause) &&
        (primary.IsPressed() || (secondary?.IsPressed() ?? false));
    public bool HasBeenPressed() =>
        (!MainEngine.IsPaused || ignorePause) &&
        (primary.HasBeenPressed() || (secondary?.HasBeenPressed() ?? false));
    public bool HasBeenReleased() =>
        (!MainEngine.IsPaused || ignorePause) &&
        (primary.HasBeenReleased() || (secondary?.HasBeenReleased() ?? false));
}
public interface SingleInputBinding
{
    public float GetValue();
    public bool IsPressed();
    public bool HasBeenPressed();
    public bool HasBeenReleased();
}
public class BoundKey : SingleInputBinding
{
    public Keys Key;
    public float GetValue() => IsPressed() ? 1 : 0;
    public bool IsPressed() => KeyboardManager.IsPressed(Key);
    public bool HasBeenPressed() => KeyboardManager.HasBeenPressed(Key);
    public bool HasBeenReleased() => KeyboardManager.HasBeenReleased(Key);
}
public enum MouseButton
{
    Left,
    Right,
    Middle
};
public class BoundMouseButton : SingleInputBinding
{
    public MouseButton MouseButton;
    public float GetValue() => IsPressed() ? 1 : 0;

    public bool IsPressed() => MouseButton == MouseButton.Left ? BetterMouse.GetCurrentState().LeftButton == ButtonState.Pressed :
                               MouseButton == MouseButton.Right ? BetterMouse.GetCurrentState().RightButton == ButtonState.Pressed :
                                                              BetterMouse.GetCurrentState().MiddleButton == ButtonState.Pressed;
    public bool HasBeenPressed() => MouseButton == MouseButton.Left ? !BetterMouse.WasleftDown && BetterMouse.LeftDown :
                                    MouseButton == MouseButton.Right ? !BetterMouse.WasRightDown && BetterMouse.RightDown :
                                                                   !BetterMouse.WasMiddleDown && BetterMouse.MiddleDown;
    public bool HasBeenReleased() => MouseButton == MouseButton.Left ? BetterMouse.WasleftDown && !BetterMouse.LeftDown :
                                     MouseButton == MouseButton.Right ? BetterMouse.WasRightDown && !BetterMouse.RightDown :
                                                                    BetterMouse.WasMiddleDown && !BetterMouse.MiddleDown;
}
public enum GamepadJoystick
{
    Left,
    Right
};
public enum GamepadJoystickAxis
{
    X,
    Y
};
public enum AxisSide
{
    Positive,
    Negative,
    Both
};
public class BoundGamepadAxis : SingleInputBinding
{
    private int playerIndex;
    public GamepadJoystick GamepadJoystick;
    public GamepadJoystickAxis GamepadJoystickAxis;
    public AxisSide AxisSide;

    public BoundGamepadAxis(int playerIndex, GamepadJoystick gamepadJoystick, GamepadJoystickAxis gamepadJoystickAxis, AxisSide axisSide)
    {
        this.playerIndex = playerIndex;
        this.GamepadJoystick = gamepadJoystick;
        this.GamepadJoystickAxis = gamepadJoystickAxis;
        this.AxisSide = axisSide;
    }

    public float GetValue()
    {
        var jState = GamePad.GetState(playerIndex);
        var stick = GamepadJoystick == GamepadJoystick.Left ? jState.ThumbSticks.Left : jState.ThumbSticks.Right;
        float val = GamepadJoystickAxis == GamepadJoystickAxis.X ? stick.X : stick.Y;

        if (AxisSide == AxisSide.Positive) return float.Max(0, val);
        if (AxisSide == AxisSide.Negative) return float.Max(0, -val);
        return val;
    }

    public bool HasBeenPressed() => false;

    public bool HasBeenReleased() => false;

    public bool IsPressed() => GetValue() > 0.1f;
}
public class BoundGamepadButton : SingleInputBinding
{
    private int playerIndex;
    public Buttons GamepadButton;
    private GamePadState oldState;
    private GamePadState currentState;
    public BoundGamepadButton(int playerIndex, Buttons button)
    {
        this.playerIndex = playerIndex;
        this.GamepadButton = button;
    }

    public float GetValue()
    {
        return IsPressed()?1:0;
    }

    public bool HasBeenPressed()
    {
        return IsPressed() && !oldState.IsButtonDown(GamepadButton);
    }

    public bool HasBeenReleased()
    {
        return !IsPressed() && !oldState.IsButtonUp(GamepadButton);
    }

    public bool IsPressed()
    {
        oldState = currentState;
        var jState = GamePad.GetState(playerIndex);
        currentState = jState;

        return currentState.IsButtonDown(GamepadButton);
    }
}
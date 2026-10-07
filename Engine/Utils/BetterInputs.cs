using Engine;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

public class KeyboardManager
{
    static KeyboardState currentKeyState;
    static KeyboardState previousKeyState;

    public static KeyboardState GetState()
    {
        previousKeyState = currentKeyState;
        currentKeyState = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        return currentKeyState;
    }


    public static Keys[] GetPressedKeys()
    {
        return currentKeyState.GetPressedKeys();
    }

    public static bool IsPressed(Keys key)
    {
        return currentKeyState.IsKeyDown(key);
    }

    public static bool HasBeenPressed(Keys key)
    {
        return currentKeyState.IsKeyDown(key) && !previousKeyState.IsKeyDown(key);
    }
    public static bool HasBeenReleased(Keys key)
    {
        return previousKeyState.IsKeyDown(key) && !currentKeyState.IsKeyDown(key);
    }
}

public class BetterMouse
{
    static MouseState currentMouseState;
    static MouseState previousMouseState;

    static float timeSinceLeftClick = 0;
    static float timeSinceRightClick = 0;
    static float timeSinceMiddleClick = 0;

    static int wheelDelta = 0;

    const int miliAllowedTime = 250; //0.25 of a second

    public static MouseState GetCurrentState() => currentMouseState;
    public static MouseState GetPreviousState() => previousMouseState;

    public static bool WasleftDown, WasMiddleDown, WasRightDown;
    public static bool LeftDown, MiddleDown, RightDown;
    public static MouseState GetState(bool skipClicks)
    {
        previousMouseState = currentMouseState;
        currentMouseState = Mouse.GetState();

        if (skipClicks) return currentMouseState;

        WasleftDown = LeftDown;
        WasRightDown = RightDown;
        WasMiddleDown = MiddleDown;

        wheelDelta = currentMouseState.ScrollWheelValue - previousMouseState.ScrollWheelValue;

        if (currentMouseState.LeftButton == ButtonState.Pressed) { timeSinceLeftClick = DateTime.Now.Millisecond; LeftDown = true; }
        if (currentMouseState.RightButton == ButtonState.Pressed) { timeSinceRightClick = DateTime.Now.Millisecond; RightDown = true; }
        if (currentMouseState.MiddleButton == ButtonState.Pressed) { timeSinceMiddleClick = DateTime.Now.Millisecond; MiddleDown = true; }
        if (currentMouseState.LeftButton == ButtonState.Released) { timeSinceLeftClick = -1; LeftDown = false; }
        if (currentMouseState.RightButton == ButtonState.Released) { timeSinceRightClick = -1; RightDown = false; }
        if (currentMouseState.MiddleButton == ButtonState.Released) { timeSinceMiddleClick = -1; MiddleDown = false; }

        return currentMouseState;
    }

    public static int GetScrollDir()
    {
        return int.Sign(wheelDelta);
    }

    public static bool WasLeftPressed() => LeftDown == true && WasleftDown == false;
    public static bool WasRightPressed() => RightDown == true && WasRightDown == false;

    public static bool IsDraggingLeft()
    {
        return (timeSinceLeftClick > 0 && Math.Abs(timeSinceLeftClick - DateTime.Now.Millisecond) > miliAllowedTime);
    }
    public static bool IsDraggingRight()
    {
        return (timeSinceRightClick > 0 && Math.Abs(timeSinceRightClick - DateTime.Now.Millisecond) > miliAllowedTime);
    }
    public static bool IsDraggingMiddle()
    {
        return (timeSinceMiddleClick > 0 && Math.Abs(timeSinceMiddleClick - DateTime.Now.Millisecond) > miliAllowedTime);
    }

    public static Vector2 GetMovement()
    {
        if(!MainEngine.RawInput) return -new Vector2(currentMouseState.X-previousMouseState.X,currentMouseState.Y-previousMouseState.Y);
        return SDLMouse.GetRelativeMotion();    
    }
}

public static class SDLMouse
{
    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_SetRelativeMouseMode(bool enabled);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint SDL_GetRelativeMouseState(out int x, out int y);

    public static void SetRelativeMode(bool enabled) => SDL_SetRelativeMouseMode(enabled);

    public static Vector2 GetRelativeMotion()
    {
        SDL_GetRelativeMouseState(out int x, out int y);
        return new Vector2(x, y);
    }
}

public class LittleBetterKeyboard
{
    KeyboardState currentKeyState;
    KeyboardState previousKeyState;

    public KeyboardState GetState()
    {
        previousKeyState = currentKeyState;
        currentKeyState = Keyboard.GetState();
        return currentKeyState;
    }

    public bool IsPressed(Keys key)
    {
        return currentKeyState.IsKeyDown(key);
    }

    public bool HasBeenPressed(Keys key)
    {
        return currentKeyState.IsKeyDown(key) && !previousKeyState.IsKeyDown(key);
    }
}
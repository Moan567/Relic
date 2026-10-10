using Avalonia.Input;
using Microsoft.Xna.Framework;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Mapper.ViewportManagement;
using Rockwall2.Tools;
using Rockwall2.Views;
using System;

namespace Rockwall2.Editor.Common;

public static class Viewport3DCamera
{
    public static Matrix projectionMatrix, viewMatrix, worldMatrix;

    public static Vector3 position;
    public static Vector3 velocity;
    public static Quaternion rotation;
    public static bool flyPanMode;
    public static bool zFlyMode;

    private static float fieldOfView = 70f;
    private static float zNear = 0.01f;
    private static float zFar = 1024;
    private static float aspectRatio = 1;

    static float yaw, pitch;

    public static float FOV
    {
        get
        {
            return fieldOfView;
        }
        set
        {
            fieldOfView = value;
            RebuildMatrix();
        }
    }
    public static float ZNear
    {
        get
        {
            return zNear;
        }
        set
        {
            zNear = value;
            RebuildMatrix();
        }
    }
    public static float ZFar
    {
        get
        {
            return zFar;
        }
        set
        {
            zFar = value;
            RebuildMatrix();
        }
    }
    public static float AspectRatio
    {
        get
        {
            return aspectRatio;
        }
        set
        {
            if (value == aspectRatio) return;
            aspectRatio = value;
            RebuildMatrix();
        }
    }
    public static void RebuildMatrix()
    {
        projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
            fieldOfView * (MathF.PI / 180f), aspectRatio, zNear, zFar);
    }
    public static void Update(GameTime gameTime, Vector2? centerPos = null)
    {
        float delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
        bool wasfly = flyPanMode;

        if (KeyboardManager.IsPressed(Key.Z) && !KeyboardManager.IsDown(Key.LeftCtrl)) zFlyMode = !zFlyMode;

        // We can enter it by pressing space, but it doesnt need to be held down the whole time.
        if (KeyboardManager.IsDown(Key.Space) || flyPanMode)
        {
            flyPanMode = MouseManager.IsDown(MouseButton.Left);
        }

        flyPanMode |= zFlyMode;

        if (!wasfly && flyPanMode)
        {
            MouseManager.CaptureCursor(MainWindow.Instance, new Vector2((float)MouseManager.Position.X, (float)MouseManager.Position.Y));
        }
        if (wasfly && !flyPanMode)
        {
            MouseManager.ReleaseCursor(MainWindow.Instance);
            MouseManager.ResetCursor(MainWindow.Instance);
        }

        Vector3 moveTarg = new Vector3();
        if (!KeyboardManager.IsDown(Key.LeftCtrl) && !KeyboardManager.IsDown(Key.LeftShift) && !KeyboardManager.IsDown(Key.LeftAlt))
        {
            if(zFlyMode || flyPanMode)
            {
                MouseManager.SetCursor(MainWindow.Instance, StandardCursorType.None);

                yaw -= (float)((MouseManager.Delta.X) * 0.3f);
                pitch -= (float)((MouseManager.Delta.Y) * 0.3f);
            }

            moveTarg += Matrix.CreateFromQuaternion(rotation).Right * (KeyboardManager.IsDown(Key.D) ? 8 : KeyboardManager.IsDown(Key.A) ? -8 : 0);
            moveTarg += Matrix.CreateFromQuaternion(rotation).Forward * (KeyboardManager.IsDown(Key.W) ? 8 : KeyboardManager.IsDown(Key.S) ? -8 : 0);
        }

        if(ViewportManager.Active.IsPerspective && (zFlyMode || flyPanMode))
        {
            int mouseWheel = float.Sign(MouseManager.ScrollDelta);

            if (mouseWheel > 0) position += Matrix.CreateFromQuaternion(rotation).Forward * 2.5f;
            if (mouseWheel < 0) position -= Matrix.CreateFromQuaternion(rotation).Forward * 2.5f;
        }

        velocity = OtherMath.MoveTowards(velocity, moveTarg * 2, delta * 24 * Vector3.Distance(velocity, moveTarg * 2));

        position += velocity * delta;

        rotation = Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(yaw), MathHelper.ToRadians(pitch), 0);

        viewMatrix = Matrix.CreateTranslation(-position) * Matrix.Invert(Matrix.CreateFromQuaternion(rotation));
        worldMatrix = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);
    }
}
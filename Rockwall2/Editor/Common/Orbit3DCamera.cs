using Avalonia.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common;
public class Orbit3DCamera
{
    public Matrix projectionMatrix, viewMatrix, worldMatrix;

    public Vector3 position = new Vector3(0, 8, 8);
    public Vector3 posOffset = new Vector3(0,1,0);
    public Vector3 targetPosOffset = new Vector3(0, 1, 0);
    public Vector3 velocity;
    public Quaternion rotation = Quaternion.CreateFromYawPitchRoll(-1, -1, 0);
    public bool flyPanMode;
    public bool blockOrbit = false;

    private float fieldOfView = 70;
    private float zNear = 0.01f;
    private float zFar = 1024;
    private float aspectRatio = 1;

    float yaw = 5f, pitch = -30f, dist = 5f, finalDist = 5f;
    Point oldCursorPos;
    Point startCursorPos;

    public float FOV
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
    public float ZNear
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
    public float ZFar
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
    public float AspectRatio
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
    public void RebuildMatrix()
    {
        projectionMatrix = Matrix.CreatePerspectiveFieldOfView(
            fieldOfView * (MathF.PI / 180f),
            aspectRatio,
            zNear,
            zFar);
    }
    public void FocusOn(Vector3 center, float radius, float paddingFactor = 1.6f)
    {
        targetPosOffset = center;
        float fovRad = fieldOfView * (MathF.PI / 180f);
        dist = (radius * paddingFactor) / MathF.Sin(fovRad * 0.5f);
    }

    public void Update(GameTime gameTime)
    {
        float delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
        bool wasfly = flyPanMode;

        flyPanMode = MouseManager.IsDown(MouseButton.Left) && !blockOrbit;

        if (!wasfly && flyPanMode)
        {
            MouseManager.CaptureCursor(MainWindow.Instance, null);
        }
        if (wasfly && !flyPanMode)
        {
            MouseManager.ReleaseCursor(MainWindow.Instance);
            MouseManager.ResetCursor(MainWindow.Instance);
        }

        Vector3 moveTarg = new Vector3();
        if (flyPanMode)
        {
            MouseManager.SetCursor(MainWindow.Instance, StandardCursorType.None);

            if (!KeyboardManager.IsDown(Key.LeftShift))
            {
                float rotScale = MathHelper.Clamp(finalDist / 5f, 0.5f, 1f);
                yaw -= (float)((MouseManager.Delta.X) * 0.3f * rotScale);
                pitch -= (float)((MouseManager.Delta.Y) * 0.3f * rotScale);
                pitch = float.Clamp(pitch,-90,90);
            }
            else
            {
                float panScale = finalDist * 0.004f;
                posOffset += Matrix.CreateFromQuaternion(rotation).Up * ((float)MouseManager.Delta.Y * panScale) +
                             Matrix.CreateFromQuaternion(rotation).Right * -((float)MouseManager.Delta.X * panScale);
                targetPosOffset = posOffset;
            }
        }
        //velocity = OtherMath.MoveTowards(velocity, moveTarg, delta * 24 * Vector3.Distance(velocity, moveTarg));

        //position += velocity * delta;

        if (MouseManager.ScrollDelta < 0 && !blockOrbit) dist *= 1.125f;
        if (MouseManager.ScrollDelta > 0 && !blockOrbit) dist /= 1.125f;

        finalDist = OtherMath.MoveTowards(finalDist, dist, delta * float.Abs(finalDist - dist) * 25);
        posOffset = targetPosOffset;

        rotation = Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(yaw), MathHelper.ToRadians(pitch), 0);
        position = Matrix.CreateFromQuaternion(rotation).Forward * -finalDist + posOffset;

        viewMatrix = Matrix.CreateTranslation(-position) * Matrix.Invert(Matrix.CreateFromQuaternion(rotation));
        worldMatrix = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);
    }
}

using Engine.Sound;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Rendering;
public static class CameraControl
{
    private struct CameraRequest
    {
        public Matrix World, View, Projection;
        public float NearPlane, FarPlane;
        public Vector3 ListenerPos;
        public Vector3 ListenerVel;
        public uint Priority;
    }

    private static Dictionary<uint,CameraRequest> requests = new();

    public static void RequestCameraFrameControl(
        Matrix world, Matrix view, Matrix projection,
        Vector3 listenerPos, Vector3 listenerVel, float nearPlane, float farPlane, uint priority)
    {
        requests[priority] = new CameraRequest
        {
            World = world,
            View = view,
            Projection = projection,
            ListenerPos = listenerPos,
            ListenerVel = listenerVel,
            NearPlane = nearPlane,
            FarPlane = farPlane,
            Priority = priority
        };
    }
    public static void Evaluate()
    {
        var ordered = requests.OrderByDescending(c => c.Key);

        var cam = ordered.FirstOrDefault().Value;

        RenderEngine.WorldMatrix = cam.World;
        RenderEngine.ViewMatrix = cam.View;
        RenderEngine.ProjectionMatrix = cam.Projection;
        RenderEngine.CameraNear = cam.NearPlane;
        RenderEngine.CameraFar = cam.FarPlane;

        SoundDevice.Device.ListenerPosition = cam.ListenerPos;
        SoundDevice.Device.ListenerVelocity = cam.ListenerVel;
        SoundDevice.Device.CameraPosition = RenderEngine.CameraPosition;
        SoundDevice.Device.CameraForward = RenderEngine.CameraForward;
        SoundDevice.Device.CameraUp = RenderEngine.CameraUp;
    }
    public static void Clear() => requests.Clear();
}
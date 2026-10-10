using Avalonia.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Rockwall2.Editor.Common.Utils;

public static class GDLoader
{
    static Dictionary<Game, GraphicsDeviceManager> gameDevices = new Dictionary<Game, GraphicsDeviceManager>();
    public static GraphicsDeviceManager Create(Game owner)
    {
        return GetDevice(owner);
    }
    static GraphicsDeviceManager GetDevice(Game owner)
    {
        if (!gameDevices.TryGetValue(owner, out var device))
        {
            device = new GraphicsDeviceManager(owner)
            {
                GraphicsProfile = GraphicsProfile.HiDef
            };
            device.ApplyChanges();

            gameDevices.Add(owner, device);
        }
        return device;
    }
}
using Engine.Scripting.Sound;
using JoltPhysicsSharp;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Engine.Sound;
public static class PhysicsSounds
{
    private const int MaxPerFrame = 16;
    private const float TimerTime = 1f / 15;
    private static ConcurrentStack<(string physName, Vector3 position, Vector3 velocity)> impacts = new ConcurrentStack<(string physName, Vector3 position, Vector3 velocity)>();
    private static ConcurrentBag<BodyID> currentlyQueued = new ConcurrentBag<BodyID>();
    private static int curQueued;
    private static float timer;
    public static void PlayImpactSound(string physName, Vector3 position, Vector3 velocity)
    {
        if (string.IsNullOrEmpty(physName)) return;

        float volume = velocity.Length() / 8f;

        if (volume <= 0.1f) return;

        // potential here to make this more dynamic.
        var scriptname = $"Physics.{physName}.Impact";

        if (!SoundScriptManager.Exists(scriptname)) return;

        SoundScriptManager.PlaySound(scriptname,position,velocity,overrideVolume:volume);
    }
    public static void QueueImpactSound(string physName, BodyID id, Vector3 position, Vector3 velocity)
    {
        if (curQueued > MaxPerFrame) return;

        if (currentlyQueued.Contains(id)) return;

        currentlyQueued.Add(id);
        impacts.Push((physName,position,velocity));
        Interlocked.Increment(ref curQueued);
    }
    public static void PlayAll()
    {
        // This is to help prevent crazy rapid impacts
        timer -= MainEngine.PreviousFrameDelta;
        if(timer <= 0)
        {
            currentlyQueued.Clear();
            timer = TimerTime;
        }

        curQueued = 0;
        while (impacts.Count > 0)
        {
            if(impacts.TryPop(out (string phys, Vector3 pos, Vector3 vel) p))
                PlayImpactSound(p.phys, p.pos, p.vel);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chisel;

public delegate void ChoreoEventTriggeredHandler(string actor, ChoreoEvent action);
public enum ChoreoEventType
{
    LookAt,
    PlayMorph,
    PlayGesture,
    EntityIO
}
public struct ChoreoEvent
{
    public ChoreoEventType EventType;
    public string Target;
    public string Data1;
    public string Data2;
    public float Time;
    public float Duration;
}
public class ChoreoTrack
{
    public string ActorName;
    public List<ChoreoEvent> ChoreoEvents = new();

    public ChoreoEventTriggeredHandler OnEventTriggered;

    private float lastTime;
    private float currentTime;

    public void Update(float dt)
    {
        currentTime += dt;

        for(int i = ChoreoEvents.Count-1; i>=0; i--)
        {
            var action = ChoreoEvents[i];
            if (action.Time >= lastTime && action.Time <= currentTime)
            {
                OnEventTriggered(ActorName, action);

                ChoreoEvents.RemoveAt(i);
            }
        }

        lastTime = currentTime;
    }
}
public class ChoreoScene
{
    public int StartFrame { get; set; } = 0;
    public int EndFrame { get; set; } = 300;
    public float Duration => (EndFrame - StartFrame) / 30f;
    public bool IsComplete => completed;

    public List<ChoreoTrack> Tracks { get; set; } = new();
    public ChoreoEventTriggeredHandler OnEventTriggered { get; set; }

    private float currentTime;
    private bool completed;

    public void SetEvents()
    {
        foreach (var track in Tracks)
            track.OnEventTriggered = OnEventTriggered;
    }

    public void Reset()
    {
        currentTime = 0;
        completed = false;
    }

    public void Update(float dt)
    {
        if (completed) return;
        currentTime += dt;
        foreach (var track in Tracks)
            track.Update(dt);
        if (currentTime >= Duration)
        {
            completed = true;
        }
    }

    public static void WriteToFile(ChoreoScene scene, string path)
    {
        using var fstream = File.OpenWrite(path);
        using var writer = new BinaryWriter(fstream);
        writer.Write(scene.StartFrame);
        writer.Write(scene.EndFrame);
        writer.Write(scene.Tracks.Count);
        foreach (var track in scene.Tracks)
        {
            writer.Write(track.ActorName);
            writer.Write(track.ChoreoEvents.Count);
            foreach (var evt in track.ChoreoEvents)
            {
                writer.Write((byte)evt.EventType);
                writer.Write(evt.Target);
                writer.Write(evt.Data1);
                writer.Write(evt.Data2);
                writer.Write(evt.Time);
            }
        }
    }

    public static async Task<ChoreoScene> LoadFromFile(string path, CancellationToken ct = default)
    {
        byte[] fileBytes = await File.ReadAllBytesAsync(path, ct);

        using var mstream = new MemoryStream(fileBytes);
        using var reader = new BinaryReader(mstream);

        ChoreoScene scene = new();
        scene.StartFrame = reader.ReadInt32();
        scene.EndFrame = reader.ReadInt32();
        int trackCount = reader.ReadInt32();

        for (int i = 0; i < trackCount; i++)
        {
            var track = new ChoreoTrack();
            track.ActorName = reader.ReadString();
            int eventCount = reader.ReadInt32();

            for (int j = 0; j < eventCount; j++)
            {
                var evt = new ChoreoEvent();
                evt.EventType = (ChoreoEventType)reader.ReadByte();
                evt.Target = reader.ReadString();
                evt.Data1 = reader.ReadString();
                evt.Data2 = reader.ReadString();
                evt.Time = reader.ReadSingle();
                track.ChoreoEvents.Add(evt);
            }
            scene.Tracks.Add(track);
        }

        return scene;
    }
}
using NVorbis;
using Rockwall2.Editor.Common.Utils;
using Silk.NET.OpenAL;
using System;
using System.Collections.Generic;

namespace Rockwall2.Editor;

public class EditorAudioPlayer : IDisposable
{
    readonly AL al;

    private uint source;
    private uint buffer;
    private bool hasBuffer;
    private bool disposed;

    private List<(uint source, uint buffer)> sceneSources = new();

    public float CurrentSceneTime
    {
        get
        {
            float max = 0f;
            foreach (var (src, _) in sceneSources)
            {
                al.GetSourceProperty(src, SourceFloat.SecOffset, out float t);
                if (t > max) max = t;
            }
            return max;
        }
    }

    public bool IsScenePlaying
    {
        get
        {
            foreach (var (src, _) in sceneSources)
            {
                al.GetSourceProperty(src, GetSourceInteger.SourceState, out int state);
                if (state == (int)SourceState.Playing) return true;
            }
            return false;
        }
    }

    public bool IsPlaying
    {
        get
        {
            if (!hasBuffer) return false;
            al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
            return state == (int)SourceState.Playing;
        }
    }

    public float CurrentTime
    {
        get
        {
            if (!hasBuffer) return 0f;
            al.GetSourceProperty(source, SourceFloat.SecOffset, out float t);
            return t;
        }
    }

    public EditorAudioPlayer()
    {
        AudioBackend.Acquire();
        al = AudioBackend.AL;

        source = al.GenSource();
        al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        al.SetSourceProperty(source, SourceVector3.Position, 0f, 0f, 0f);
        al.SetSourceProperty(source, SourceFloat.RolloffFactor, 0f);
        al.SetSourceProperty(source, SourceFloat.Gain, 1f);
    }

    public void Load(string path)
    {
        Stop();

        if (hasBuffer)
        {
            al.SetSourceProperty(source, SourceInteger.Buffer, 0);
            al.DeleteBuffer(buffer);
            hasBuffer = false;
        }

        try
        {
            byte[] pcm = OggAudio.LoadPcm16(path, out int channels, out int sampleRate);
            buffer = al.GenBuffer();
            var fmt = channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
            al.BufferData(buffer, fmt, pcm, sampleRate);
            al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
            hasBuffer = true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[EditorAudio] Load failed: {e.Message}");
        }
    }

    public void Play(float fromTime = 0f)
    {
        if (!hasBuffer) return;
        al.SourceStop(source);
        al.SetSourceProperty(source, SourceFloat.SecOffset, fromTime);
        al.SourcePlay(source);
    }

    public void Stop()
    {
        if (!hasBuffer) return;
        al.SourceStop(source);
    }

    public void PlaySceneAudio(string path, float fileOffset)
    {
        try
        {
            byte[] pcm = OggAudio.LoadPcm16(path, out int channels, out int sampleRate);
            uint buf = al.GenBuffer();
            al.BufferData(buf, channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16, pcm, sampleRate);

            uint src = al.GenSource();
            al.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
            al.SetSourceProperty(src, SourceVector3.Position, 0f, 0f, 0f);
            al.SetSourceProperty(src, SourceFloat.RolloffFactor, 0f);
            al.SetSourceProperty(src, SourceFloat.Gain, 1f);
            al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
            al.SourcePlay(src);
            al.SetSourceProperty(src, SourceFloat.SecOffset, fileOffset);
            sceneSources.Add((src, buf));
        }
        catch (Exception e) { Console.WriteLine($"[ChoreoAudio] {e.Message}"); }
    }

    public void StopSceneAudio()
    {
        foreach (var (src, buf) in sceneSources)
        {
            al.SourceStop(src);
            al.SetSourceProperty(src, SourceInteger.Buffer, 0);
            al.DeleteSource(src);
            al.DeleteBuffer(buf);
        }
        sceneSources.Clear();
    }

    public void Seek(float time)
    {
        if (!hasBuffer) return;
        bool wasPlaying = IsPlaying;
        al.SourceStop(source);
        al.SetSourceProperty(source, SourceFloat.SecOffset, time);
        if (wasPlaying) al.SourcePlay(source);
    }

    public void ScrubPlay(float time)
    {
        if (!hasBuffer) return;
        al.SourceStop(source);
        al.SetSourceProperty(source, SourceFloat.SecOffset, time);
        al.SourcePlay(source);
    }

    public void StopScrub()
    {
        if (!hasBuffer) return;
        al.SourceStop(source);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        al.SourceStop(source);
        al.DeleteSource(source);
        if (hasBuffer)
        {
            al.SetSourceProperty(source, SourceInteger.Buffer, 0);
            al.DeleteBuffer(buffer);
        }
        StopSceneAudio();

        AudioBackend.Release();
    }
}

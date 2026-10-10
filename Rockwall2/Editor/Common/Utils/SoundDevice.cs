using Microsoft.Xna.Framework;
using Silk.NET.OpenAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Utils;

public enum SoundCategory
{
    SFX,
    Music,
    Master
}

public class SoundDevice : IDisposable
{
    class Voice
    {
        public uint Id;
        public uint ALSource;
        public float BaseGain;
        public bool Is3D;
        public bool Looping;
        public SoundCategory Category;
    }

    class Clip
    {
        public uint ALBuffer;
        public int Channels;
    }

    public Dictionary<SoundCategory, float> SoundLevels = Enum.GetValues<SoundCategory>().ToDictionary(c => c, f => 1.0f);

    /// <summary>
    /// The instance of the sound device.
    /// </summary>
    public static SoundDevice Device { get; private set; }
    public static int MaxSources { get; private set; } = 256;

    public Vector3 ListenerPosition;
    public Vector3 ListenerVelocity;
    public Vector3 CameraForward;
    public Vector3 CameraUp;
    public Vector3 CameraPosition;

    bool disposedValue;

    AL al;

    Dictionary<uint, Voice> voices = new Dictionary<uint, Voice>();
    uint nextVoiceId = 1;

    List<Clip> clips = new List<Clip>();
    Dictionary<string, uint> cachedSounds = new Dictionary<string, uint>();

    public static void Initialize()
    {
        Device ??= new SoundDevice();
    }

    public SoundDevice()
    {
        AudioBackend.Acquire();
        al = AudioBackend.AL;
    }

    /// <summary>
    /// Loads and caches a sound so that it can be used later without needing to load it then.
    /// </summary>
    /// <param name="filePath">Path to the sound file, from the application directory.</param>
    /// <param name="mono">Whether to downmix to mono (needed for 3D sounds) or keep original channels (music/UI).</param>
    /// <returns></returns>
    public uint PrefetchSound(string filePath, bool mono)
    {
        string cacheKey = mono ? filePath + "#mono" : filePath;

        if (cachedSounds.TryGetValue(cacheKey, out uint existing))
        {
            return existing;
        }

        byte[] pcm;
        int channels;
        int sampleRate;
        if (mono)
        {
            pcm = OggAudio.LoadPcm16Mono(filePath, out sampleRate);
            channels = 1;
        }
        else
        {
            pcm = OggAudio.LoadPcm16(filePath, out channels, out sampleRate);
        }

        uint alBuffer = al.GenBuffer();
        BufferFormat format = channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
        al.BufferData(alBuffer, format, pcm, sampleRate);

        uint id = (uint)clips.Count;
        clips.Add(new Clip { ALBuffer = alBuffer, Channels = channels });
        cachedSounds[cacheKey] = id;
        return id;
    }

    /// <summary>
    /// Plays a sound file.
    /// </summary>
    /// <param name="filePath">Path to the sound file, from the application directory.</param>
    /// <param name="position">Global position in 3D space to play the sound.</param>
    /// <param name="loop">Should the sound loop when finished?</param>
    /// <param name="gain">Volume of the sound.</param>
    /// <param name="pitch">Pitch of the sound. 1.0f is default.</param>
    /// <param name="disable3D">Disables the 3D sound spatialization. Used for music, or anything alike. 3D sounds are forced to be mono.</param>
    /// <param name="minDist">Distance to which the sound is at full volume.</param>
    /// <param name="maxDist">Distance where the sound is outside of hearing range.</param>
    /// <param name="rolloff">How quickly volume decreases over distance.</param>
    /// <param name="category">Which category does the sound belong to?</param>
    /// <returns>ID of the sound</returns>
    public uint PlaySound(string filePath, Vector3 position, bool loop = false, float gain = 1f, float pitch = 1f, bool disable3D = false, float minDist = 1f, float maxDist = 64, float rolloff = 1f, SoundCategory category = SoundCategory.SFX)
    {
        uint bufferID = PrefetchSound(filePath, !disable3D);
        return PlaySound(bufferID, position, loop, gain, pitch, disable3D, minDist, maxDist, rolloff, category);
    }

    /// <summary>
    /// Plays a sound file.
    /// </summary>
    /// <param name="bufferID">ID of the sound file. Obtained from Prefetch.</param>
    /// <param name="position">Global position in 3D space to play the sound.</param>
    /// <param name="loop">Should the sound loop when finished?</param>
    /// <param name="gain">Volume of the sound.</param>
    /// <param name="pitch">Pitch of the sound. 1.0f is default.</param>
    /// <param name="disable3D">Disables the 3D sound spatialization. Used for music, or anything alike. 3D sounds are forced to be mono.</param>
    /// <param name="minDist">Distance to which the sound is at full volume.</param>
    /// <param name="maxDist">Distance where the sound is outside of hearing range.</param>
    /// <param name="rolloff">How quickly volume decreases over distance.</param>
    /// <param name="category">Which category does the sound belong to?</param>
    /// <returns>ID of the sound</returns>
    public uint PlaySound(uint bufferID, Vector3 position, bool loop = false, float gain = 1f, float pitch = 1f, bool disable3D = false, float minDist = 1f, float maxDist = 64, float rolloff = 1f, SoundCategory category = SoundCategory.SFX)
    {
        if (bufferID >= clips.Count) return 0;
        if (!disable3D && Vector3.Distance(position, ListenerPosition) > maxDist) return 0;

        if (voices.Count >= MaxSources)
        {
            uint? oldestNonMusic = null;
            foreach (var kv in voices)
            {
                if (kv.Value.Category != SoundCategory.Music)
                {
                    if (oldestNonMusic == null || kv.Key < oldestNonMusic.Value)
                    {
                        oldestNonMusic = kv.Key;
                    }
                }
            }
            if (oldestNonMusic != null)
            {
                Voice evicted = voices[oldestNonMusic.Value];
                al.SourceStop(evicted.ALSource);
                al.DeleteSource(evicted.ALSource);
                voices.Remove(oldestNonMusic.Value);
            }
        }

        Clip clip = clips[(int)bufferID];

        uint id = nextVoiceId++;
        uint src = al.GenSource();

        if (disable3D)
        {
            al.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
            al.SetSourceProperty(src, SourceVector3.Position, 0f, 0f, 0f);
        }
        else
        {
            al.SetSourceProperty(src, SourceBoolean.SourceRelative, false);
            al.SetSourceProperty(src, SourceVector3.Position, position.X, position.Y, position.Z);
            al.SetSourceProperty(src, SourceFloat.ReferenceDistance, minDist);
            al.SetSourceProperty(src, SourceFloat.MaxDistance, maxDist);
            al.SetSourceProperty(src, SourceFloat.RolloffFactor, rolloff);
        }

        al.SetSourceProperty(src, SourceInteger.Buffer, (int)clip.ALBuffer);
        al.SetSourceProperty(src, SourceFloat.Gain, gain * SoundLevels[category]);
        al.SetSourceProperty(src, SourceFloat.Pitch, pitch);
        al.SetSourceProperty(src, SourceBoolean.Looping, loop);
        al.SourcePlay(src);

        voices[id] = new Voice
        {
            Id = id,
            ALSource = src,
            BaseGain = gain,
            Is3D = !disable3D,
            Looping = loop,
            Category = category
        };

        return id;
    }

    public void StopSound(uint id)
    {
        if (voices.TryGetValue(id, out Voice voice))
        {
            al.SourceStop(voice.ALSource);
            al.DeleteSource(voice.ALSource);
            voices.Remove(id);
        }
    }

    public bool UpdateSound(uint id, Vector3? pos = null, Vector3? velocity = null, float? gain = null)
    {
        if (!voices.TryGetValue(id, out Voice voice)) return false;

        if (pos != null && voice.Is3D)
        {
            al.SetSourceProperty(voice.ALSource, SourceVector3.Position, pos.Value.X, pos.Value.Y, pos.Value.Z);
        }
        if (velocity != null && voice.Is3D)
        {
            al.SetSourceProperty(voice.ALSource, SourceVector3.Velocity, velocity.Value.X, velocity.Value.Y, velocity.Value.Z);
        }
        if (gain != null)
        {
            voice.BaseGain = gain.Value;
            al.SetSourceProperty(voice.ALSource, SourceFloat.Gain, voice.BaseGain * SoundLevels[voice.Category]);
        }

        return true;
    }

    /// <summary>
    /// Stops everything currently playing. Previously this only cleared acoustic-zone
    /// reverb bookkeeping tied to level geometry; with that system gone, this now
    /// actually stops and frees all voices, which is the more useful behaviour for
    /// something like a level unload.
    /// </summary>
    public void Reset()
    {
        foreach (var voice in voices.Values)
        {
            al.SourceStop(voice.ALSource);
            al.DeleteSource(voice.ALSource);
        }
        voices.Clear();
    }

    public unsafe void UpdateWorld()
    {
        al.SetListenerProperty(ListenerVector3.Position, ListenerPosition.X, ListenerPosition.Y, ListenerPosition.Z);
        al.SetListenerProperty(ListenerVector3.Velocity, ListenerVelocity.X, ListenerVelocity.Y, ListenerVelocity.Z);

        float* orientation = stackalloc float[6]
        {
            CameraForward.X, CameraForward.Y, CameraForward.Z,
            CameraUp.X, CameraUp.Y, CameraUp.Z
        };
        al.SetListenerProperty(ListenerFloatArray.Orientation, orientation);

        List<uint> finishedIds = null;
        foreach (var kv in voices)
        {
            Voice voice = kv.Value;

            al.SetSourceProperty(voice.ALSource, SourceFloat.Gain, voice.BaseGain * SoundLevels[voice.Category]);

            if (voice.Looping) continue;

            al.GetSourceProperty(voice.ALSource, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing)
            {
                (finishedIds ??= new List<uint>()).Add(kv.Key);
            }
        }
        if (finishedIds != null)
        {
            foreach (uint id in finishedIds)
            {
                if (voices.TryGetValue(id, out Voice voice))
                {
                    al.DeleteSource(voice.ALSource);
                    voices.Remove(id);
                }
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                foreach (var voice in voices.Values)
                {
                    al.SourceStop(voice.ALSource);
                    al.DeleteSource(voice.ALSource);
                }
                voices.Clear();

                foreach (var clip in clips)
                {
                    al.DeleteBuffer(clip.ALBuffer);
                }
                clips.Clear();

                AudioBackend.Release();
            }
            disposedValue = true;
        }
    }
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

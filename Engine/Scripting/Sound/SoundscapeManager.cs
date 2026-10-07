using Engine.Console;
using Engine.Scripting.ValueScript;
using Engine.Sound;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Engine.Scripting.Sound;

class ActiveSoundscapeSound
{
    public SoundscapeSound Definition;
    public SoundInstance Instance;
    public float BaseVolume;
    public float NextTriggerTime;
}

class ActiveSoundscape
{
    public SoundscapeDefinition Definition;
    public List<ActiveSoundscapeSound> Sounds = new();
    public float Elapsed;
    public float FadeMultiplier = 1f;
}

public static class SoundscapeManager
{
    static Dictionary<string, SoundscapeDefinition> definitions = new();
    static EvaluationContext evaluationContext;
    static Random random = new Random();

    public static Vector3[] Anchors = new Vector3[8];

    public static string CurrentSoundscape => current?.Definition.Name;
    public static string PreviousSoundscape => previous?.Definition.Name;

    static ActiveSoundscape current;
    static ActiveSoundscape previous;
    static float crossfadeDuration = 1f;

    public static void LoadAll(string rootScriptPath)
    {
        foreach (var file in Directory.GetFiles(rootScriptPath))
        {
            var resolved = ScriptLoader.LoadScript(file);
            definitions = definitions.Concat(new SoundscapeReader().Read(resolved)).ToDictionary();
        }
        evaluationContext = new EvaluationContext();
        CoreScriptFunctions.RegisterDefaults(evaluationContext);
    }

    public static void SetAnchor(int index, Vector3 position)
    {
        if (index < 0 || index > 7)
        {
            return;
        }
        Anchors[index] = position;
    }

    public static void StartSoundscape(string name, float crossfade = 1f)
    {
        if (!definitions.TryGetValue(name, out var definition))
        {
            Logger.AppendError($"Unknown soundscape '{name}'.");
            return;
        }

        if (current != null && current.Definition.Name == name)
        {
            return;
        }
        Logger.AppendInfo($"Started soundscape '{name}'.");

        if (previous != null)
        {
            StopScape(previous);
        }

        if (current != null)
        {
            previous = current;
            previous.Elapsed = 0f;
        }

        crossfadeDuration = crossfade;

        current = new ActiveSoundscape { Definition = definition, FadeMultiplier = 0f };

        foreach (var sound in definition.Sounds.Values)
        {
            StartSound(current, sound);
        }
    }

    public static void StopSoundscape(float crossfade = 1f)
    {
        if (current == null)
        {
            return;
        }

        if (previous != null)
        {
            StopScape(previous);
        }

        previous = current;
        previous.Elapsed = 0f;
        current = null;

        crossfadeDuration = crossfade;
    }

    static void StartSound(ActiveSoundscape scape, SoundscapeSound sound)
    {
        Vector3 position = ResolvePosition(sound, Vector3.Zero, true);
        bool is3D = sound.PositionMode != SoundscapePositionMode.Global;

        evaluationContext.CursorKey = "volume";
        float volume = sound.Volume != null ? ScriptEvaluator.EvaluateFloat(sound.Volume, evaluationContext) : 1f;

        evaluationContext.CursorKey = "pitch";
        float pitch = sound.Pitch != null ? ScriptEvaluator.EvaluateFloat(sound.Pitch, evaluationContext) : 1f;

        SoundCategory channel = sound.Channel switch
        {
            "SFX" => SoundCategory.SFX,
            "Music" => SoundCategory.Music,
            _ => SoundCategory.Master
        };

        var active = new ActiveSoundscapeSound
        {
            Definition = sound,
            BaseVolume = volume
        };

        if (sound.Mode == SoundscapeSoundMode.Looping)
        {
            evaluationContext.CursorKey = sound.Name;
            string path = Path.Combine(MainEngine.FullPath, ScriptEvaluator.EvaluateString(sound.Sound, evaluationContext));
            var id = SoundDevice.Device.PlaySound(path, position, true, 0f, pitch, !is3D, category: channel);
            active.Instance = new SoundInstance(id, position, Vector3.Zero, 0f);
        }
        else
        {
            evaluationContext.CursorKey = "interval";
            active.NextTriggerTime = ScriptEvaluator.EvaluateFloat(sound.Interval, evaluationContext);
        }

        scape.Sounds.Add(active);
    }

    static void TriggerRandomSound(ActiveSoundscape scape, ActiveSoundscapeSound active, Vector3 listenerPosition)
    {
        var sound = active.Definition;
        Vector3 position = ResolvePosition(sound, listenerPosition, false);
        bool is3D = sound.PositionMode != SoundscapePositionMode.Global;

        evaluationContext.CursorKey = "pitch";
        float pitch = sound.Pitch != null ? ScriptEvaluator.EvaluateFloat(sound.Pitch, evaluationContext) : 1f;

        SoundCategory channel = sound.Channel switch
        {
            "SFX" => SoundCategory.SFX,
            "Music" => SoundCategory.Music,
            _ => SoundCategory.Master
        };

        evaluationContext.CursorKey = sound.Name;
        string path = Path.Combine(MainEngine.FullPath, ScriptEvaluator.EvaluateString(sound.Sound, evaluationContext));
        SoundDevice.Device.PlaySound(path, position, false, active.BaseVolume * scape.FadeMultiplier, pitch, !is3D, category: channel);

        evaluationContext.CursorKey = "interval";
        active.NextTriggerTime = ScriptEvaluator.EvaluateFloat(sound.Interval, evaluationContext);
    }

    static Vector3 ResolvePosition(SoundscapeSound sound, Vector3 listenerPosition, bool onStart)
    {
        switch (sound.PositionMode)
        {
            case SoundscapePositionMode.Anchor:
                return Anchors[Math.Clamp(sound.AnchorIndex, 0, 7)];

            case SoundscapePositionMode.RandomRadius:
                evaluationContext.CursorKey = "position";
                float min = ScriptEvaluator.EvaluateFloat(sound.PositionMin, evaluationContext);
                float max = ScriptEvaluator.EvaluateFloat(sound.PositionMax, evaluationContext);
                Vector3 origin = onStart ? Vector3.Zero : listenerPosition;
                return origin + RandomPointAround(min, max);

            default:
                return Vector3.Zero;
        }
    }

    static Vector3 RandomPointAround(float min, float max)
    {
        float angle = (float)(random.NextDouble() * Math.PI * 2);
        float dist = min + (float)random.NextDouble() * (max - min);
        return new Vector3((float)Math.Cos(angle) * dist, 0, (float)Math.Sin(angle) * dist);
    }

    public static void Update(float deltaTime, Vector3 listenerPosition)
    {
        if (current != null)
        {
            current.Elapsed += deltaTime;
            current.FadeMultiplier = crossfadeDuration > 0f
                ? Math.Clamp(current.Elapsed / crossfadeDuration, 0f, 1f)
                : 1f;
            ApplyVolumes(current);
            UpdateRandomSounds(current, deltaTime, listenerPosition);
        }

        if (previous != null)
        {
            previous.Elapsed += deltaTime;
            previous.FadeMultiplier = crossfadeDuration > 0f
                ? 1f - Math.Clamp(previous.Elapsed / crossfadeDuration, 0f, 1f)
                : 0f;
            ApplyVolumes(previous);

            if (previous.FadeMultiplier <= 0f)
            {
                StopScape(previous);
                previous = null;
            }
        }
    }

    static void UpdateRandomSounds(ActiveSoundscape scape, float deltaTime, Vector3 listenerPosition)
    {
        foreach (var sound in scape.Sounds)
        {
            if (sound.Definition.Mode != SoundscapeSoundMode.Random)
            {
                continue;
            }

            sound.NextTriggerTime -= deltaTime;
            if (sound.NextTriggerTime <= 0f)
            {
                TriggerRandomSound(scape, sound, listenerPosition);
            }
        }
    }

    static void ApplyVolumes(ActiveSoundscape scape)
    {
        foreach (var sound in scape.Sounds)
        {
            if (sound.Instance == null) continue;
            sound.Instance.SourceGain = (sound.BaseVolume * scape.FadeMultiplier);
        }
    }

    static void StopScape(ActiveSoundscape scape)
    {
        foreach (var sound in scape.Sounds)
        {
            sound.Instance?.Stop();
        }
    }
}
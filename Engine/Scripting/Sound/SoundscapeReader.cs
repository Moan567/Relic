using Engine.Scripting.ValueScript;
using System;
using System.Collections.Generic;

namespace Engine.Scripting.Sound;

public class SoundscapeReader
{
    public Dictionary<string, SoundscapeDefinition> Read(ScriptBlock resolvedRoot)
    {
        var result = new Dictionary<string, SoundscapeDefinition>();

        foreach (var entry in resolvedRoot.Entries)
        {
            if (entry.Value.Kind != ScriptValueKind.Block)
            {
                continue;
            }

            var definition = new SoundscapeDefinition { Name = entry.Key };

            foreach (var soundEntry in entry.Value.Block.Entries)
            {
                if (soundEntry.Value.Kind != ScriptValueKind.Block)
                {
                    continue;
                }

                definition.Sounds[soundEntry.Key] = ReadSound(soundEntry.Key, soundEntry.Value.Block);
            }

            result[definition.Name] = definition;
        }

        return result;
    }

    SoundscapeSound ReadSound(string name, ScriptBlock block)
    {
        var sound = new SoundscapeSound { Name = name };

        var channel = block.FindFirst("channel");
        if (channel != null)
        {
            sound.Channel = channel.Value.StringValue;
        }

        var soundValue = block.FindFirst("sound");
        if (soundValue != null)
        {
            sound.Sound = soundValue.Value;
        }

        var mode = block.FindFirst("mode");
        sound.Mode = mode != null && mode.Value.StringValue.Equals("random", StringComparison.OrdinalIgnoreCase)
            ? SoundscapeSoundMode.Random
            : SoundscapeSoundMode.Looping;

        var interval = block.FindFirst("interval");
        if (interval != null)
        {
            sound.Interval = interval.Value;
        }

        var volume = block.FindFirst("volume");
        if (volume != null)
        {
            sound.Volume = volume.Value;
        }

        var pitch = block.FindFirst("pitch");
        if (pitch != null)
        {
            sound.Pitch = pitch.Value;
        }

        ReadPosition(sound, block.FindFirst("position"));

        return sound;
    }

    void ReadPosition(SoundscapeSound sound, ScriptEntry positionEntry)
    {
        if (positionEntry == null || positionEntry.Value.Kind != ScriptValueKind.FunctionCall)
        {
            sound.PositionMode = SoundscapePositionMode.Global;
            return;
        }

        var call = positionEntry.Value.FunctionCall;

        if (call.Name.Equals("anchor", StringComparison.OrdinalIgnoreCase))
        {
            sound.PositionMode = SoundscapePositionMode.Anchor;
            sound.AnchorIndex = (int)call.Arguments[0].NumberValue;
        }
        else if (call.Name.Equals("rndpos", StringComparison.OrdinalIgnoreCase))
        {
            sound.PositionMode = SoundscapePositionMode.RandomRadius;
            sound.PositionMin = call.Arguments[0];
            sound.PositionMax = call.Arguments[1];
        }
    }
}
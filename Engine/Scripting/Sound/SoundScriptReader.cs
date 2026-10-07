using Engine.Scripting.ValueScript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.Sound;
public class SoundScriptReader
{
    public Dictionary<string, SoundScriptEntry> Read(ScriptBlock resolvedRoot)
    {
        var result = new Dictionary<string, SoundScriptEntry>();

        foreach (var entry in resolvedRoot.Entries)
        {
            if (entry.Value.Kind != ScriptValueKind.Block)
                continue;

            var block = entry.Value.Block;
            var sound = new SoundScriptEntry { Name = entry.Key };

            var channel = block.FindFirst("channel");
            if (channel != null)
                sound.Channel = channel.Value.StringValue;

            var volume = block.FindFirst("volume");
            if (volume != null)
                sound.Volume = volume.Value;

            var pitch = block.FindFirst("pitch");
            if (pitch != null)
                sound.Pitch = pitch.Value;

            var soundValue = block.FindFirst("sound");
            if (soundValue != null)
                sound.Sound = soundValue.Value;

            var maxDistValue = block.FindFirst("maxDistance");
            if (maxDistValue != null)
                sound.MaxDistance = maxDistValue.Value;

            var minDistValue = block.FindFirst("minDistance");
            if (minDistValue != null)
                sound.MinDistance = minDistValue.Value;

            var loopValue = block.FindFirst("loop");
            if (loopValue != null)
                sound.Loop = loopValue.Value;

            var is3DValue = block.FindFirst("is3D");
            if (is3DValue != null)
                sound.Is3D = is3DValue.Value;

            result[sound.Name] = sound;
        }

        return result;
    }
}
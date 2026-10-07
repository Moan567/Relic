using Engine.Console;
using Engine.Scripting.ValueScript;
using Engine.Sound;
using Microsoft.Xna.Framework;
using Silk.NET.OpenAL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.Sound;
public static class SoundScriptManager
{
    static Dictionary<string, SoundScriptEntry> definitions = new();
    static EvaluationContext evaluationContext;
    public static void LoadAll(string rootScriptPath)
    {
        foreach(var file in Directory.GetFiles(rootScriptPath))
        {
            var resolved = ScriptLoader.LoadScript(file);
            definitions = definitions.Concat(new SoundScriptReader().Read(resolved)).ToDictionary();
        }
        evaluationContext = new EvaluationContext();
        CoreScriptFunctions.RegisterDefaults(evaluationContext);
    }
    public static bool Exists(string name)
    {
        return definitions.ContainsKey(name);
    }
    // Obviously this has to call the old PlaySound
#pragma warning disable 0618 
    public static SoundInstance PlaySound(string name, Vector3? position = null, Vector3? velocity = null, bool? is3DOverride = null, float? overrideVolume = null, float? overridePitch = null)
    {
        try
        {
            if (!definitions.TryGetValue(name, out var entry))
            {
                string fullpath = Path.IsPathRooted(name) ? name : Path.Combine(MainEngine.FullPath, name);

                if (File.Exists(fullpath))
                {
                    Logger.AppendInfo($"Soundscript name was an audio path; playing audio from path.");

                    var rID = SoundDevice.Device.PlaySound(fullpath, position ?? Vector3.Zero, false, overrideVolume ?? 1, overridePitch ?? 1, !(is3DOverride ?? true), category: SoundCategory.SFX);

                    return new SoundInstance(rID, position ?? Vector3.Zero, velocity ?? Vector3.Zero, overrideVolume ?? 1);
                }
                else
                {
                    throw new Exception($"Unknown soundscript entry '{name}'.");
                }
            }
            evaluationContext.CursorKey = "volume";
            float volume = entry.Volume != null ? ScriptEvaluator.EvaluateFloat(entry.Volume, evaluationContext) : 1f;

            evaluationContext.CursorKey = "pitch";
            float pitch = entry.Pitch != null ? ScriptEvaluator.EvaluateFloat(entry.Pitch, evaluationContext) : 1f;

            evaluationContext.CursorKey = "loop";
            bool loop = entry.Loop != null ? ScriptEvaluator.EvaluateBool(entry.Loop, evaluationContext) : false;

            evaluationContext.CursorKey = "is3D";
            bool is3D = entry.Is3D != null ? ScriptEvaluator.EvaluateBool(entry.Is3D, evaluationContext) : true;

            evaluationContext.CursorKey = "maxdist";
            float maxDist = entry.MaxDistance != null ? ScriptEvaluator.EvaluateFloat(entry.MaxDistance, evaluationContext) : 64;

            evaluationContext.CursorKey = "mindist";
            float minDist = entry.MinDistance != null ? ScriptEvaluator.EvaluateFloat(entry.MinDistance, evaluationContext) : 1;

            SoundCategory channel = entry.Channel switch
            {
                "SFX" => SoundCategory.SFX,
                "Music" => SoundCategory.Music,
                _ => SoundCategory.Master
            };

            if (is3DOverride.HasValue) is3D = is3DOverride.Value;
            if (overrideVolume.HasValue) volume = overrideVolume.Value;
            if (overridePitch.HasValue) pitch = overridePitch.Value;

            if (entry.Sound == null)
                throw new Exception($"Soundscript '{name}' has no sound to play.");

            evaluationContext.CursorKey = name;

            var soundValues = ScriptEvaluator.EvaluateList(entry.Sound, evaluationContext);

            string wavePath = Path.Combine(MainEngine.FullPath, ScriptEvaluator.EvaluateString(entry.Sound, evaluationContext));

            var id = SoundDevice.Device.PlaySound(wavePath, position ?? Vector3.Zero, loop, volume, pitch, !is3D, category: channel,
                                                  minDist:minDist, maxDist:maxDist);

            return new SoundInstance(id, position ?? Vector3.Zero, velocity ?? Vector3.Zero, volume, wavePath);
        }
        catch (Exception e)
        {
            Logger.AppendError($"Soundscript '{name}' error: {e.Message}");
            return null;
        }
    }
#pragma warning restore 0618
}
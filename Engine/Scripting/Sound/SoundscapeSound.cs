using Engine.Scripting.ValueScript;
using System.Collections.Generic;

namespace Engine.Scripting.Sound;
public enum SoundscapeSoundMode
{
    Looping,
    Random
}

public enum SoundscapePositionMode
{
    Global,
    Anchor,
    RandomRadius
}
public class SoundscapeSound
{
    public string Name;
    public string Channel;
    public ScriptValue Sound;
    public SoundscapeSoundMode Mode;
    public ScriptValue Interval;
    public SoundscapePositionMode PositionMode;
    public int AnchorIndex;
    public ScriptValue PositionMin;
    public ScriptValue PositionMax;
    public ScriptValue Volume;
    public ScriptValue Pitch;
}

public class SoundscapeDefinition
{
    public string Name;
    public Dictionary<string, SoundscapeSound> Sounds = new();
}
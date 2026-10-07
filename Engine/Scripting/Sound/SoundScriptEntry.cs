using Engine.Scripting.ValueScript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.Sound;
public class SoundScriptEntry
{
    public string Name;
    public string Channel;
    public ScriptValue Volume;
    public ScriptValue Pitch;
    public ScriptValue Sound;
    public ScriptValue MaxDistance;
    public ScriptValue MinDistance;
    public ScriptValue Loop;
    public ScriptValue Is3D;
}
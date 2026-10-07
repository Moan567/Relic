using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Sound;
public class SoundInstance
{
    public uint SourceID;

    public Vector3 SourcePosition
    {
        get
        {
            return sourcePosition;
        }
        set
        {
            sourcePosition = value;

            SoundDevice.Device.UpdateSound(SourceID, sourcePosition, sourceVelocity, sourceGain);
        }
    }
    public Vector3 SourceVelocity
    {
        get
        {
            return sourceVelocity;
        }
        set
        {
            sourceVelocity = value;

            SoundDevice.Device.UpdateSound(SourceID, sourcePosition, sourceVelocity, sourceGain);
        }
    }
    public float SourceGain
    {
        get
        {
            return sourceGain;
        }
        set
        {
            sourceGain = value;

            SoundDevice.Device.UpdateSound(SourceID, sourcePosition, sourceVelocity, sourceGain);
        }
    }
    public string SoundPath => soundPath;

    private Vector3 sourcePosition;
    private Vector3 sourceVelocity;
    private float sourceGain;
    private string soundPath;

    public bool HasStopped => !SoundDevice.Device.IsVoicePlaying(SourceID);

    public SoundInstance(uint sourceID, Vector3 sourcePosition, Vector3 sourceVelocity, float sourceGain, string soundPath = null)
    {
        SourceID = sourceID;
        this.sourcePosition = sourcePosition;
        this.sourceVelocity = sourceVelocity;
        this.sourceGain = sourceGain;
        this.soundPath = soundPath;
    }
    public void Stop()
    {
        SoundDevice.Device.StopSound(SourceID);
    }
}

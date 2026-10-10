using NVorbis;
using System;

namespace Rockwall2.Editor.Common.Utils;
public static class OggAudio
{
    public static byte[] LoadPcm16(string filePath, out int channels, out int sampleRate)
    {
        float[] interleaved = ReadFloats(filePath, out channels, out sampleRate, out int frameCount);
        return FloatsToPcm16(interleaved, frameCount * channels);
    }

    public static byte[] LoadPcm16Mono(string filePath, out int sampleRate)
    {
        float[] interleaved = ReadFloats(filePath, out int channels, out sampleRate, out int frameCount);

        float[] mono = interleaved;
        if (channels > 1)
        {
            mono = new float[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                float sum = 0f;
                int baseIdx = frame * channels;
                for (int c = 0; c < channels; c++)
                {
                    sum += interleaved[baseIdx + c];
                }
                mono[frame] = sum / channels;
            }
        }

        return FloatsToPcm16(mono, frameCount);
    }

    static float[] ReadFloats(string filePath, out int channels, out int sampleRate, out int frameCount)
    {
        using var vorbis = new VorbisReader(filePath);
        channels = vorbis.Channels;
        sampleRate = vorbis.SampleRate;

        var floats = new float[vorbis.TotalSamples * channels];
        int read = vorbis.ReadSamples(floats, 0, floats.Length);
        if (read != floats.Length)
        {
            Array.Resize(ref floats, read);
        }

        frameCount = read / channels;
        return floats;
    }

    static byte[] FloatsToPcm16(float[] samples, int count)
    {
        var pcm = new byte[count * sizeof(short)];
        int bi = 0;
        for (int i = 0; i < count; i++)
        {
            float f = MathF.Max(-1f, MathF.Min(1f, samples[i]));
            short s = (short)(f * short.MaxValue);
            pcm[bi++] = (byte)(s & 0xff);
            pcm[bi++] = (byte)((s >> 8) & 0xff);
        }
        return pcm;
    }
}
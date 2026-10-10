using Silk.NET.OpenAL;

namespace Rockwall2.Editor.Common.Utils;
public sealed unsafe class AudioBackend
{
    static AudioBackend instance;
    static int refCount;

    Device* device;
    Context* context;
    ALContext alc;

    /// <summary>The shared AL API instance. Valid as long as at least one caller holds a reference via Acquire().</summary>
    public static AL AL { get; private set; }

    AudioBackend()
    {
        AL = Silk.NET.OpenAL.AL.GetApi();
        alc = ALContext.GetApi();
        device = alc.OpenDevice(null);
        context = alc.CreateContext(device, null);
        alc.MakeContextCurrent(context);
    }

    /// <summary>
    /// Takes a reference on the shared device/context, creating it on first use.
    /// Every caller must pair this with exactly one call to <see cref="Release"/>.
    /// </summary>
    public static void Acquire()
    {
        instance ??= new AudioBackend();
        refCount++;
    }

    /// <summary>
    /// Releases a reference taken via <see cref="Acquire"/>. Tears down the device/context
    /// once the last consumer has released it.
    /// </summary>
    public static void Release()
    {
        if (instance == null) return;

        refCount--;
        if (refCount <= 0)
        {
            instance.Shutdown();
            instance = null;
            refCount = 0;
        }
    }

    void Shutdown()
    {
        alc.MakeContextCurrent(null);
        alc.DestroyContext(context);
        alc.CloseDevice(device);
        AL.Dispose();
        alc.Dispose();
        AL = null;
    }
}
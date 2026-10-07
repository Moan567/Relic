using System.Collections.Concurrent;
using System.Threading;

namespace Engine.Utils;

public sealed class GameLoopSyncContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback callback, object state)> queue = new();

    public override void Post(SendOrPostCallback d, object state)
    {
        queue.Enqueue((d, state));
    }

    public override void Send(SendOrPostCallback d, object state)
    {
        d(state);
    }

    public void Pump()
    {
        int count = queue.Count;
        for (int i = 0; i < count; i++)
        {
            if (queue.TryDequeue(out var item))
            {
                item.callback(item.state);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils;
public readonly struct QueuedDelay
{
    readonly WorldEntity entity;
    readonly float seconds;
    public QueuedDelay(WorldEntity entity, float seconds) { this.entity = entity; this.seconds = seconds; }
    public QueuedDelayAwaiter GetAwaiter() => new(entity, seconds);
}

public readonly struct QueuedDelayAwaiter : INotifyCompletion
{
    readonly WorldEntity entity;
    readonly float seconds;
    public QueuedDelayAwaiter(WorldEntity entity, float seconds) { this.entity = entity; this.seconds = seconds; }
    public bool IsCompleted => seconds <= 0;
    public void OnCompleted(Action continuation) => entity.queuedTasks.Add((seconds, continuation));
    public void GetResult() { }
}
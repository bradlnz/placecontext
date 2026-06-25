using System.Threading.Channels;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// In-process, unbounded <see cref="IJobRunQueue"/> backed by a <see cref="Channel{T}"/>. Trigger
/// firing (cron scheduler + event fan-out) writes; <see cref="TriggerSchedulerService"/> reads and
/// executes. Singleton.
///
/// On k3s this becomes per-pod; a durable/shared queue (or scheduler leader-election) is needed so a
/// run fires exactly once across replicas — see the k3s deployment task.
/// </summary>
public sealed class InMemoryJobRunQueue : IJobRunQueue
{
    private readonly Channel<QueuedJobRun> _channel =
        Channel.CreateUnbounded<QueuedJobRun>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<QueuedJobRun> Reader => _channel.Reader;

    public void Enqueue(QueuedJobRun run) => _channel.Writer.TryWrite(run);
}

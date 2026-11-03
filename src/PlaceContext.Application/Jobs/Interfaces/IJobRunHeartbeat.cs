namespace PlaceContext.Application.Jobs;

/// <summary>A run may execute only while its durable ownership pulse is accepted.</summary>
public interface IJobRunHeartbeat
{
    const int IntervalSeconds = 10;
    const int TimeoutSeconds = 60;
    Task<bool> PulseAsync(Guid runId, CancellationToken ct = default);
}

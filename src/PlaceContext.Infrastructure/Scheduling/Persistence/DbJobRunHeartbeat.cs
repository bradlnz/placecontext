using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Jobs;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Scheduling;

public sealed class DbJobRunHeartbeat(IServiceScopeFactory scopes) : IJobRunHeartbeat
{
    public async Task<bool> PulseAsync(Guid runId, CancellationToken ct = default)
    {
        // The run's DbContext may be saving results concurrently; each pulse owns its context.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobRuns.Where(r => r.Id == runId && r.Status == "Running"
                && (r.HeartbeatAt ?? r.StartedAt) > DateTimeOffset.UtcNow.AddSeconds(-IJobRunHeartbeat.TimeoutSeconds))
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.HeartbeatAt, DateTimeOffset.UtcNow), ct) == 1;
    }
}

using PlaceContext.Application.Billing;

namespace PlaceContext.Infrastructure.Tests.Billing;

internal sealed class ExhaustedAllowance : IRunAllowance
{
    public Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(
        new RunAllowanceSnapshot("basic", "active", 2, DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(29), 60, RunLimit: 2));
    public Task ReserveAsync(Guid projectId, Guid executionId, string kind, CancellationToken ct = default) => throw new NotSupportedException();
}

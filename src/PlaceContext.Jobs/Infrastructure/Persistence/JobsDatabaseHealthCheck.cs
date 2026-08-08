using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

public sealed class JobsDatabaseHealthCheck : IHealthCheck
{
    private readonly JobsDbContext _dbContext;

    public JobsDatabaseHealthCheck(JobsDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await _dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Jobs database is unavailable.");
}

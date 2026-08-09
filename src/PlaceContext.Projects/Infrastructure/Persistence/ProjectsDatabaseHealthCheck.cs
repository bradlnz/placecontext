using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PlaceContext.Projects.Infrastructure.Persistence;

public sealed class ProjectsDatabaseHealthCheck : IHealthCheck
{
    private readonly ProjectsDbContext _dbContext;

    public ProjectsDatabaseHealthCheck(ProjectsDbContext dbContext) => _dbContext = dbContext;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await _dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Projects database is unavailable.");
}

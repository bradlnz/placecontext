using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class CrmDatabaseHealthCheck : IHealthCheck
{
    private readonly CrmDbContext _dbContext;

    public CrmDatabaseHealthCheck(CrmDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await _dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("CRM database is unavailable.");
}

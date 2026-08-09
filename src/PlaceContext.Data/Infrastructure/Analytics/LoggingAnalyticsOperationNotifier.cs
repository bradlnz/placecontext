using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Infrastructure.Analytics;

/// <summary>
/// Standalone Data fallback for advisory analytics progress. Durable chart state remains in Data;
/// Operations integration can replace this notifier at composition time.
/// </summary>
public sealed class LoggingAnalyticsOperationNotifier(
    ILogger<LoggingAnalyticsOperationNotifier> logger) : IBackgroundOperationNotifier
{
    public Guid Track(
        TenantContext tenant,
        Guid? projectId,
        string title,
        string? link,
        string? correlationKey = null)
    {
        var operationId = Guid.NewGuid();
        logger.LogInformation(
            "Analytics operation {OperationId} tracked for tenant {TenantId}, project {ProjectId}: {Title}.",
            operationId,
            tenant.Id,
            projectId,
            title);
        return operationId;
    }

    public void MarkRunning(Guid operationId) =>
        logger.LogInformation("Analytics operation {OperationId} is running.", operationId);

    public void MarkDone(Guid operationId, string? outcome = null) =>
        logger.LogInformation("Analytics operation {OperationId} completed: {Outcome}.", operationId, outcome);

    public void MarkFailed(Guid operationId, string error) =>
        logger.LogError("Analytics operation {OperationId} failed: {Error}.", operationId, error);
}

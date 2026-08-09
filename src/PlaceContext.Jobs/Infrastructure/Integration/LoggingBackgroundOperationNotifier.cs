using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Infrastructure.Integration;

/// <summary>
/// Jobs-local fallback for advisory scheduling progress. Durable run state remains in the Jobs
/// database; Operations can consume these events over HTTP once its ingestion API is enabled.
/// </summary>
public sealed class LoggingBackgroundOperationNotifier(
    ILogger<LoggingBackgroundOperationNotifier> logger) : IBackgroundOperationNotifier
{
    public Guid Track(TenantContext tenant, Guid? projectId, string title, string? link, string? correlationKey = null)
    {
        var id = Guid.NewGuid();
        logger.LogInformation(
            "Operation {OperationId} tracked for tenant {TenantId}, project {ProjectId}: {Title} ({CorrelationKey}).",
            id, tenant.Id, projectId, title, correlationKey);
        return id;
    }

    public void MarkRunning(Guid operationId) =>
        logger.LogInformation("Operation {OperationId} is running.", operationId);

    public void MarkDone(Guid operationId, string? outcome = null) =>
        logger.LogInformation("Operation {OperationId} completed: {Outcome}.", operationId, outcome);

    public void MarkFailed(Guid operationId, string error) =>
        logger.LogError("Operation {OperationId} failed: {Error}.", operationId, error);
}

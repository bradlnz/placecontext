using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Operations;

public sealed class OperationCenterBackgroundOperationNotifier : IBackgroundOperationNotifier
{
    private readonly OperationCenter _operations;

    public OperationCenterBackgroundOperationNotifier(OperationCenter operations)
        => _operations = operations;

    public Guid Track(
        TenantContext tenant,
        Guid? projectId,
        string title,
        string? link,
        string? correlationKey = null)
        => _operations.Track(
            new TenantInfo(tenant.Id, tenant.Slug, tenant.Slug, tenant.TimeZoneId),
            projectId,
            title,
            link,
            correlationKey).Id;

    public void MarkRunning(Guid operationId) => _operations.MarkRunning(operationId);
    public void MarkDone(Guid operationId, string? outcome = null)
        => _operations.MarkDone(operationId, outcome);
    public void MarkFailed(Guid operationId, string error)
        => _operations.MarkFailed(operationId, error);
}

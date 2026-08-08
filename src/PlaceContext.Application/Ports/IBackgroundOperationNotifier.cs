namespace PlaceContext.Application.Ports;

/// <summary>
/// Publishes advisory progress for work driven by a service runtime. The returned identifier is
/// opaque to the service and is used only to update the same notification.
/// </summary>
public interface IBackgroundOperationNotifier
{
    Guid Track(
        TenantContext tenant,
        Guid? projectId,
        string title,
        string? link,
        string? correlationKey = null);

    void MarkRunning(Guid operationId);
    void MarkDone(Guid operationId, string? outcome = null);
    void MarkFailed(Guid operationId, string error);
}

using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

public sealed class FakeBackgroundOperationNotifier : IBackgroundOperationNotifier
{
    public Guid Track(
        TenantContext tenant,
        Guid? projectId,
        string title,
        string? link,
        string? correlationKey = null)
        => Guid.NewGuid();

    public void MarkRunning(Guid operationId) { }
    public void MarkDone(Guid operationId, string? outcome = null) { }
    public void MarkFailed(Guid operationId, string error) { }
}

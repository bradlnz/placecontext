using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Operations;

/// <summary>
/// Feeds run-status updates from the watcher into the notifications pane. Statuses map onto the
/// pane's four states — Partial lands as Succeeded, with the detail line carrying the distinction,
/// matching how in-process callers have always reported it. Tenancy comes from the ambient current
/// tenant, which the watcher loop establishes per tenant before each sweep.
/// </summary>
public sealed class OperationCenterRunStatusNotifier : IRunStatusNotifier
{
    private readonly OperationCenter _ops;
    private readonly ICurrentTenant _tenant;

    public OperationCenterRunStatusNotifier(OperationCenter ops, ICurrentTenant tenant)
    {
        _ops = ops;
        _tenant = tenant;
    }

    public void Sync(RunStatusUpdate update)
    {
        if (!_tenant.IsResolved) return;

        var status = update.Outcome switch
        {
            RunOutcome.Running => PortalOperationStatus.Running,
            RunOutcome.Failed => PortalOperationStatus.Failed,
            _ => PortalOperationStatus.Succeeded,
        };
        _ops.Sync(_tenant.TenantId, update.Key, status, update.Title, update.Link,
            update.ProjectId, update.Detail, update.StartedAt, update.FinishedAt);
    }
}

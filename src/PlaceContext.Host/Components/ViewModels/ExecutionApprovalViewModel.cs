using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Workload;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ExecutionApprovalViewModel(IExecutionApprovals approvals, IPermissionService permissions) : PageViewModel, IComponentViewModel
{
    public IReadOnlyList<ExecutionApproval> Requests { get; private set; } = [];
    public bool CanApprove { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }

    public async Task RefreshAsync(Guid project, IReadOnlyList<Guid> runs, CancellationToken ct)
    {
        try
        {
            CanApprove = await permissions.HasAsync(Permission.JobsManage, ct);
            Requests = await approvals.ListAsync(project, runs, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { Error = "Could not load execution approvals. Reopen the run to reconnect."; }
    }

    public async Task DecideAsync(Guid id, bool approve)
    {
        if (Busy || !CanApprove) return;
        Busy = true;
        Error = null;
        try
        {
            await approvals.DecideAsync(id, approve);
            Requests = Requests.Select(x => x.Id == id ? x with { Status = approve ? "Approved" : "Rejected" } : x).ToArray();
        }
        catch (InvalidOperationException ex) { Error = ex.Message; }
        catch (Exception) { Error = "The decision could not be saved. Refresh the run and check your permissions."; }
        finally { Busy = false; NotifyStateChanged(); }
    }
}

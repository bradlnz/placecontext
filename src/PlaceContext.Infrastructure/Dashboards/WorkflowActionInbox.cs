using System.Text.Json;
using PlaceContext.Application;
using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Dashboards;

/// <summary>
/// Surfaces only real AwaitingInput human Yes/No steps. Delegates actor auditing, idempotency
/// and durable continuation to the existing tenant-scoped submission queue.
/// </summary>
public sealed class WorkflowActionInbox(
    PlaceContextService workflows,
    IJobChainSubmissionQueue submissions,
    IPermissionService permissions) : IWorkflowActionInbox
{
    private const int MaxWorkflows = 50;
    private const int RecentRunsPerWorkflow = 12;

    public async Task<IReadOnlyList<WorkflowApprovalAction>> ListAsync(
        Guid projectId, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A project is required.", nameof(projectId));
        if (!await permissions.HasAsync(Permission.ChainsManage, ct)
            || !await permissions.HasAsync(Permission.JobsRun, ct))
            return []; // Read-only dashboard users must not discover or submit workflow approvals.

        var chains = await workflows.ListJobChainsAsync(projectId, ct);
        var result = new List<WorkflowApprovalAction>();
        // Serial query execution prevents concurrent use of a scoped EF DbContext.
        foreach (var chain in chains.Take(MaxWorkflows))
        {
            ct.ThrowIfCancellationRequested();
            var runs = await workflows.ListChainRunsAsync(chain.Id, RecentRunsPerWorkflow, ct);
            foreach (var run in runs)
            {
                if (run.ProjectId != projectId || run.ChainId != chain.Id) continue;
                if (TryCreate(run) is { } action) result.Add(action);
            }
        }
        return result.OrderBy(x => x.StartedAt).Take(50).ToArray();
    }

    public async Task RespondAsync(
        Guid projectId, Guid chainRunId, Guid requestId, bool approved,
        CancellationToken ct = default)
    {
        if (!await permissions.HasAsync(Permission.ChainsManage, ct)
            || !await permissions.HasAsync(Permission.JobsRun, ct))
            throw new UnauthorizedAccessException("Workflow management and jobs.run permission are required.");

        // Recheck on the server immediately before submitting; the browser's original
        // request can have been completed, changed or replaced by another approver.
        var run = await workflows.GetChainRunAsync(chainRunId, ct)
            ?? throw new InvalidOperationException("The workflow run is no longer available.");
        if (run.ProjectId != projectId || TryCreate(run) is not { } action
            || action.RequestId != requestId)
            throw new InvalidOperationException("This approval is no longer pending. Refresh the inbox.");

        var payload = JsonSerializer.Serialize(
            new Dictionary<string, Dictionary<string, bool>>
            {
                [action.StepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                    new() { [action.ResponseField] = approved }
            });
        // This existing store serializes submissions, records the authenticated actor and
        // resumes the *same* durable run. No ad-hoc approval status is manufactured.
        await submissions.ResumeAsync(chainRunId, requestId, payload, ct);
    }

    public static WorkflowApprovalAction? TryCreate(ChainRunView run)
    {
        var request = run.InputRequest;
        if (run.Status != nameof(ChainRunStatus.AwaitingInput)
            || request is null || request.Id == Guid.Empty
            || request.SubmittedValues is not null
            || request.Kind != LoopChainAction.HumanType
            || request.ResponseType != LoopChainAction.YesNoResponse
            || request.Steps.Count != 1 || request.Steps[0].Parameters.Count != 1)
            return null;

        var step = request.Steps[0];
        var field = step.Parameters[0];
        if (step.Index < 0 || field.Type != "checkbox"
            || string.IsNullOrWhiteSpace(field.Name)
            || field.Name.Length > 80)
            return null;

        return new(run.Id, run.ChainId, request.Id,
            run.ChainName, request.Instructions ?? "Review this workflow output before continuing.",
            run.StartedAt, step.Index, field.Name);
    }
}

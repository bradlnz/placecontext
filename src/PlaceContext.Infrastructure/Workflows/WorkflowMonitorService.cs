using System.Text.Json;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Workflows;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Workflows;

/// <summary>
/// Reads a bounded set of persisted runs; never evaluates models or calls execution just to
/// display a page. Reuses the existing tenant-scoped, idempotent input continuation service.
/// </summary>
public sealed class WorkflowMonitorService(
    PlaceContextService service,
    IPermissionService permissions,
    IJobChainSubmissionQueue submissions) : IWorkflowMonitor
{
    public const int MaxWorkflows = 50;
    public const int RunsPerWorkflow = 12;
    public const int MaxVisibleRuns = 60;

    public async Task<WorkflowMonitorSnapshot> LoadAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project is required.", nameof(projectId));
        if (!await permissions.HasAsync(Permission.JobsView, cancellationToken))
            throw new UnauthorizedAccessException("Permission to monitor jobs is required.");

        var canRespond = await permissions.HasAsync(Permission.ChainsManage, cancellationToken)
            && await permissions.HasAsync(Permission.JobsRun, cancellationToken);
        // Avoid concurrent use of the circuit-scoped EF DbContext.
        var workflows = await service.ListJobChainsAsync(projectId, cancellationToken);
        var runs = new List<WorkflowRunIndicator>();
        var actions = new List<WorkflowHumanAction>();
        var scanned = 0;
        foreach (var workflow in workflows.Take(MaxWorkflows))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            var history = await service.ListChainRunsAsync(workflow.Id, RunsPerWorkflow, cancellationToken);
            foreach (var run in history)
            {
                // Defence in depth: never display a run returned under the wrong project or chain.
                if (run.ProjectId != projectId || run.ChainId != workflow.Id) continue;
                runs.Add(new(run.Id, run.ChainId, run.ChainName, run.Status,
                    run.Steps.Count(x => x.Status == "Succeeded"), run.Steps.Count,
                    run.StartedAt, run.FinishedAt));
                // Never return approval input details to people without approval rights.
                if (canRespond && PendingHumanAction(run) is { } action)
                    actions.Add(action);
            }
        }

        return new(DateTimeOffset.UtcNow, workflows.Count, scanned, RunsPerWorkflow,
            runs.OrderByDescending(x => x.StartedAt).Take(MaxVisibleRuns).ToArray(),
            actions.OrderBy(x => x.StartedAt).Take(MaxVisibleRuns).ToArray(), canRespond);
    }

    public async Task RespondAsync(Guid projectId, Guid runId, Guid requestId,
        bool approved, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || runId == Guid.Empty || requestId == Guid.Empty)
            throw new ArgumentException("A project, run and request are required.");
        if (!await permissions.HasAsync(Permission.ChainsManage, cancellationToken)
            || !await permissions.HasAsync(Permission.JobsRun, cancellationToken))
            throw new UnauthorizedAccessException("Workflow management and execution permissions are required.");

        var run = await service.GetChainRunAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException("The run is no longer available.");
        if (run.ProjectId != projectId) throw new InvalidOperationException("The run is outside this project.");

        // Do not trust a request ID supplied by the browser; the current durable checkpoint is authority.
        var current = PendingHumanAction(run);
        if (current is null || !current.CanAnswerYesNo || current.RequestId != requestId
            || string.IsNullOrWhiteSpace(current.ResponseField))
            throw new InvalidOperationException("The approval is no longer pending. Refresh Monitor.");

        var chains = await service.ListJobChainsAsync(projectId, cancellationToken);
        if (!chains.Any(x => x.Id == run.ChainId))
            throw new InvalidOperationException("This workflow no longer belongs to the selected project.");

        var values = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, bool>>
        {
            [current.StepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                new() { [current.ResponseField] = approved }
        });
        // ResumeAsync enforces tenant scope, checks the request ID under a row lock, saves
        // SubmittedBy and schedules the same workflow run. It rejects stale/conflicting responses.
        await submissions.ResumeAsync(runId, requestId, values, cancellationToken);
    }

    public static WorkflowHumanAction? PendingHumanAction(ChainRunView run)
    {
        var request = run.InputRequest;
        if (run.Status != "AwaitingInput" || request is null || request.Id == Guid.Empty
            || request.SubmittedValues is not null || request.Steps.Count == 0)
            return null;

        // Job parameter prompts (Kind null) and written human input are visible, but handled
        // by the existing Workflows UI rather than being misrepresented as an approval.
        if (request.Kind is not null && request.Kind != LoopChainAction.HumanType)
            return null;
        var yesNo = request.Kind == LoopChainAction.HumanType
            && request.ResponseType == LoopChainAction.YesNoResponse
            && request.Steps.Count == 1
            && request.Steps[0].Index >= 0
            && request.Steps[0].Parameters.Count == 1
            && request.Steps[0].Parameters[0].Type == "checkbox";
        var field = yesNo ? request.Steps[0].Parameters[0].Name : null;
        if (yesNo && (string.IsNullOrWhiteSpace(field) || field.Length > 80
            || !char.IsAsciiLetter(field[0]) || field.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            yesNo = false;
        var instructions = request.Instructions ?? "The workflow needs additional input before continuing.";
        var isApproval = yesNo && ((field?.Contains("approv", StringComparison.OrdinalIgnoreCase) ?? false)
            || instructions.Contains("approv", StringComparison.OrdinalIgnoreCase));
        return new(run.Id, run.ChainId, request.Id, run.ChainName,
            instructions, run.StartedAt, request.Steps[0].Index, yesNo ? field : null, yesNo, isApproval);
    }
}

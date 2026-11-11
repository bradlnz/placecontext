using System.Text.Json.Nodes;
using System.Text.Json;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public sealed record WorkspaceItem(Guid Id, string Kind, string Name, Guid? WorkflowId = null, Guid? TrackingId = null);
    public ChainRunView? PreviewRun { get; private set; }
    public JobChainSubmission? PreviewSubmission { get; private set; }
    public string? PreviewRunStatus => PreviewRun?.Status ?? PreviewSubmission?.Status;
    private CancellationTokenSource? _previewPolling;
    private bool PreviewRunActive => PreviewRunStatus is "Queued" or "Running" or "Waiting" or "Retrying" or "AwaitingInput";
    private int? _previewStepIndex;
    private Guid? _previewChildRunId;
    private int _previewStepVersion;
    public ChainStepRunView? PreviewStep => _previewStepIndex is { } index
        ? PreviewRun?.Steps.FirstOrDefault(x => x.Index == index)
        : PreviewRun?.Steps.FirstOrDefault(x => x.Status == "Running")
            ?? PreviewRun?.Steps.LastOrDefault(x => x.StartedAt is not null) ?? PreviewRun?.Steps.FirstOrDefault();
    public JobRunDetailView? PreviewStepRun { get; private set; }
    public string? PreviewStepError { get; private set; }

    public async Task SelectPreviewStepAsync(ChainStepRunView step)
    {
        if (PreviewRun?.Steps.Any(x => x.Index == step.Index) != true)
        {
            return;
        }
        _previewStepIndex = step.Index;
        _previewChildRunId = null;
        PreviewStepRun = null;
        PreviewStepError = null;
        NotifyStateChanged();
        await RefreshPreviewStepAsync();
    }

    public async Task OpenPreviewStepLogsAsync(Guid runId)
    {
        if (PreviewStep?.AgentActivity?.Any(x => x.RunId == runId) != true)
        {
            return;
        }
        _previewChildRunId = runId;
        PreviewStepRun = null;
        await RefreshPreviewStepAsync();
    }

    private async Task RefreshPreviewStepAsync(CancellationToken ct = default)
    {
        var version = ++_previewStepVersion;
        var previewVersion = _previewVersion;
        var step = PreviewStep;
        var child = step?.AgentActivity?.LastOrDefault(x => x.RunId.HasValue && (_previewChildRunId is null || x.RunId == _previewChildRunId));
        var runId = _previewChildRunId ?? step?.RunId ?? child?.RunId;
        var jobId = runId == step?.RunId ? step?.JobId : child?.JobId;
        if (PreviewStepRun?.Id != runId)
        {
            PreviewStepRun = null;
        }
        PreviewStepError = null;
        if (runId is null)
        {
            return;
        }
        try
        {
            if (!await permissions.HasAsync(Permission.ProjectsView, ct) || !await permissions.HasAsync(Permission.ChainsManage, ct)
                || !await permissions.HasAsync(Permission.JobsView, ct))
            {
                throw new UnauthorizedAccessException();
            }
            var detail = await service.GetJobRunAsync(runId.Value, ct);
            if (detail is not null && (detail.Id != runId || detail.ProjectId != Conversation.ProjectId || (jobId is { } expected && detail.JobId != expected)))
            {
                throw new UnauthorizedAccessException();
            }
            if (previewVersion == _previewVersion && version == _previewStepVersion && PreviewStep?.Index == step?.Index)
            {
                PreviewStepRun = detail;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (previewVersion == _previewVersion && version == _previewStepVersion)
            {
                PreviewStepRun = null;
                PreviewStepError = "Could not load this step's job output. Check access and select the step to retry.";
            }
        }
        NotifyStateChanged();
    }
    public sealed record ChartPreview(ChartSpec Spec, string Source);
    public ChartPreview? PreviewChart
    {
        get; private set;
    }
    public ChartPreview? ResultChart(JsonNode message)
    {
        var callId = message["tool_call_id"]?.ToString();
        if (callId is null || !Conversation.Messages.OfType<JsonNode>().SelectMany(Calls)
            .Any(call => call["id"]?.ToString() == callId && call["function"]?["name"]?.ToString() == "show_chart"))
        {
            return null;
        }
        try
        {
            var result = JsonNode.Parse(Text(message));
            var spec = result?["chart"]?.ToJsonString();
            var source = result?["source"]?.GetValue<string>();
            return spec is not null && ChartSpec.TryParse(spec) is { } chart && !string.IsNullOrWhiteSpace(source)
                ? new(chart, source) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
    public void OpenChart(ChartPreview chart)
    {
        CloseWorkspace();
        OpenWorkspace();
        PreviewChart = chart;
    }

    public WorkspaceItem? PreviewItem
    {
        get; private set;
    }
    public JobChainView? PreviewWorkflow
    {
        get; private set;
    }
    public JobView? PreviewJob
    {
        get; private set;
    }
    public bool PreviewLoading
    {
        get; private set;
    }
    public string? PreviewError
    {
        get; private set;
    }
    public string? PreviewEditorUrl => PreviewItem is not { } item ? null
        : item.Kind == "workflow-run"
            ? item.WorkflowId is { } workflowId ? $"/project/{Conversation.ProjectId}/workflows/{workflowId}/runs/{item.Id}" : null
        : item.Kind == "workflow"
            ? $"/project/{Conversation.ProjectId}/workflows/{item.Id}"
            : $"/project/{Conversation.ProjectId}/jobs?job={item.Id}";
    private int _previewVersion;
    private string? _autoPreviewCall;

    public IReadOnlyList<WorkspaceItem> ResultItems(JsonNode message)
    {
        var callId = message["tool_call_id"]?.ToString();
        if (callId is null)
        {
            return [];
        }
        var call = Conversation.Messages.OfType<JsonNode>().SelectMany(Calls)
                    .FirstOrDefault(x => x["id"]?.ToString() == callId);
        var function = call?["function"]?["name"]?.ToString();
        if (function is not ("project_context" or "list_workflows" or "get_workflow" or "save_workflow"
            or "list_jobs" or "get_job" or "save_job" or "run_workflow" or "list_workflow_runs"))
        {
            return [];
        }
        try
        {
            var value = JsonNode.Parse(Text(message));
            var items = new List<WorkspaceItem>();
            void Add(JsonNode? node, string kind)
            {
                if (node is JsonArray array)
                {
                    foreach (var entry in array)
                    {
                        Add(entry, kind);
                    }
                    return;
                }
                if (node is not JsonObject obj)
                {
                    return;
                }
                string? Read(string key) => obj.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                var name = Read("name") ?? Read("chainName");
                if (Guid.TryParse(Read("id"), out var parsed) && parsed != Guid.Empty)
                {
                    items.Add(new(parsed, kind, name ?? (kind == "workflow-run" ? "Workflow run" : kind == "workflow" ? "Workflow" : "Job"),
                        Guid.TryParse(Read("chainId"), out var workflowId) ? workflowId : null,
                        Guid.TryParse(Read("trackingId"), out var trackingId) ? trackingId : null));
                }
            }
            void ApplyToolContext()
            {
                if (function == "project_context" && value is JsonObject context)
                {
                    Add(context["Workflows"], "workflow");
                    Add(context["Jobs"], "job");
                    return;
                }
                Add(value, function is "run_workflow" or "list_workflow_runs" ? "workflow-run"
                    : function.Contains("workflow", StringComparison.Ordinal) ? "workflow" : "job");
            }
            ApplyToolContext();
            return items.DistinctBy(x => (x.Kind, x.Id)).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task OpenPreviewAsync(WorkspaceItem item)
    {
        if (item.Kind is not ("workflow" or "job" or "workflow-run"))
        {
            return;
        }
        AgentOpen = false;
        ClosePreview();
        _autoPreviewCall = Conversation.Messages.OfType<JsonNode>().LastOrDefault(x => Role(x) == "tool")?["tool_call_id"]?.ToString();
        if (item.Kind != "workflow-run")
        {
            NavigateToDashboard();
        }
        OpenWorkspace();
        PreviewChart = null;
        var version = ++_previewVersion;
        var projectId = Conversation.ProjectId;
        PreviewItem = item;
        PreviewWorkflow = null;
        PreviewJob = null;
        PreviewError = null;
        PreviewLoading = true;
        NotifyStateChanged();
        try
        {
            if (item.Kind == "workflow-run")
            {
                await RefreshPreviewRunAsync();
                if (version == _previewVersion && PreviewError is null && navigation is not null)
                {
                    _requestedRunId = item.Id;
                    var route = PageRoutes.DashboardRun(projectId, item.Id, item.TrackingId);
                    if (navigation.Uri != navigation.ToAbsoluteUri(route).AbsoluteUri)
                    {
                        navigation.NavigateTo(route, replace: true);
                    }
                }
                if (version == _previewVersion && PreviewError is null && PreviewRunActive)
                {
                    _previewPolling = new();
                    _ = PollPreviewRunAsync(_previewPolling.Token);
                }
                return;
            }
            var permission = item.Kind == "workflow" ? Permission.ChainsManage : Permission.JobsView;
            if (!await permissions.HasAsync(permission))
            {
                throw new UnauthorizedAccessException();
            }
            async Task LoadWorkspaceItemAsync()
            {
                if (item.Kind == "workflow")
                {
                    var workflow = (await service.ListJobChainsAsync(projectId))
                        .FirstOrDefault(x => x.Id == item.Id && x.ProjectId == projectId)
                        ?? throw new InvalidOperationException();
                    if (version == _previewVersion)
                    {
                        PreviewWorkflow = workflow;
                        PreviewItem = item with
                        {
                            Name = workflow.Name
                        };
                    }
                    return;
                }
                {
                    var job = await service.GetJobAsync(item.Id);
                    if (job is null || job.ProjectId != projectId)
                    {
                        throw new InvalidOperationException();
                    }
                    if (version == _previewVersion)
                    {
                        PreviewJob = job;
                        PreviewItem = item with
                        {
                            Name = job.Name
                        };
                    }
                }
            }
            await LoadWorkspaceItemAsync();
        }
        catch (Exception)
        {
            if (version == _previewVersion)
            {
                PreviewError = "Could not open this item. It may have been removed or access is restricted.";
            }
        }
        finally
        {
            if (version == _previewVersion)
            {
                PreviewLoading = false;
            }
            NotifyStateChanged();
        }
    }
    public Task OpenJobPreviewAsync(Guid id) => OpenPreviewAsync(new(id, "job", "Job"));
    public async Task RefreshPreviewRunAsync(CancellationToken ct = default)
    {
        if (PreviewItem is not { Kind: "workflow-run" } item)
        {
            return;
        }
        var version = _previewVersion;
        var projectId = Conversation.ProjectId;
        try
        {
            if (!await permissions.HasAsync(Permission.ProjectsView, ct) || !await permissions.HasAsync(Permission.ChainsManage, ct))
            {
                throw new UnauthorizedAccessException();
            }
            var run = await service.GetChainRunAsync(item.Id, ct);
            JobChainSubmission? receipt = null;
            var name = run?.ChainName ?? "Workflow run";
            var workflowId = run?.ChainId ?? Guid.Empty;
            if (run is not null && (run.Id != item.Id || run.ProjectId != projectId || (item.WorkflowId is { } expected && run.ChainId != expected)))
            {
                throw new InvalidOperationException();
            }
            if (run is null)
            {
                receipt = item.TrackingId is { } trackingId && submissions is not null ? await submissions.GetAsync(trackingId, ct) : null;
                if (receipt is null || receipt.ProjectId != projectId || receipt.ChainRunId != item.Id
                    || (item.WorkflowId is { } expectedReceipt && receipt.ChainId != expectedReceipt))
                {
                    throw new InvalidOperationException();
                }
                var workflow = (await service.ListJobChainsAsync(projectId, ct))
                    .SingleOrDefault(x => x.Id == receipt.ChainId && x.ProjectId == projectId) ?? throw new InvalidOperationException();
                name = workflow.Name;
                workflowId = workflow.Id;
            }
            if (version != _previewVersion || ct.IsCancellationRequested)
            {
                return;
            }
            PreviewRun = run;
            PreviewSubmission = receipt;
            PreviewItem = item with { Name = name, WorkflowId = workflowId };
            PreviewError = null;
            await RefreshPreviewStepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (version != _previewVersion)
            {
                return;
            }
            PreviewRun = null;
            PreviewSubmission = null;
            PreviewStepRun = null;
            PreviewError = "Could not load this run. It may have been removed or access is restricted. Try again to reconnect.";
        }
        if (version == _previewVersion)
        {
            NotifyStateChanged();
        }
    }

    private async Task PollPreviewRunAsync(CancellationToken ct)
    {
        try
        {
            while (PreviewRunActive && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                await RefreshPreviewRunAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public void ClosePreview()
    {
        CloseWorkflowPicker();
        ShowDemoShare = false;
        DemoUrl = DemoError = null;
        DemoExpiry = null;
        ++_previewVersion;
        ++_previewStepVersion;
        _previewStepIndex = null;
        _previewChildRunId = null;
        PreviewStepRun = null;
        PreviewStepError = null;
        _previewPolling?.Cancel();
        _previewPolling?.Dispose();
        _previewPolling = null;
        PreviewRun = null;
        PreviewSubmission = null;
        PreviewChart = null;
        PreviewItem = null;
        PreviewWorkflow = null;
        PreviewJob = null;
        PreviewError = null;
        PreviewLoading = false;
    }
    private void ResetWorkspace()
    {
        _handledWorkflowPicker = 0;
        AgentOpen = false;
        ClosePreview();
        _workspaceVisible = null;
        _autoPreviewCall = null;
    }
    private void NavigateToDashboard()
    {
        if (navigation is null)
        {
            return;
        }
        var route = $"/project/{Conversation.ProjectId}/dashboard";
        if (new Uri(navigation.Uri).AbsolutePath.StartsWith(route + "/runs/", StringComparison.Ordinal))
        {
            _requestedRunId = null;
            navigation.NavigateTo(route, replace: true);
        }
    }
    private async Task AutoPreviewAsync()
    {
        if (AgentOpen || WorkflowPickerOpen || PreviewLoading || _workspaceVisible == false)
        {
            return;
        }
        if (Conversation.Messages.LastOrDefault()?["dashboard_action"]?.ToString() == "choose_workflow"
            && _handledWorkflowPicker != Conversation.Messages.Count)
        {
            _handledWorkflowPicker = Conversation.Messages.Count;
            await OpenWorkflowPickerAsync();
            return;
        }
        foreach (var message in Conversation.Messages.OfType<JsonNode>().Reverse().Where(x => Role(x) == "tool"))
        {
            var chart = ResultChart(message);
            if (chart is not null)
            {
                var chartCall = message["tool_call_id"]?.ToString();
                if (chartCall == _autoPreviewCall)
                {
                    return;
                }
                _autoPreviewCall = chartCall;
                OpenChart(chart);
                return;
            }
            var items = ResultItems(message);
            if (items.Count == 0)
            {
                continue;
            }
            var call = message["tool_call_id"]?.ToString();
            if (call == _autoPreviewCall)
            {
                return;
            }
            _autoPreviewCall = call;
            await OpenPreviewAsync(items.FirstOrDefault(x => x.Kind == "workflow") ?? items[0]);
            return;
        }
    }
}

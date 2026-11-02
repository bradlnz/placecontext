using System.Globalization;
using System.Text.Json;
using PlaceContext.Application.AgentWorkflows;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AgentWorkflowsViewModel(IAgentWorkflows workflows, PortalUiState ui) : PageViewModel, IComponentViewModel, IDisposable
{
    public AgentWorkspace? Workspace { get; private set; }
    public AgentWorkflow? Selected { get; private set; }
    public AgentRun? Result { get; private set; }
    public Dictionary<string, string> Inputs { get; } = [];
    public string Industry { get; set; } = "";
    public string Search { get; set; } = "";
    public AgentBrowseFilter Filter { get; private set; }
    public bool ShowHistory { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public Guid ProjectId { get; private set; }
    public IEnumerable<string> Industries => Workspace?.Workflows.Select(x => x.Industry).Distinct() ?? [];
    public IEnumerable<AgentWorkflow> VisibleWorkflows => (Workspace?.Workflows ?? [])
        .Where(x => Industry.Length == 0 || x.Industry == Industry)
        .Where(x => Filter switch { AgentBrowseFilter.Free => !x.Paid, AgentBrowseFilter.Subscribed => Subscribed(x.Id), _ => true })
        .Where(x => string.IsNullOrWhiteSpace(Search) || $"{x.Name} {x.Description} {x.Industry}".Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.Paid);
    public AgentWorkflow? Featured => Workspace?.Workflows.FirstOrDefault(x => x.Id == "services");
    public int SubscriptionCount => Workspace?.Subscriptions.Count(x => x.Active) ?? 0;
    public string DashboardsUrl => $"/project/{ProjectId}/dashboards";
    public void Browse(AgentBrowseFilter filter) { Filter = filter; ShowHistory = false; }
    public void ToggleHistory() => ShowHistory = !ShowHistory;
    public void CloseDetails() { if (!Busy) { Selected = null; Result = null; } }
    public static string IndustryIcon(AgentWorkflow workflow) => workflow.Industry switch
    { "Retail" => "box", "Professional services" => "human", "Property operations" => "grid", _ => "chain" };
    public static string IndustryClass(AgentWorkflow workflow) => workflow.Industry switch
    { "Retail" => "retail", "Professional services" => "services", "Property operations" => "property", _ => "general" };
    public static string PriceLabel(AgentWorkflow workflow) => workflow.Paid ? "1 demo credit" : "Always free";
    public static string PriceUnit(AgentWorkflow workflow) => workflow.Paid ? "per 1,000 model tokens" : "no AI tokens required";
    public bool CanRun => !Busy && Selected is not null && (!Selected.Paid || Subscribed(Selected.Id));
    public bool CanManageSubscriptions => Workspace is { SimulationEnabled: true, CanManageSubscriptions: true };
    public string BuildUrl => $"/project/{ProjectId}/dashboard";
    private readonly CancellationTokenSource _stop = new();
    private Guid? _requestId;
    private string? _requestInput;
    private Task _operation = Task.CompletedTask;
    private bool _disposed;

    public async Task LoadAsync(Guid projectId)
    {
        await _operation;
        if (_disposed) return;
        if (ProjectId == projectId && Workspace is not null) return;
        ProjectId = projectId;
        Selected = null; Result = null; Inputs.Clear(); Industry = Search = ""; Filter = AgentBrowseFilter.All;
        ui.Set("Build", "Workflow store");
        await RefreshAsync();
    }

    public bool Subscribed(string id) => Workspace?.Subscriptions.Any(x => x.AgentId == id && x.Active) == true;
    public string AccessLabel(AgentWorkflow workflow) => !workflow.Paid ? "Free" : Subscribed(workflow.Id) ? "Subscribed · simulation" : "Subscription required";
    public static string FieldLabel(string field) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(field);
    public string WorkflowName(string id) => Workspace?.Workflows.FirstOrDefault(x => x.Id == id)?.Name ?? id;
    public static string StatusLabel(AgentRun run) => run.Status switch
    {
        AgentRunStatus.NeedsReview => "Needs review", AgentRunStatus.Completed => "Completed",
        AgentRunStatus.Running => "Running", AgentRunStatus.Interrupted => "Interrupted", _ => "Failed"
    };
    public static string TokenLabel(AgentRun run) => run.InputTokens is null || run.OutputTokens is null
        ? "Usage not reported" : $"{run.InputTokens:N0} input / {run.OutputTokens:N0} output";
    public static string CreditLabel(AgentRun run) => run.DemoCredits is { } credits ? $"{credits:0.######} demo credits" : "Charge unavailable";
    public static string StartedLabel(AgentRun run) => run.StartedAt.ToLocalTime().ToString("g");

    public void Select(AgentWorkflow workflow)
    {
        if (Busy) return;
        Selected = workflow; Result = null; Error = null; _requestId = null; _requestInput = null;
        Inputs.Clear();
        foreach (var field in workflow.InputFields) Inputs[field] = "";
    }

    public void OpenResult(AgentRun run)
    {
        if (Busy) return;
        var workflow = Workspace?.Workflows.FirstOrDefault(x => x.Id == run.AgentId);
        if (workflow is not null) Select(workflow);
        Result = run;
    }
    public Task RefreshAsync() => ExecuteAsync(async () => Workspace = await workflows.LoadAsync(ProjectId, _stop.Token));

    public Task ToggleSubscriptionAsync(AgentWorkflow workflow) => ExecuteAsync(async () =>
    {
        await workflows.SetSubscriptionAsync(ProjectId, workflow.Id, !Subscribed(workflow.Id), _stop.Token);
        Workspace = await workflows.LoadAsync(ProjectId, _stop.Token);
    });

    public Task RunAsync() => ExecuteAsync(async () =>
    {
        if (Selected is null) return;
        var input = JsonSerializer.Serialize(Inputs);
        if (_requestId is null || _requestInput != input) { _requestId = Guid.NewGuid(); _requestInput = input; }
        Result = await workflows.RunAsync(ProjectId, Selected.Id, _requestId.Value, input, _stop.Token);
        Workspace = await workflows.LoadAsync(ProjectId, _stop.Token);
        if (Result.Status != AgentRunStatus.Running) _requestId = null;
    });

    private Task ExecuteAsync(Func<Task> action)
    {
        if (Busy || _disposed) return Task.CompletedTask;
        return _operation = ExecuteCoreAsync(action);
    }

    private async Task ExecuteCoreAsync(Func<Task> action)
    {
        Busy = true; Error = null; NotifyStateChanged();
        try { await action(); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; NotifyStateChanged(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _stop.Cancel(); _stop.Dispose(); Detach();
    }
}

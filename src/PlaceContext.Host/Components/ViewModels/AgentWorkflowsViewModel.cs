using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PlaceContext.Application.AgentWorkflows;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AgentWorkflowsViewModel(IAgentWorkflows workflows, PortalUiState ui) : PageViewModel, IComponentViewModel, IDisposable
{
    public AgentWorkspace? Workspace { get; private set; }
    public AgentWorkflow? Selected { get; private set; }
    public AgentRun? Result { get; private set; }
    public AgentPreparation? Preparation { get; private set; }
    public bool ProjectContextApplied { get; private set; }
    // Connector selection is a reference to an authorised source, never copied record data.
    public string ConnectedSourceName { get; private set; } = "";
    public string WorkflowName { get; set; } = "";
    public bool NamingWorkflow { get; private set; }
    public IReadOnlyList<AgentContextSource> ConnectorSources => Preparation?.Sources
        .Where(source => !string.IsNullOrWhiteSpace(source.Name) && source.Name.Length <= 160
            && !source.Name.Any(char.IsControl))
        .GroupBy(source => source.Name, StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .Select(group => group.Single()).Take(20).ToArray() ?? [];
    public bool CanGenerateWorkflow => !Busy && Selected is not null
        && ConnectorSources.Any(source => source.Name == ConnectedSourceName);
    public string? WorkflowNameError => string.IsNullOrWhiteSpace(WorkflowName) || WorkflowName.Trim().Length < 3
        ? "Enter at least 3 characters." : WorkflowName.Trim().Length > 100
            ? "Use a name of 100 characters or fewer." : null;
    public string? NamedWorkflowDraftUrl
    {
        get
        {
            if (!CanGenerateWorkflow || !NamingWorkflow || WorkflowNameError is not null || Selected is null)
                return null;
            var source = ConnectorSources.First(source => source.Name == ConnectedSourceName);
            var template = Workspace?.Workflows.FirstOrDefault(x => x.UseCaseId == Selected.UseCaseId && x.Paid) ?? Selected;
            // Pass the source reference only. Build rechecks scope and consent; no source samples go in the URL.
            var request = $"Create a workflow named \"{WorkflowName.Trim()}\" from the \"{template.UseCaseName}\" mini app. " +
                $"Use the existing project source connector \"{source.Name}\" ({source.Kind}) only after verifying " +
                "its current project permissions, available schema and data freshness. " +
                $"Objective: {template.Outcome} Expected outputs: {string.Join("; ", template.Deliverables.Take(3))}. " +
                "Build the smallest testable draft, with measured indicators and Jev review steps. " +
                "If Jev support is low or evidence is missing, route the case to a durable Human in the loop Yes/No approval checkpoint. " +
                "Require review of source evidence before approving, and configure a safe No branch that prevents external action. " +
                "Ask only for essential missing details. Do not execute external actions or enable a schedule without approval.";
            return $"{BuildUrl}?prompt={Uri.EscapeDataString(request)}";
        }
    }
    public void ConnectSource(string? sourceName)
    {
        if (Busy) return;
        ConnectedSourceName = ConnectorSources.Any(source => source.Name == sourceName) ? sourceName! : "";
        NamingWorkflow = false;
        WorkflowName = "";
    }
    public void AskWorkflowName()
    {
        if (!CanGenerateWorkflow || Selected is null) return;
        WorkflowName = Selected.UseCaseName;
        NamingWorkflow = true;
    }
    public void CancelWorkflowName()
    {
        NamingWorkflow = false;
        WorkflowName = "";
    }
    public bool PreparingContext { get; private set; }
    public Dictionary<string, string> Inputs { get; } = [];
    public string Industry { get; set; } = "";
    public string Search { get; set; } = "";
    public AgentBrowseFilter Filter { get; private set; }
    public bool ShowHistory { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public Guid ProjectId { get; private set; }
    public IEnumerable<string> Industries => (Workspace?.Workflows.Select(x => x.Industry).Distinct() ?? []).Order();
    public IEnumerable<AgentWorkflow> VisibleWorkflows => (Workspace?.Workflows ?? [])
        .Where(x => Industry.Length == 0 || x.Industry == Industry)
        .Where(x => Filter switch { AgentBrowseFilter.Free => !x.Paid, AgentBrowseFilter.Subscribed => Subscribed(x.Id), _ => true })
        .Where(MatchesSearch);
    public IEnumerable<AgentUseCase> VisibleUseCases
    {
        get
        {
            var matchingIds = VisibleWorkflows.Select(x => x.UseCaseId).ToHashSet(StringComparer.Ordinal);
            return (Workspace?.Workflows ?? []).GroupBy(x => x.UseCaseId)
                .Where(group => matchingIds.Contains(group.Key))
                .Select(group => new AgentUseCase(group.Key, group.First().UseCaseName, group.ToArray()));
        }
    }
    public IEnumerable<AgentWorkflow> SuggestedAgents => (Workspace?.Workflows ?? [])
        .Where(x => x.Paid).Take(3);
    public int UseCaseCount => Workspace?.Workflows.Select(x => x.UseCaseId).Distinct().Count() ?? 0;
    public int SubscriptionCount => Workspace?.Subscriptions.Count(x => x.Active) ?? 0;
    public string DashboardsUrl => $"/project/{ProjectId}/dashboards";
    public void Browse(AgentBrowseFilter filter) { Filter = filter; ShowHistory = false; }
    public void ClearFilters() { Search = Industry = ""; Browse(AgentBrowseFilter.All); }
    public void ToggleHistory() => ShowHistory = !ShowHistory;
    public void CloseDetails() { if (!Busy) { Selected = null; Result = null; Preparation = null; ProjectContextApplied = false; ConnectedSourceName = ""; CancelWorkflowName(); } }
    public static string IndustryIcon(AgentWorkflow workflow) => workflow.Industry switch
    {
        "Enterprise communications" => "mic", "Service operations" => "message",
        "Retail" => "box", "Professional services" => "human", "Property operations" => "grid", _ => "chain"
    };
    public static string PriceLabel(AgentWorkflow workflow) => workflow.Paid ? "1 demo credit" : "Free readiness check";
    public static string PriceUnit(AgentWorkflow workflow) => workflow.Paid ? "per 1,000 model tokens" : "no AI tokens required";
    public bool CanRun => !Busy && Selected is not null && (!Selected.Paid || Subscribed(Selected.Id));
    public bool CanStart => CanRun && HasContext && (Selected is { Paid: false } || MissingContextFields.Count == 0);
    public IReadOnlyList<string> MissingContextFields => Selected?.RequiredInputFields
        .Where(inputName => !Inputs.TryGetValue(inputName, out var value) || string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
    public bool HasPreparedContext => Preparation?.Inputs.Any(x => !string.IsNullOrWhiteSpace(x.Value)) == true;
    public bool HasContext => Inputs.Any(x => !string.IsNullOrWhiteSpace(x.Value));
    public bool CanRunAiPlan => Selected is { Paid: true } selected && Workspace is { SimulationEnabled: true } && Subscribed(selected.Id);
    public bool CanManageSubscriptions => Workspace is { SimulationEnabled: true, CanManageSubscriptions: true };
    public string BuildUrl => $"/project/{ProjectId}/build";
    public string BuildAgentUrl => Selected is { } selected
        ? $"{BuildUrl}?prompt={Uri.EscapeDataString(BuildPrompt(Workspace?.Workflows.FirstOrDefault(x => x.UseCaseId == selected.UseCaseId && x.Paid) ?? selected))}"
        : BuildUrl;
    public IReadOnlyList<AgentWorkflow> SelectedVariants => Selected is { } selected
        ? (Workspace?.Workflows ?? []).Where(x => x.UseCaseId == selected.UseCaseId).OrderBy(x => x.Paid).ToArray() : [];
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
        Workspace = null;
        Selected = null; Result = null; Preparation = null; ProjectContextApplied = false; ConnectedSourceName = ""; CancelWorkflowName();
        Inputs.Clear(); Industry = Search = ""; Filter = AgentBrowseFilter.All; ShowHistory = false;
        ui.Set("Build", "Agent Store");
        await RefreshAsync();
    }

    public bool Subscribed(string id) => Workspace?.Subscriptions.Any(x => x.AgentId == id && x.Active) == true;
    public string AccessLabel(AgentWorkflow workflow) => !workflow.Paid ? "Local check" : Subscribed(workflow.Id) ? "Enabled · simulation" : "AI plan";
    public static string FieldLabel(string field) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
        Regex.Replace(field.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ", RegexOptions.CultureInvariant));
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

    private bool MatchesSearch(AgentWorkflow workflow)
    {
        if (string.IsNullOrWhiteSpace(Search)) return true;
        var searchable = string.Join(' ', workflow.Name, workflow.UseCaseName, workflow.Description,
            workflow.Industry, workflow.Outcome, workflow.IntendedFor, string.Join(' ', workflow.DataSources),
            string.Join(' ', workflow.Deliverables));
        return Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildPrompt(AgentWorkflow workflow) =>
        $"Build an agent for this outcome: {workflow.UseCaseName}. " +
        $"Use case: {workflow.Outcome} Intended users: {workflow.IntendedFor}. " +
        $"Relevant sources: {string.Join(", ", workflow.DataSources)}. " +
        $"Expected outputs: {string.Join("; ", workflow.Deliverables)}. " +
        "Inspect the data and capabilities already available in this project first. " +
        "Propose the workflow and ask only for missing essential context. " +
        "Explain evidence coverage and uncertainty, and confirm any external actions before execution.";

    public void Select(AgentWorkflow workflow)
    {
        if (Busy) return;
        Selected = workflow; Result = null; Error = null; _requestId = null; _requestInput = null;
        Preparation = null; ProjectContextApplied = false; ConnectedSourceName = ""; CancelWorkflowName();
        Inputs.Clear();
        foreach (var field in workflow.InputFields) Inputs[field] = "";
    }

    public Task SelectUseCaseAsync(AgentWorkflow workflow)
    {
        var entry = Workspace?.Workflows.FirstOrDefault(x => x.UseCaseId == workflow.UseCaseId && !x.Paid) ?? workflow;
        return SelectAsync(entry);
    }

    public async Task SelectAsync(AgentWorkflow workflow)
    {
        if (Busy) return;
        var previous = Selected;
        var previousInputs = Inputs.ToDictionary(x => x.Key, x => x.Value);
        var previousPreparation = Preparation;
        var previousApplied = ProjectContextApplied;
        var previousConnector = ConnectedSourceName;
        Select(workflow);
        if (previous?.UseCaseId == workflow.UseCaseId && previousPreparation is not null)
        {
            Preparation = previousPreparation;
            ProjectContextApplied = previousApplied;
            ConnectSource(previousConnector);
            foreach (var field in workflow.InputFields)
                if (previousInputs.TryGetValue(field, out var value)) Inputs[field] = value;
            return;
        }
        await PrepareContextAsync();
    }

    public Task PrepareContextAsync() => ExecuteAsync(async () =>
    {
        if (Selected is null) return;
        PreparingContext = true;
        try
        {
            Preparation = await workflows.PrepareAsync(ProjectId, Selected.Id, _stop.Token);
            // Auto-select only an unambiguous single source. Multiple sources require a deliberate choice.
            var choices = ConnectorSources;
            ConnectedSourceName = choices.Count == 1 ? choices[0].Name : "";
        }
        finally { PreparingContext = false; }
    });

    public void UseProjectContext()
    {
        if (Busy || Selected is null || Preparation is null) return;
        foreach (var field in Selected.InputFields)
            if (Preparation.Inputs.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)) Inputs[field] = value;
        ProjectContextApplied = true;
        Result = null;
        _requestId = null;
        _requestInput = null;
    }

    public void SetInput(string field, string? value)
    {
        if (Busy || !Inputs.TryGetValue(field, out var previous) || previous == value) return;
        Inputs[field] = value ?? "";
        Result = null;
        _requestId = null;
        _requestInput = null;
    }

    public static string ContextTimestamp(AgentContextSource source) => source.UpdatedAt is { } at
        ? $"Updated {at.ToLocalTime():g}" : "Source timestamp unavailable";

    public AgentReadinessResult? ReadinessResult
    {
        get
        {
            if (Selected is not { Paid: false } || Result?.Output is not { } output) return null;
            try
            {
                using var document = JsonDocument.Parse(output);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("missing", out var missing) || missing.ValueKind != JsonValueKind.Array
                    || !root.TryGetProperty("nextStep", out var next) || next.ValueKind != JsonValueKind.String)
                    return null;
                return new(next.GetString() ?? "", missing.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String).Select(x => FieldLabel(x.GetString()!)).ToArray());
            }
            catch (JsonException) { return null; }
        }
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

    public Task RunAsync() => !CanStart ? Task.CompletedTask : ExecuteAsync(async () =>
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

    public sealed record AgentUseCase(string Id, string Name, IReadOnlyList<AgentWorkflow> Variants)
    {
        public AgentWorkflow Summary => Variants.FirstOrDefault(x => x.Paid) ?? Variants[0];
    }

    public sealed record AgentReadinessResult(string Summary, IReadOnlyList<string> MissingFields);
}

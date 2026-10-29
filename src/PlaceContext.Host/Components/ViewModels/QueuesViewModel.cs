using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class QueuesViewModel(IChainQueueService queues, PlaceContextService service, PortalUiState ui,
    NavigationManager navigation) : PageViewModel
{
    public Guid ProjectId { get; private set; }
    public Guid? DetailQueueId { get; private set; }
    public string Tab { get; private set; } = "messages";
    public string Search { get; set; } = "";
    public string IntakeFilter { get; set; } = "";
    public string Sort { get; set; } = "created";
    public string MessageSearch { get; set; } = "";
    public string MessageFilter { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; private set; }
    public JobChainSubmission? InspectMessage { get; private set; }
    public Guid? InspectRunId { get; private set; }
    public ChainRunView? InspectRun { get; private set; }
    public JobRunDetailView? InspectJob { get; private set; }
    public Guid? InspectJobId { get; private set; }
    public bool InspectLoading { get; private set; }
    public string? InspectError { get; private set; }
    private int _inspectVersion;

    public async Task InspectAsync(JobChainSubmission message, Guid? runId = null)
    {
        CloseInspect();
        var version = _inspectVersion;
        var id = runId ?? message.ChainRunId;
        if (DetailQueueId is null || !Messages.Any(item => item.TrackingId == message.TrackingId)
            || (id != message.ChainRunId && !message.PreviousRunIds.Contains(id))) return;
        InspectMessage = message;
        InspectRunId = id;
        InspectLoading = true;
        NotifyStateChanged();
        try
        {
            var run = await service.GetChainRunAsync(id);
            if (version != _inspectVersion) return;
            if (run is null || run.ProjectId != ProjectId) throw new InvalidOperationException("This workflow run is no longer available.");
            InspectRun = run;
            await SelectInspectJobAsync(run.Steps.LastOrDefault(step => step.RunId.HasValue)?.RunId?.ToString());
        }
        catch (Exception ex) { if (version == _inspectVersion) InspectError = ex.Message; }
        finally { if (version == _inspectVersion) { InspectLoading = false; NotifyStateChanged(); } }
    }

    public Task RefreshInspectAsync() => InspectMessage is { } message ? InspectAsync(message, InspectRunId) : Task.CompletedTask;
    public Task InspectStepAsync(ChainStepRunView step) => SelectInspectJobAsync(step.RunId?.ToString());

    public async Task SelectInspectJobAsync(string? value)
    {
        var version = _inspectVersion;
        InspectJob = null;
        InspectError = null;
        InspectJobId = Guid.TryParse(value, out var id) && InspectRun?.Steps.Any(step => step.RunId == id) == true ? id : null;
        if (InspectJobId is not { } selected) return;
        try
        {
            var job = await service.GetJobRunAsync(selected);
            if (version == _inspectVersion && InspectJobId == selected) InspectJob = job;
        }
        catch (Exception ex) { if (version == _inspectVersion && InspectJobId == selected) InspectError = ex.Message; }
        NotifyStateChanged();
    }

    public void CloseInspect()
    {
        _inspectVersion++;
        InspectMessage = null;
        InspectRunId = null;
        InspectRun = null;
        InspectJob = null;
        InspectJobId = null;
        InspectError = null;
        InspectLoading = false;
        NotifyStateChanged();
    }
    public string ListUrl => $"/project/{ProjectId}/queues";
    public string QueueUrl(Guid id) => $"{ListUrl}/{id}";
    public IReadOnlyList<ChainQueueDefinition> VisibleQueues
    {
        get
        {
            var filtered = Queues.Where(queue => MatchesQueue(queue, ChainName(queue.ChainId), Search, IntakeFilter));
            return (Sort == "name" ? filtered.OrderBy(queue => queue.Name, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderByDescending(queue => queue.CreatedAt)).ToList();
        }
    }
    public IEnumerable<JobChainSubmission> VisibleMessages => Messages.Where(message =>
        (MessageFilter.Length == 0 || MessageState(message, UpdatedAt ?? DateTimeOffset.UtcNow) == MessageFilter)
        && (MessageSearch.Length == 0 || message.TrackingId.ToString().Contains(MessageSearch, StringComparison.OrdinalIgnoreCase)
            || message.ChainRunId.ToString().Contains(MessageSearch, StringComparison.OrdinalIgnoreCase)
            || (message.Error?.Contains(MessageSearch, StringComparison.OrdinalIgnoreCase) ?? false)));
    public static readonly string[] MessageStates = ["Available", "Delayed", "In flight", "Awaiting input", "Retry pending", "Succeeded", "Partial", "Failed", "Expired", "Cancelled"];
    public int MessageCount(string state) => Messages.Count(message => MessageState(message, UpdatedAt ?? DateTimeOffset.UtcNow) == state);
    public double MessagePercent(string state) => Messages.Count == 0 ? 0 : MessageCount(state) * 100d / Messages.Count;
    public static string MessageState(JobChainSubmission message, DateTimeOffset now) => message.Status switch
    {
        "Queued" when message.ExpiresAt <= now => "Expired",
        "Queued" when message.AvailableAt > now => "Delayed",
        "Queued" => "Available",
        "Claimed" or "Running" or "Waiting" => "In flight",
        "AwaitingInput" => "Awaiting input",
        "RetryPending" => "Retry pending",
        _ => message.Status,
    };
    public string MessageLabel(JobChainSubmission message) => MessageState(message, UpdatedAt ?? DateTimeOffset.UtcNow);
    public static bool MatchesQueue(ChainQueueDefinition queue, string chain, string search, string intake) =>
        (intake.Length == 0 || queue.Enabled == (intake == "enabled"))
        && (queue.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || chain.Contains(search, StringComparison.OrdinalIgnoreCase)
            || queue.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
    public void SelectTab(string tab) => navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", tab));
    public void EditSelected() { if (Selected is not null) Edit(Selected); }
    public IReadOnlyList<ChainQueueDefinition> Queues { get; private set; } = [];
    public IReadOnlyList<JobChainView> Chains { get; private set; } = [];
    public IReadOnlyList<JobChainSubmission> Messages { get; private set; } = [];
    public ChainQueueDefinition? Selected { get; private set; }
    public bool Loading { get; private set; } = true;
    public bool Busy { get; private set; }
    public ChainQueueDefinition? Deleting { get; private set; }
    public string? DeleteError { get; private set; }
    public bool ShowEditor { get; private set; }
    public Guid? EditId { get; private set; }
    public string Name { get; set; } = "";
    public string ChainId { get; set; } = "";
    public int MessageTtlSeconds { get; set; } = 86400;
    public int ReceiptRetentionDays { get; set; } = 30;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 3;
    public int DeliveryDelaySeconds { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Error { get; private set; }
    public string? FormError { get; private set; }
    public string Endpoint => Selected is null ? "" : new Uri(new Uri(navigation.BaseUri), $"api/v1/queues/{Selected.Id}/messages").ToString();
    public string Example => "curl --request POST '" + Endpoint + "' \\\n  --header \"Authorization: Bearer $PC_TOKEN\" \\\n  --header 'Content-Type: application/json' \\\n  --header 'Idempotency-Key: order-123' \\\n  --data '{\"orderId\":\"123\",\"customer\":{\"name\":\"Ada\"},\"items\":[{\"sku\":\"A1\",\"quantity\":2}]}'";

    public async Task LoadAsync(Guid projectId, Guid? queueId = null, string? tab = null)
    {
        if (ProjectId != projectId || DetailQueueId != queueId) CloseInspect();
        if (ProjectId != projectId) { Selected = null; Messages = []; Search = ""; IntakeFilter = ""; }
        if (DetailQueueId != queueId) { MessageSearch = ""; MessageFilter = ""; }
        ProjectId = projectId;
        DetailQueueId = queueId;
        Tab = tab is "monitoring" or "configuration" or "integration" ? tab : "messages";
        await RefreshAsync();
        Loading = false;
        ui.Set("Queues", "protected JSON intake for workflows");
    }

    public async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true;
        Error = null;
        try
        {
            Queues = await queues.ListAsync(ProjectId);
            Chains = await service.ListJobChainsAsync(ProjectId);
            if (Selected is not null) Selected = Queues.FirstOrDefault(queue => queue.Id == Selected.Id);
            if (DetailQueueId is { } id)
            {
                Selected = Queues.FirstOrDefault(queue => queue.Id == id);
                if (Selected is null) Error = "This queue no longer exists or is not available in this project.";
            }
            Messages = Selected is null ? [] : await queues.ListMessagesAsync(Selected.Id);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Busy = false; NotifyStateChanged(); }
    }

    public async Task SelectAsync(ChainQueueDefinition queue)
    {
        if (Busy) return;
        Selected = queue;
        await RefreshAsync();
    }

    public string ChainName(Guid chainId) => Chains.FirstOrDefault(chain => chain.Id == chainId)?.Name ?? "Deleted workflow";
    public void Edit(ChainQueueDefinition? queue = null)
    {
        EditId = queue?.Id;
        Name = queue?.Name ?? "";
        ChainId = queue?.ChainId.ToString() ?? Chains.FirstOrDefault()?.Id.ToString() ?? "";
        Enabled = queue?.Enabled ?? true;
        MessageTtlSeconds = queue?.MessageTtlSeconds ?? 86400;
        ReceiptRetentionDays = queue?.ReceiptRetentionDays ?? 30;
        DeliveryDelaySeconds = queue?.DeliveryDelaySeconds ?? 0;
        MaxAttempts = queue?.MaxAttempts ?? 3;
        RetryDelaySeconds = queue?.RetryDelaySeconds ?? 3;
        FormError = null;
        ShowEditor = true;
    }
    public void CloseEditor() { if (!Busy) ShowEditor = false; }

    public async Task SaveAsync()
    {
        if (Busy) return;
        Busy = true;
        FormError = null;
        try
        {
            if (!Guid.TryParse(ChainId, out var chainId)) throw new ArgumentException("Choose a workflow.");
            Selected = await queues.SaveAsync(EditId, ProjectId, chainId, Name, Enabled, MessageTtlSeconds, ReceiptRetentionDays, MaxAttempts, RetryDelaySeconds, DeliveryDelaySeconds);
            ShowEditor = false;
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Busy = false; }
        if (!ShowEditor && Selected is { } saved)
        {
            await RefreshAsync();
            navigation.NavigateTo(QueueUrl(saved.Id) + "?tab=configuration");
        }
    }

    public async Task RetryAsync(JobChainSubmission message)
    {
        if (Busy || Selected is null) return;
        Busy = true;
        Error = null;
        try { await queues.RetryAsync(Selected.Id, message.TrackingId); }
        catch (Exception ex) { Error = ex.Message; return; }
        finally { Busy = false; }
        await RefreshAsync();
    }

    public void RequestDelete()
    {
        if (Busy) return;
        Deleting = Selected;
        DeleteError = null;
    }
    public void CancelDelete() { if (!Busy) Deleting = null; }
    public async Task DeleteAsync()
    {
        if (Deleting is not { } queue || Busy) return;
        Busy = true;
        DeleteError = null;
        try
        {
            await queues.DeleteAsync(queue.Id);
            Deleting = null;
            Selected = null;
            Messages = [];
        }
        catch (Exception ex) { DeleteError = ex.Message; }
        finally { Busy = false; }
        if (Deleting is null)
        {
            DetailQueueId = null;
            await RefreshAsync();
            navigation.NavigateTo(ListUrl);
        }
    }

    public async Task ToggleIntakeAsync()
    {
        if (Selected is not { } selected || Busy) return;
        Busy = true;
        try { Selected = await queues.SaveAsync(selected.Id, selected.ProjectId, selected.ChainId, selected.Name, !selected.Enabled, selected.MessageTtlSeconds, selected.ReceiptRetentionDays, selected.MaxAttempts, selected.RetryDelaySeconds, selected.DeliveryDelaySeconds); }
        catch (Exception ex) { Error = ex.Message; return; }
        finally { Busy = false; }
        await RefreshAsync();
    }
}

using System.Text.Json.Nodes;
using Markdig.Syntax.Inlines;
using Markdig.Syntax;
using Markdig;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application.Build;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel(IBuildChatStore store, IModelConnections connections, BuildChatAgent agent,
    PortalUiState ui, IJSRuntime js, PlaceContextService service, PlaceContext.Application.Access.IPermissionService permissions,
    OperationCenter? operations = null, ICurrentTenant? tenant = null, ICurrentUser? user = null,
    PlaceContext.Application.Jobs.IJobChainSubmissionQueue? submissions = null, NavigationManager? navigation = null,
    IWorkflowDemoShares? demoShares = null) : PageViewModel, IComponentViewModel
{
    private static readonly MarkdownPipeline ChatMarkdown = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().Build();
    public static string FormatReply(string text)
    {
        var document = Markdown.Parse(text, ChatMarkdown);
        foreach (var link in document.Descendants<LinkInline>())
        {
            // Disabling raw HTML does not prevent executable Markdown link URLs.
            if (link.Url is not { } url || url.Any(char.IsControl)
                || !Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)
                || (uri.IsAbsoluteUri && !url.StartsWith('/') && uri.Scheme is not ("http" or "https" or "mailto")))
            {
                link.Url = "";
            }
        }
        return Markdown.ToHtml(document, ChatMarkdown);
    }

    private bool? _workspaceVisible;
    public bool WorkspaceOpen => AgentOpen || _workspaceVisible == true || PreviewItem is not null || PreviewChart is not null;
    public bool WorkspaceVisible => _workspaceVisible ?? (Conversation.Messages.Count > 0 || WorkspaceOpen);
    public void OpenWorkspace() => _workspaceVisible = true;
    public void CloseWorkspace()
    {
        AgentOpen = false;
        ClosePreview();
        _workspaceVisible = false;
        NavigateToDashboard();
    }

    public string ViewId { get; } = "build-" + Guid.NewGuid().ToString("N");
    public BuildConversation Conversation { get; private set; } = new();
    public IReadOnlyList<BuildConversationSummary> Conversations { get; private set; } = [];
    public IReadOnlyList<ModelConnection> Connections { get; private set; } = [];
    public IEnumerable<string> Models => Connections.FirstOrDefault(x => x.Id == Conversation.ConnectionId)?.Models ?? [];
    public string? ModelSetupHint => Connections.FirstOrDefault(x => x.Id == Conversation.ConnectionId)?.SetupHint;
    public string Draft { get; set; } = "";
    public bool ApplyTaskHint(string prompt)
    {
        if (Locked || Listening || !string.IsNullOrWhiteSpace(Draft))
        {
            return false;
        }
        Draft = prompt;
        return true;
    }
    public string? Error
    {
        get; private set;
    }
    public string? Notice
    {
        get; private set;
    }
    public bool Busy => Conversation.Status is BuildRunStatus.Queued or BuildRunStatus.Running;
    public bool Submitting
    {
        get; private set;
    }
    public bool NavigationLocked => Loading || Submitting;
    public bool Loading
    {
        get; private set;
    }
    public bool Locked => Busy || NavigationLocked;
    public bool ShowHistory
    {
        get; set;
    }
    public bool ConfirmDelete
    {
        get; set;
    }
    public bool SpeechSupported
    {
        get; private set;
    }
    public bool Listening
    {
        get; private set;
    }
    public bool CanStartChat => !Locked && !Listening && Conversation.ConnectionId != Guid.Empty && Models.Contains(Conversation.Model);
    public bool CanSend => CanStartChat && Draft.Trim().Length > 0;
    public async Task StartWorkflowChatAsync()
    {
        await StartDashboardActionAsync("Help me run an existing workflow in this project. Show me the available workflows and use clickable choices to ask which one. Ask for any required inputs before running it. Do not create or modify workflows.");
    }
    private IDisposable? _conversationSubscription;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private int _updateVersion;
    private bool _refreshPending, _disposed;
    private Guid? _requestedConversationId;
    private Guid? _requestedRunId;
    private Task? _send;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BuildChatViewModel>? _reference;

    public async Task LoadAsync(Guid projectId, Guid? conversationId = null, Guid? runId = null, Guid? submissionId = null)
    {
        var runChanged = _requestedRunId != runId;
        _requestedRunId = runId;
        if (Conversation.ProjectId == projectId)
        {
            if (_requestedConversationId != conversationId && conversationId is { } id)
            {
                await OpenAsync(id);
            }
            _requestedConversationId = conversationId;
            if (runChanged)
            {
                ClosePreview();
                if (runId is { } selectedRun)
                {
                    await OpenPreviewAsync(new(selectedRun, "workflow-run", "Workflow run", TrackingId: submissionId));
                }
            }
            return;
        }
        _requestedConversationId = conversationId;
        StopUpdates();
        if (_send is not null)
        {
            await _send;
        }
        await StopSpeechAsync();
        Loading = true;
        Error = Notice = null;
        Draft = "";
        ResetWorkspace();
        Dashboard = new();
        AgentDraft = new();
        AgentPrompt = AgentTools = AgentMemory = NewActionTitle = NewActionPrompt = "";
        AgentWorkflows = [];
        CanShareDemo = false;
        Conversation = new()
        {
            ProjectId = projectId
        };
        try
        {
            ui.Set("Dashboard", "");
            var models = connections.ListAsync();
            var history = store.ListAsync(projectId);
            await Task.WhenAll(models, history);
            Connections = (await models).Where(x => x.Enabled).ToArray();
            Conversations = await history;
            Dashboard = await store.GetSettingsAsync(projectId);
            CanShareDemo = await permissions.HasAsync(PlaceContext.Application.Access.Models.Permission.ArtifactsShare);
            SelectDefaultModel();
            var active = Conversations.FirstOrDefault(x => x.Status is BuildRunStatus.Queued or BuildRunStatus.Running);
            async Task LoadConversationAsync()
            {
                if (conversationId is { } id)
                {
                    Conversation = await store.LoadAsync(projectId, id);
                    return;
                }
                if (active is not null)
                {
                    Conversation = await store.LoadAsync(projectId, active.Id);
                }
            }
            await LoadConversationAsync();
            if (runId is { } selectedRun)
            {
                await OpenPreviewAsync(new(selectedRun, "workflow-run", "Workflow run", TrackingId: submissionId));
            }
            if (runId is null)
            {
                await AutoPreviewAsync();
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
        }
        SubscribeToConversation();
        if (Busy)
        {
            await RefreshConversationAsync();
        }
    }
    private void SelectDefaultModel()
    {
        var connection = Connections.Where(x => x.Models.Count > 0)
            .OrderByDescending(x => x.IsLocalCli || (Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out var url) && url.IsLoopback))
            .FirstOrDefault();
        Conversation.ConnectionId = connection?.Id ?? Guid.Empty;
        Conversation.Model = connection?.Models.FirstOrDefault() ?? "";
    }
    public void SelectConnection(string? value)
    {
        if (Locked || !Guid.TryParse(value, out var id))
        {
            return;
        }
        Conversation.ConnectionId = id;
        Conversation.Model = Models.FirstOrDefault() ?? "";
    }
    public async Task NewAsync()
    {
        if (NavigationLocked)
        {
            return;
        }
        StopUpdates();
        await StopSpeechAsync();
        Conversation = new()
        {
            ProjectId = Conversation.ProjectId,
            ConnectionId = Conversation.ConnectionId,
            Model = Conversation.Model
        };
        Draft = "";
        Error = Notice = null;
        ConfirmDelete = false;
        ShowHistory = false;
        ResetWorkspace();
        NavigateToDashboard();
    }
    public async Task OpenAsync(Guid id)
    {
        if (NavigationLocked)
        {
            return;
        }
        StopUpdates();
        Loading = true;
        ShowHistory = false;
        Error = Notice = null;
        NotifyStateChanged();
        try
        {
            await StopSpeechAsync();
            Conversation = await store.LoadAsync(Conversation.ProjectId, id);
            ResetWorkspace();
            NavigateToDashboard();
            await AutoPreviewAsync();
            Draft = "";
            Error = Notice = null;
            ConfirmDelete = false;
            ShowHistory = false;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
        SubscribeToConversation();
        if (Busy)
        {
            await RefreshConversationAsync();
        }
    }
    public async Task DeleteAsync()
    {
        if (Locked)
        {
            return;
        }
        try
        {
            await store.DeleteAsync(Conversation.ProjectId, Conversation.Id);
            await NewAsync();
            Conversations = await store.ListAsync(Conversation.ProjectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
    public Task SendAsync()
    {
        if (!CanSend)
        {
            return Task.CompletedTask;
        }
        _send = SendCoreAsync();
        return _send;
    }
    public bool CanAnswer(JsonNode message) => !Locked && !Listening && Draft.Length == 0
        && Conversation.ConnectionId != Guid.Empty && Models.Contains(Conversation.Model)
        && ReferenceEquals(Conversation.Messages.LastOrDefault(), message);

    public Task AnswerAsync(JsonNode message, string answer)
    {
        if (!CanAnswer(message) || !SuggestedAnswers(message).Contains(answer))
        {
            return Task.CompletedTask;
        }
        Draft = answer;
        return SendAsync();
    }
    private async Task SendCoreAsync()
    {
        Submitting = true;
        Error = Notice = null;
        SubscribeToConversation();
        var text = Draft;
        if (PreviewItem is { Kind: "workflow-run", WorkflowId: { } workflowId } selected && PreviewError is null)
        {
            text += $"\n\nSelected workflow run: {selected.Id} (workflow {workflowId}).";
        }
        try
        {
            await agent.QueueAsync(Conversation, text);
            Draft = "";
            Notice = "Working in the background. You can leave this page and return later.";
            Conversations = await store.ListAsync(Conversation.ProjectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Submitting = false;
            NotifyStateChanged();
        }
        if (_refreshPending)
        {
            await RefreshConversationAsync();
        }
    }
    public async Task StopAsync()
    {
        try
        {
            await store.RequestStopAsync(Conversation.ProjectId, Conversation.Id);
            Notice = "Stop requested. Completed actions remain applied.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
    public async Task ToggleHistoryAsync()
    {
        ShowHistory = !ShowHistory;
        if (!ShowHistory)
        {
            return;
        }
        try
        {
            Conversations = await store.ListAsync(Conversation.ProjectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
    private void SubscribeToConversation()
    {
        StopUpdates();
        if (operations is null || tenant is not { IsResolved: true } || user is null)
        {
            return;
        }        // shortcut: progress notifications are replica-local; use shared pub/sub before separating workers from UI hosts.
        _conversationSubscription = operations.Subscribe(tenant.TenantId,
            () => _ = RefreshConversationAsync(), user.UserId, Conversation.ProjectId, $"build:{Conversation.Id}");
    }
    private void StopUpdates()
    {
        ++_updateVersion;
        _conversationSubscription?.Dispose();
        _conversationSubscription = null;
        _refreshPending = false;
    }
    private async Task RefreshConversationAsync()
    {
        var version = _updateVersion;
        await _updateGate.WaitAsync();
        try
        {
            if (_disposed || version != _updateVersion)
            {
                return;
            }
            if (Submitting || Loading)
            {
                _refreshPending = true;
                return;
            }
            _refreshPending = false;
            var current = Conversation;
            var latest = await store.LoadAsync(current.ProjectId, current.Id);
            if (_disposed || version != _updateVersion || Conversation != current)
            {
                return;
            }
            if (latest.Status == current.Status && latest.Error == current.Error
                            && latest.StopRequested == current.StopRequested && JsonNode.DeepEquals(latest.Messages, current.Messages))
            {
                return;
            }
            Conversation = latest;
            await AutoPreviewAsync();
            if (_disposed || version != _updateVersion)
            {
                return;
            }
            if (!Busy)
            {
                Notice = latest.Status == BuildRunStatus.Completed ? "Background run completed." : null;
                Error = latest.Error;
            }
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            if (!_disposed && version == _updateVersion)
            {
                Error = ex.Message;
                NotifyStateChanged();
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }
    public static string Role(JsonNode message) => message["role"]?.GetValue<string>() ?? "assistant";
    public static string Text(JsonNode message) => message["content"] is JsonArray parts
        ? string.Join("\n", parts.Where(x => x?["type"]?.GetValue<string>() == "text").Select(x => x!["text"]!.GetValue<string>()))
        : message["content"]?.GetValue<string>() ?? "";
    public static string? JsonReply(JsonNode message)
    {
        if (Role(message) != "assistant")
        {
            return null;
        }
        var text = Text(message).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal))
        {
            var line = text.IndexOf('\n');
            if (line < 0 || line > text.Length - 3)
            {
                return null;
            }
            var fence = text[..line].Trim();
            if (fence != "```" && !fence.Equals("```json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            text = text[(line + 1)..^3].Trim();
        }
        if (!text.StartsWith('{') && !text.StartsWith('['))
        {
            return null;
        }
        try
        {
            var json = JsonNode.Parse(text);
            return json?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
    public static IEnumerable<string> ImageUrls(JsonNode message) => (message["content"] as JsonArray ?? [])
        .Where(x => x?["type"]?.GetValue<string>() == "image_url").Select(x => x!["image_url"]!["url"]!.GetValue<string>());
    public static IEnumerable<JsonNode> Calls(JsonNode message) => (message["tool_calls"] as JsonArray ?? []).OfType<JsonNode>();
    public static IEnumerable<string> SuggestedAnswers(JsonNode message) => Role(message) == "assistant"
        ? (message["suggested_answers"] as JsonArray ?? []).OfType<JsonValue>()
            .Select(x => x.TryGetValue<string>(out var value) ? value : "").Where(x => !string.IsNullOrWhiteSpace(x))
        : [];

    public async Task InitializeBrowserAsync()
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./build-chat.js");
            _reference ??= DotNetObjectReference.Create(this);
            SpeechSupported = await _module.InvokeAsync<bool>("supportsSpeech");
            await _module.InvokeVoidAsync("mountChat", ViewId);
            NotifyStateChanged();
        }
        catch (JSException)
        {
            SpeechSupported = false;
        }
    }
    public async Task ToggleSpeechAsync()
    {
        if (Busy || _module is null)
        {
            return;
        }
        try
        {
            async Task ToggleSpeechAsync()
            {
                if (Listening)
                {
                    await StopSpeechAsync();
                    return;
                }
                Listening = true;
                Error = null;
                await _module.InvokeVoidAsync("startSpeech", _reference, ViewId);
            }
            await ToggleSpeechAsync();
        }
        catch (JSException)
        {
            Listening = false;
            Error = "Could not start dictation. Check microphone permission and browser speech support.";
        }
    }
    private async Task StopSpeechAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("stopSpeech", ViewId);
        }
        Listening = false;
    }
    [JSInvokable]
    public Task SpeechText(string text)
    {
        if (Listening && !Busy)
        {
            var transcript = (Draft + " " + text).Trim();
            Draft = transcript[..Math.Min(16000, transcript.Length)];
        }
        NotifyStateChanged();
        return Task.CompletedTask;
    }
    [JSInvokable]
    public Task SpeechEnded(string? error)
    {
        Listening = false;
        if (error is not null)
        {
            Error = error;
        }
        NotifyStateChanged();
        return Task.CompletedTask;
    }
    public async ValueTask DisposePageAsync()
    {
        _disposed = true;
        ClosePreview();
        Detach();
        StopUpdates();
        if (_send is not null)
        {
            await _send;
        }
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("unmountChat", ViewId);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        _module = null;
        _reference?.Dispose();
        _reference = null;
        Listening = false;
        Conversation = new();
    }
}

using System.Text.Json.Nodes;
using Markdig.Syntax.Inlines;
using Markdig.Syntax;
using Markdig;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PlaceContext.Application.Access;
using PlaceContext.Application.Build;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Operations;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application;
using PlaceContext.Host.Components.Models;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel(IBuildChatStore store, IModelConnections connections, BuildChatAgent agent,
    PortalUiState ui, IJSRuntime js, PlaceContextService service, PlaceContext.Application.Access.IPermissionService permissions, DashboardViewModel? dashboard = null,
    OperationCenter? operations = null, ICurrentTenant? tenant = null, ICurrentUser? user = null) : PageViewModel, IComponentViewModel
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

    public DashboardViewModel? Dashboard => dashboard;
    public bool ShowOverview
    {
        get; private set;
    }
    private Task? _dashboardLoad;
    private bool? _workspaceVisible;
    public bool WorkspaceOpen => _workspaceVisible == true || PreviewItem is not null || ShowOverview || PreviewChart is not null;
    public bool WorkspaceVisible => _workspaceVisible ?? (Conversation.Messages.Count > 0 || WorkspaceOpen);
    public void OpenWorkspace() => _workspaceVisible = true;
    public DashboardSection OverviewSection { get; private set; } = DashboardSection.Runs;
    public void OpenDashboardSection(DashboardSection section)
    {
        OpenOverview();
        OverviewSection = section;
    }

    public void OpenOverview(string filter = "all")
    {
        ClosePreview();
        OpenWorkspace();
        ShowOverview = true;
        OverviewSection = DashboardSection.Runs;
        dashboard?.SetFilter(filter);
    }
    public void CloseWorkspace()
    {
        ClosePreview();
        ShowOverview = false;
        _workspaceVisible = false;
    }

    public string ViewId { get; } = "build-" + Guid.NewGuid().ToString("N");
    public BuildConversation Conversation { get; private set; } = new();
    public IReadOnlyList<BuildConversationSummary> Conversations { get; private set; } = [];
    public IReadOnlyList<ModelConnection> Connections { get; private set; } = [];
    public IEnumerable<string> Models => Connections.FirstOrDefault(x => x.Id == Conversation.ConnectionId)?.Models ?? [];
    public string? ModelSetupHint => Connections.FirstOrDefault(x => x.Id == Conversation.ConnectionId)?.SetupHint;
    public List<BuildImage> Images { get; } = [];
    public string Draft { get; set; } = "";
    public static IReadOnlyList<(string Title, string Description, string Icon, string Prompt)> QuickPicks
    {
        get;
    } =
    [
        ("Chart project activity", "See job activity, outcomes, and trends.", "data.graph",
            "Show a chart of this project’s current running and queued jobs and its succeeded and failed runs from the last 24 hours. Use project_activity and show_chart, label the time ranges clearly, and do not run or change any jobs."),
        ("Compare grocery prices", "Compare Coles and Woolworths for your postcode.", "vm",
            "Build a grocery price comparison workflow for Coles and Woolworths in Australia. Ask me for the product name and confirm the postcode (default 4101). Inspect existing jobs and workflows first and reuse what fits. Use QEMU Live preview frames and AI verification to verify prices, pack sizes and availability. Compare matching products by unit price, link the evidence, and mark any unverified price clearly."),
        ("Check a website", "Give a browser a task and verify what happened.", "check",
            "Help me build a QEMU browser workflow to check a real website. Ask for the starting URL, the task and the expected result. Inspect existing workflows first. Use Live preview to guide the browser and AI verification to verify the outcome, with replay evidence and a clear pass or fail result."),
        ("Create a report", "Turn your data into a summary and useful charts.", "data.graph",
            "Help me build a report with a concise summary and charts. First inspect the project's existing jobs and data sources, then ask which data and questions the report should cover. Reuse existing work where possible, use real data, and make missing data clear. Ask whether this should run once or on a schedule."),
        ("Connect data with Jev", "Choose a source and build its processing and retrieval pipeline.", "data",
            "Help me connect data and choose a pipeline. Identify my source, inspect supported connectors and existing sources, and guide me through secure setup. Include databases, lakes, warehouses or inbound email as appropriate. Ask what data I have and what outcome I need."),
        ("Add a review step", "Check a result with AI review or ask a person to approve it.", "agent",
            "Help me add a review step to a workflow. Inspect the existing workflows and ask which one to use, what needs checking, and whether AI review or a person should review it. Define the approval criteria and an explicit path for approved, rejected or uncertain results. Preserve the other workflow steps."),
        ("Onboard a client", "Collect information and check what is still missing.", "human",
            "Help me build an end-to-end client onboarding workflow for my industry. Ask which information and documents are required, inspect existing workflows and data connections, and collect the inputs. Use AI review to check completeness against explicit criteria. Route low-confidence or missing evidence to a durable Human in the loop approval step, and only proceed through an explicitly approved workflow branch. Ask whether to test once or activate a schedule."),
        ("Fix a failed workflow", "Trace a failed run and find the smallest fix.", "logs",
            "Help me diagnose a failed workflow. Inspect the available workflows and ask which run failed if it is unclear. Read its execution results and relevant definitions, explain the cause using the evidence, and make the smallest fix. Preserve unrelated steps and verify the change.")
    ];
    public bool CanUseQuickPick => !Locked && !Listening && string.IsNullOrWhiteSpace(Draft);
    public bool UseQuickPick(string prompt)
    {
        if (!CanUseQuickPick)
        {
            return false;
        }
        Draft = prompt;
        return true;
    }
    public string Memory { get; set; } = "";
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
    public bool NavigationLocked => Loading || Uploading || Submitting;
    public bool Loading
    {
        get; private set;
    }
    public bool Uploading
    {
        get; private set;
    }
    public bool Locked => Busy || NavigationLocked;
    public bool ShowMemory
    {
        get; set;
    }
    public bool ShowHistory
    {
        get; set;
    }
    public bool ShowMcpMenu
    {
        get; set;
    }
    public bool ShowAddMcp
    {
        get; private set;
    }
    private bool _reopenMcpMenu;
    public bool LoadingMcps
    {
        get; private set;
    }
    public string? McpError
    {
        get; private set;
    }
    public IReadOnlyList<McpConnectionView> AvailableMcps { get; private set; } = [];

    public async Task<bool> SelectMcpAsync(Guid id)
    {
        if (Locked || Listening || AvailableMcps.FirstOrDefault(server => server.Id == id && server.Enabled
            && server.ProjectId == Conversation.ProjectId) is not { } server)
        {
            return false;
        }
        var mention = BuildMcpTools.Mention(server.Name);
        if (_mentionStart >= 0 && _mentionDraft != Draft)
        {
            return false;
        }
        if (_mentionStart >= 0 && _mentionDraft == Draft)
        {
            var suffix = Draft[_mentionEnd..];
            var insertion = mention + (suffix.Length == 0 || !char.IsWhiteSpace(suffix[0]) ? " " : "");
            var draft = Draft[.._mentionStart] + insertion + suffix;
            if (draft.Length > 16000)
            {
                Error = "Shorten your message before adding an MCP.";
                return false;
            }
            Draft = draft;
            _composerCaret = _mentionStart + insertion.Length;
        }
        if (_mentionStart < 0 && !BuildMcpTools.MentionNames(Draft).Contains(server.Name, StringComparer.OrdinalIgnoreCase))
        {
            var draft = mention + " " + Draft;
            if (draft.Length > 16000)
            {
                Error = "Shorten your message before adding an MCP.";
                return false;
            }
            Draft = draft;
        }
        _composerCaret ??= Draft.Length;
        ResetMcpMention();
        ShowMcpMenu = false;
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setComposerMenu", ViewId, false);
        }
        return true;
    }

    public Task OpenMcpMenuAsync()
    {
        ResetMcpMention();
        return LoadMcpMenuAsync(true);
    }

    private async Task LoadMcpMenuAsync(bool addWhenEmpty)
    {
        ShowMcpMenu = true;
        if (LoadingMcps)
        {
            return;
        }
        var projectId = Conversation.ProjectId;
        LoadingMcps = true;
        McpError = null;
        AvailableMcps = [];
        try
        {
            var servers = await service.ListMcpConnectionsAsync(projectId);
            if (Conversation.ProjectId == projectId)
            {
                AvailableMcps = servers.Where(x => x.ProjectId == projectId && x.Enabled)
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                if (addWhenEmpty && !servers.Any(x => x.ProjectId == projectId) && ShowMcpMenu)
                {
                    await OpenAddMcpAsync();
                }
            }
        }
        catch (Exception)
        {
            if (Conversation.ProjectId == projectId)
            {
                McpError = "Could not load MCP servers. Please try again.";
            }
        }
        finally
        {
            if (Conversation.ProjectId == projectId)
            {
                LoadingMcps = false;
            }
        }
    }
    public async Task OpenAddMcpAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setComposerMenu", ViewId, false);
        }
        ShowAddMcp = true;
    }
    public void CloseAddMcp()
    {
        ShowAddMcp = false;
        ShowMcpMenu = true;
        _reopenMcpMenu = true;
    }
    public void McpAdded(McpConnectionView connection)
    {
        if (connection.ProjectId != Conversation.ProjectId)
        {
            return;
        }
        AvailableMcps = AvailableMcps.Where(x => x.Id != connection.Id).Append(connection)
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        CloseAddMcp();
    }
    public async Task AfterRenderAsync()
    {
        if (!_reopenMcpMenu || _module is null)
        {
            return;
        }
        _reopenMcpMenu = false;
        await _module.InvokeVoidAsync("setComposerMenu", ViewId, true);
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
    public bool CanSend => !Locked && !Listening && Conversation.ConnectionId != Guid.Empty
        && Models.Contains(Conversation.Model) && (Draft.Trim().Length > 0 || Images.Count > 0);
    private IDisposable? _conversationSubscription;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private int _updateVersion;
    private bool _refreshPending, _disposed;
    private Guid? _requestedConversationId;
    private Task? _send;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BuildChatViewModel>? _reference;

    public async Task LoadAsync(Guid projectId, Guid? conversationId = null)
    {
        if (Conversation.ProjectId == projectId)
        {
            if (_requestedConversationId != conversationId && conversationId is { } id)
            {
                await OpenAsync(id);
            }
            _requestedConversationId = conversationId;
            return;
        }
        _requestedConversationId = conversationId;
        StopUpdates();
        if (_dashboardLoad is not null)
        {
            await _dashboardLoad;
        }
        dashboard?.Dispose();
        if (_send is not null)
        {
            await _send;
        }
        await StopSpeechAsync();
        Loading = true;
        Error = Notice = null;
        Draft = "";
        Images.Clear();
        ShowMcpMenu = ShowAddMcp = LoadingMcps = _reopenMcpMenu = false;
        ResetMcpMention();
        McpError = null;
        AvailableMcps = [];
        ResetWorkspace();
        Conversation = new()
        {
            ProjectId = projectId
        };
        try
        {
            ui.Set("Dashboard", "");
            Connections = (await connections.ListAsync()).Where(x => x.Enabled).ToArray();
            Conversations = await store.ListAsync(projectId);
            Memory = await store.GetMemoryAsync(projectId);
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
            await AutoPreviewAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
        }
        if (dashboard is not null)
        {
            dashboard.Attach(() =>
            {
                NotifyStateChanged();
                return Task.CompletedTask;
            });
            dashboard.Initialize(projectId);
            _dashboardLoad = dashboard.LoadAsync();
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
        Images.Clear();
        Error = Notice = null;
        ConfirmDelete = false;
        ShowHistory = ShowMcpMenu = false;
        ResetWorkspace();
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
            await AutoPreviewAsync();
            Draft = "";
            Images.Clear();
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
    public async Task SaveMemoryAsync()
    {
        if (Locked)
        {
            return;
        }
        try
        {
            await store.SaveMemoryAsync(Conversation.ProjectId, Memory);
            Notice = "Memory saved for your chats in this project.";
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
    public async Task UploadAsync(InputFileChangeEventArgs args)
    {
        if (Locked)
        {
            return;
        }
        Uploading = true;
        var projectId = Conversation.ProjectId;
        var conversationId = Conversation.Id;
        try
        {
            var files = args.GetMultipleFiles(4);
            if (Images.Count + files.Count > 4)
            {
                throw new ArgumentException("Attach up to four images.");
            }
            var additions = new List<BuildImage>();
            foreach (var file in files)
            {
                await using var input = file.OpenReadStream(2_000_000);
                using var buffer = new MemoryStream();
                await input.CopyToAsync(buffer);
                var image = new BuildImage(file.Name, file.ContentType, buffer.ToArray());
                image.Validate();
                additions.Add(image);
            }
            if (Conversation.ProjectId == projectId && Conversation.Id == conversationId)
            {
                Images.AddRange(additions);
                Error = null;
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Uploading = false;
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
    public bool CanAnswer(JsonNode message) => !Locked && !Listening && Draft.Length == 0 && Images.Count == 0
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
        var images = Images.ToArray();
        try
        {
            await agent.QueueAsync(Conversation, text, images);
            Draft = "";
            Images.Clear();
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
                var memory = await store.GetMemoryAsync(current.ProjectId);
                if (_disposed || version != _updateVersion)
                {
                    return;
                }
                Memory = memory;
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
            await _module.InvokeVoidAsync("mountChat", ViewId, _reference);
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
        Detach();
        StopUpdates();
        if (_dashboardLoad is not null)
        {
            await _dashboardLoad;
        }
        dashboard?.Dispose();
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

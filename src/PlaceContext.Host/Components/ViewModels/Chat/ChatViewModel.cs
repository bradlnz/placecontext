using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Agents;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel : PageViewModel
{
    private readonly IChatGateway _gateway;
    private readonly IProjectChatGateway _projectChat;
    private readonly CommandAgentOrchestrator _orchestrator;
    private readonly IPlaceContextService _svc;
    private readonly IMcpClientService _mcpClient;
    private readonly PortalUiState _ui;
    private readonly IChatMemoryStore _memoryStore;
    private readonly AgentContextBuilder _contextBuilder;
    private readonly IDocumentTextExtractor _docExtractor;
    private readonly IObjectStore _objectStore;
    private readonly IRunArtifactLinkRepository _links;
    private readonly ICurrentTenant _tenant;
    private readonly IContentIndexer _contentIndexer;
    private readonly IJSRuntime _js;
    private bool _dropZoneInitialized;
    public bool ShowCommandSuggestions { get; private set; }
    public IReadOnlyList<ChatCommandView> FilteredCommands { get; private set; } =
        Array.Empty<ChatCommandView>();
    public int SelectedCommandIndex { get; private set; }
    public int FileInputVersion { get; private set; }

    public ChatViewModel(
        IChatGateway gateway,
        IProjectChatGateway projectChat,
        CommandAgentOrchestrator orchestrator,
        IPlaceContextService svc,
        IMcpClientService mcpClient,
        PortalUiState ui,
        IChatMemoryStore memoryStore,
        AgentContextBuilder contextBuilder,
        IDocumentTextExtractor docExtractor,
        IObjectStore objectStore,
        IRunArtifactLinkRepository links,
        ICurrentTenant tenant,
        IContentIndexer contentIndexer,
        IJSRuntime js
    )
    {
        _gateway = gateway;
        _projectChat = projectChat;
        _orchestrator = orchestrator;
        _svc = svc;
        _mcpClient = mcpClient;
        _ui = ui;
        _memoryStore = memoryStore;
        _contextBuilder = contextBuilder;
        _docExtractor = docExtractor;
        _objectStore = objectStore;
        _links = links;
        _tenant = tenant;
        _contentIndexer = contentIndexer;
        _js = js;
        _ui.OnChanged += HandleUiChanged;
    }

    // ── Public state (bound by the razor component) ──────────────────────────

    public readonly List<AgentMessage> Messages = new();
    public string Input { get; set; } = "";
    public bool Streaming { get; private set; }
    public string StreamBuffer { get; private set; } = "";
    public Guid? ProjectId { get; private set; }
    public string ProjectName { get; private set; } = "";
    public ToolCallInfo? CurrentToolCall { get; private set; }
    public bool ShowSidePanel { get; set; } = true;
    public bool ShowSettings { get; set; }
    public string SettingsTab { get; set; } = "prompt";
    public ChatSettingsTab CurrentSettingsTab =>
        ChatPresentationCatalog.ParseSettingsTab(SettingsTab);

    public bool IsAssistant(AgentMessage message) => message.RoleKind == ChatRole.Assistant;

    public bool IsUser(AgentMessage message) => message.RoleKind == ChatRole.User;

    public bool IsVisible(AgentMessage message) => message.RoleKind != ChatRole.System;

    public IReadOnlyList<ToolCallInfo> GraphCalls(AgentMessage message) =>
        message
            .ToolCalls.Where(call =>
                call.ResultKind == ChatResultKind.Graph
                && call.Status == AgentToolCallStatus.Completed
            )
            .ToList();

    public IReadOnlyList<ToolCallInfo> MapCalls(AgentMessage message) =>
        message
            .ToolCalls.Where(call =>
                call.ResultKind == ChatResultKind.Map
                && call.Status == AgentToolCallStatus.Completed
            )
            .ToList();

    public IReadOnlyList<ToolCallInfo> ArtifactCalls(AgentMessage message) =>
        message
            .ToolCalls.Where(call =>
                call.ResultKind == ChatResultKind.Artifact
                && call.Status == AgentToolCallStatus.Completed
            )
            .ToList();

    public IReadOnlyList<ToolCallInfo> OtherCalls(AgentMessage message) =>
        message
            .ToolCalls.Where(call =>
                call.ResultKind
                    is not (ChatResultKind.Graph or ChatResultKind.Map or ChatResultKind.Artifact)
                || call.Status != AgentToolCallStatus.Completed
            )
            .ToList();

    public int CompletedOtherCallCount(AgentMessage message) =>
        OtherCalls(message).Count(call => call.Status == AgentToolCallStatus.Completed);

    public int ErrorOtherCallCount(AgentMessage message) =>
        OtherCalls(message).Count(call => call.Status == AgentToolCallStatus.Error);

    public IReadOnlyList<string> NumberedOptions(AgentMessage message) =>
        ParseNumberedOptions(message.Content);

    public string StreamThinking => ChatViewModel.SplitThinking(StreamBuffer).Thinking;
    public string StreamAnswer => ChatViewModel.SplitThinking(StreamBuffer).Answer;
    public IReadOnlyList<McpConnectionView> McpConnections { get; private set; } =
        Array.Empty<McpConnectionView>();
    public bool ShowAddMcp { get; set; }
    public bool ShowAuthFields { get; set; }
    public string NewMcpName { get; set; } = "";
    public string NewMcpTransport { get; set; } = McpTransport.Http;
    public string NewMcpEndpoint { get; set; } = "";
    public string NewMcpCommand { get; set; } = "";
    public string NewMcpArgs { get; set; } = "";
    public string NewMcpAuthType { get; set; } = McpAuthType.None;
    public string NewMcpAuthToken { get; set; } = "";
    public string NewMcpAuthHeader { get; set; } = "";
    public string NewMcpOAuthScopes { get; set; } = "";
    public string PendingSystemPrompt { get; set; } = "";
    public string PendingPreamble { get; set; } = "";
    public string PendingToolCatalog { get; set; } = "";
    public string PendingLaunchpadToolCatalog { get; set; } = "";
    public float PendingTemperature { get; set; }
    public int PendingMaxTokens { get; set; }
    public bool PendingRagEnabled { get; set; } = true;
    public int PendingMaxContextChunks { get; set; } = 5;
    public bool ShowSessionList { get; set; } = true;
    public bool ShowCreateChannel { get; private set; }
    public bool CreatingChannel { get; private set; }
    public string NewChannelName { get; set; } = "";
    public string? ChannelError { get; private set; }
    public IReadOnlyList<ChatSessionSummary> Sessions { get; private set; } =
        Array.Empty<ChatSessionSummary>();
    public IReadOnlyList<AgentDefinitionView> TeamAgents { get; private set; } =
        Array.Empty<AgentDefinitionView>();
    public IReadOnlyList<RunReportView> TeamGoals { get; private set; } =
        Array.Empty<RunReportView>();
    public readonly List<AgentAction> ActiveActions = new();
    public readonly List<FetchedData> FetchedData = new();
    public readonly List<ToolHistoryEntry> ToolHistory = new();
    public readonly List<GraphNodeView> GraphNodes = new();
    public int GraphLinks;
    public readonly List<ArtifactFileView> PanelArtifacts = new();
    public ClarificationRequest? PendingClarification { get; private set; }
    public readonly HashSet<string> ClarificationSelected = new();
    public readonly HashSet<int> RenderedChartIds = new();
    public byte[]? AttachedFile { get; private set; }
    public string? AttachedFileName { get; private set; }
    public string? AttachedFileText { get; private set; }
    public string? AttachmentError { get; private set; }

    // ── Private state ────────────────────────────────────────────────────────

    private string _systemPrompt = ChatCopy.DefaultSystemPrompt;
    private string _baseModel = "";
    private string _preamble = ChatCopy.DefaultPreamble;
    private string _toolCatalog = ChatCopy.DefaultToolCatalog;
    private string _launchpadToolCatalog = ChatCopy.DefaultLaunchpadToolCatalog;
    private bool _ragEnabled = true;
    private bool _agentEnabled = true;
    private int _maxContextChunks = 5;
    private float _temperature = 0.7f;
    private int _maxTokens = 2048;
    private int _toolCallCounter;
    private string _sessionTitle = ChatCopy.DefaultSessionTitle;
    private Guid? _sessionId;
    public Guid? SessionId => _sessionId;
    private TaskCompletionSource<ClarificationResult>? _clarificationTcs;
    private bool _attachmentsBucketEnsured;
    private CancellationTokenSource? _sendCts;
    private ProjectChatStatus _chatStatus = new(ProjectChatBackend.None, false, "No model configured");
    private CommandAgentRoute? _activeRoute;

    public string ChannelName => ChannelSlug(_sessionTitle);
    public int EnabledTeamMemberCount => TeamAgents.Count(agent => agent.Enabled);

    public string TeamMemberInitial(AgentDefinitionView agent) =>
        string.IsNullOrWhiteSpace(agent.Name) ? "A" : agent.Name.Trim()[..1].ToUpperInvariant();

    public string GoalText(RunReportView goal) =>
        string.IsNullOrWhiteSpace(goal.Run.Snapshot.Goal)
            ? goal.JobName
            : goal.Run.Snapshot.Goal;

    public string GoalStatus(RunReportView goal) =>
        AgentsViewModel.WorkStatusLabel(goal.Run.Status);

    public string GoalStatusClass(RunReportView goal) =>
        AgentsViewModel.WorkBucketClass(goal.Run.Status);

    public string ActiveAgentName => _activeRoute is { CollaboratingAgents.Count: > 0 } route
        ? string.Join(" + ", route.CollaboratingAgents.Select(agent => agent.Name))
        : "Command Agent";

    private static string ChannelSlug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title == ChatCopy.DefaultSessionTitle)
            return "new-channel";

        var slug = System.Text.RegularExpressions.Regex.Replace(
            title.Trim().ToLowerInvariant(),
            @"[^a-z0-9]+",
            "-"
        ).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "channel" : slug;
    }

    /// <summary>Cancels the currently running chat send loop.</summary>
    public async Task StopAsync()
    {
        try
        {
            var cts = _sendCts;
            if (cts is not null && !cts.IsCancellationRequested)
                cts.Cancel();
        }
        catch (ObjectDisposedException) { }
        await Task.CompletedTask;
    }

    private const string AttachmentsBucket = ChatCopy.AttachmentsBucket;
    private const int MaxArtifactInlineBytes = 512 * 1024;
    private const int MaxArtifactDocumentBytes = 10 * 1024 * 1024;
    private const int MaxArtifactExtractedTextLength = 200_000;

    // ── Gateway status ───────────────────────────────────────────────────────

    public string GatewayStatusText => _agentEnabled ? _chatStatus.Label : "Agent chat disabled";

    public bool GatewayIsCluster => _gateway is ClusterChatGateway;

    public bool GatewayReady => _agentEnabled && _chatStatus.IsEnabled;

    public string ChatInputPlaceholder => !_agentEnabled
        ? "Agent chat is disabled in Settings → Agents"
        : !_chatStatus.IsEnabled
            ? "Configure a model before starting a channel"
            : PendingClarification != null
                ? "Respond to the question above…"
                : "Message the agent team…";

    /// <summary>True once sessions, MCP, artifacts, and agent config have finished loading.</summary>
    public bool WorkspaceLoaded { get; private set; }

    public IChatGateway Gateway => _gateway;

    private async void HandleUiChanged()
    {
        _dropZoneInitialized = false;
        OnProjectChanged();
        NotifyStateChanged();
        await Task.CompletedTask;
    }

    public void DetachUi() => _ui.OnChanged -= HandleUiChanged;

    public async Task AfterRenderAsync(
        ElementReference messages,
        ElementReference scrollAnchor,
        ElementReference dropZone,
        bool firstRender
    )
    {
        if (WorkspaceLoaded && !_dropZoneInitialized)
        {
            try
            {
                _dropZoneInitialized = await _js.InvokeAsync<bool>(
                    "placecontext.setupDropZone",
                    dropZone,
                    "chat-file-input"
                );
            }
            catch (JSException) { }
        }
        if (firstRender)
        {
            try
            {
                if (await _js.InvokeAsync<bool>("placecontext.isMobile"))
                    ShowSidePanel = false;
            }
            catch { }
        }
        foreach (var msg in Messages)
        {
            foreach (
                var tc in msg.ToolCalls.Where(t =>
                    t.ResultType == "graph" && t.Status == AgentToolCallStatus.Completed
                )
            )
            {
                if (RenderedChartIds.Contains(tc.Id))
                    continue;
                try
                {
                    await _js.InvokeVoidAsync("pcchart.render", $"graph-{tc.Id}", tc.Result);
                    RenderedChartIds.Add(tc.Id);
                }
                catch { }
            }
            foreach (
                var tc in msg.ToolCalls.Where(t =>
                    t.ResultType == "map" && t.Status == AgentToolCallStatus.Completed
                )
            )
            {
                try
                {
                    await _js.InvokeVoidAsync("pcmap.render", $"map-{tc.Id}", tc.Result);
                }
                catch { }
            }
        }
    }

    public Task SubmitOptionsAsync(
        int messageId,
        ElementReference messages,
        ElementReference scrollAnchor
    ) => SubmitOptionsFromBrowserAsync(messageId, messages, scrollAnchor);

    private async Task SubmitOptionsFromBrowserAsync(
        int messageId,
        ElementReference messages,
        ElementReference scrollAnchor
    )
    {
        var selected = await _js.InvokeAsync<string[]>("placecontext.getCheckedOptions", messageId);
        if (selected is null || selected.Length == 0)
            return;
        Input = $"I selected: {string.Join(", ", selected)}";
        await SendAsync(
            () => ScrollToBottomAsync(messages),
            () => ScrollAfterRenderAsync(scrollAnchor)
        );
    }

    public Task CopyMessageAsync(string content) =>
        _js.InvokeVoidAsync("navigator.clipboard.writeText", content).AsTask();

    public Task OpenFilePickerAsync() =>
        _js.InvokeVoidAsync("eval", "document.getElementById('chat-file-input')?.click()").AsTask();

    public Task ScrollToBottomAsync(ElementReference messages) =>
        _js.InvokeVoidAsync("placecontext.scrollToBottom", messages).AsTask();

    public Task ScrollAfterRenderAsync(ElementReference anchor) =>
        _js.InvokeVoidAsync("placecontext.scrollToElement", anchor).AsTask();

    public async Task HandleInputKeydownAsync(
        KeyboardEventArgs e,
        ElementReference messages,
        ElementReference scrollAnchor
    )
    {
        if (ShowCommandSuggestions)
        {
            if (e.Key == "ArrowDown")
            {
                SelectedCommandIndex = Math.Min(
                    SelectedCommandIndex + 1,
                    FilteredCommands.Count - 1
                );
                return;
            }
            if (e.Key == "ArrowUp")
            {
                SelectedCommandIndex = Math.Max(SelectedCommandIndex - 1, 0);
                return;
            }
            if (e.Key is "Enter" or "Tab")
            {
                if (FilteredCommands.Count > 0 && SelectedCommandIndex < FilteredCommands.Count)
                {
                    Input = "/" + FilteredCommands[SelectedCommandIndex].Name + " ";
                    ShowCommandSuggestions = false;
                    if (e.Key == "Enter")
                        await SendAsync(
                            () => ScrollToBottomAsync(messages),
                            () => ScrollAfterRenderAsync(scrollAnchor)
                        );
                }
                return;
            }
            if (e.Key == "Escape")
            {
                ShowCommandSuggestions = false;
                return;
            }
        }
        if (e.Key == "Enter" && !e.ShiftKey)
            await SendAsync(
                () => ScrollToBottomAsync(messages),
                () => ScrollAfterRenderAsync(scrollAnchor)
            );
    }

    public void HandleInputChanged()
    {
        if (Input.StartsWith('/') && Commands.Count > 0)
        {
            var partial = Input[1..].ToLowerInvariant();
            FilteredCommands = FilterCommands(Commands, partial);
            ShowCommandSuggestions = FilteredCommands.Count > 0;
            SelectedCommandIndex = 0;
        }
        else
        {
            FilteredCommands = Array.Empty<ChatCommandView>();
            ShowCommandSuggestions = false;
        }
    }

    public static IReadOnlyList<ChatCommandView> FilterCommands(
        IEnumerable<ChatCommandView> commands,
        string partial
    )
    {
        return string.IsNullOrEmpty(partial)
            ? commands.ToArray()
            : commands
                .Where(command =>
                    command.Name.Contains(partial, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
    }

    public void SelectCommand(ChatCommandView command)
    {
        Input = "/" + command.Name + " ";
        ShowCommandSuggestions = false;
    }

    public void HighlightCommand(ChatCommandView command)
    {
        var index = FilteredCommands.ToList().IndexOf(command);
        if (index >= 0)
            SelectedCommandIndex = index;
    }

    public async Task ImportFileAsync(IBrowserFile file)
    {
        const long maxBytes = 10 * 1024 * 1024;
        try
        {
            if (file.Size == 0)
            {
                SetAttachmentError("That file is empty.");
                return;
            }
            if (file.Size > maxBytes)
            {
                SetAttachmentError("Files must be 10 MB or smaller.");
                return;
            }
            await using var input = file.OpenReadStream(maxBytes);
            using var buffer = new MemoryStream((int)file.Size);
            await input.CopyToAsync(buffer);
            var data = buffer.ToArray();
            SetAttachment(data, file.Name, ExtractText(data, file.Name));
        }
        catch (Exception ex)
        {
            SetAttachmentError($"Couldn't read that file: {ex.Message}");
        }
        finally
        {
            FileInputVersion++;
        }
    }
}

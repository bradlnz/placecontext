using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Chat;
using PlaceContext.Infrastructure.Caching;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ChatViewModel : PageViewModel
{
    private readonly IChatGateway _gateway;
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

    public ChatViewModel(
        IChatGateway gateway, IPlaceContextService svc, IMcpClientService mcpClient,
        PortalUiState ui, IChatMemoryStore memoryStore, AgentContextBuilder contextBuilder,
        IDocumentTextExtractor docExtractor, IObjectStore objectStore,
        IRunArtifactLinkRepository links, ICurrentTenant tenant, IContentIndexer contentIndexer)
    {
        _gateway = gateway;
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
    public IReadOnlyList<McpConnectionView> McpConnections { get; private set; } = Array.Empty<McpConnectionView>();
    public bool ShowAddMcp { get; set; }
    public bool ShowAuthFields { get; set; }
    public string NewMcpName { get; set; } = "";
    public string NewMcpTransport { get; set; } = "http";
    public string NewMcpEndpoint { get; set; } = "";
    public string NewMcpCommand { get; set; } = "";
    public string NewMcpArgs { get; set; } = "";
    public string NewMcpAuthType { get; set; } = "none";
    public string NewMcpAuthToken { get; set; } = "";
    public string NewMcpAuthHeader { get; set; } = "";
    public string NewMcpOAuthScopes { get; set; } = "";
    public string PendingSystemPrompt { get; set; } = "";
    public float PendingTemperature { get; set; }
    public int PendingMaxTokens { get; set; }
    public bool PendingRagEnabled { get; set; } = true;
    public int PendingMaxContextChunks { get; set; } = 5;
    public bool ShowSessionList { get; set; } = true;
    public IReadOnlyList<ChatSessionSummary> Sessions { get; private set; } = Array.Empty<ChatSessionSummary>();
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

    // ── Private state ────────────────────────────────────────────────────────

    private string _systemPrompt = "You are a PlaceContext agent — a casual, friendly Australian mate who helps with project data, job runs, graphs, and artifacts. Talk like a real Aussie: use words like 'mate', 'no worries', 'righto', 'cheers', 'good on ya', 'sweet as', 'no drama'. Keep it relaxed and warm. CRITICAL: NEVER think out loud. NEVER write your thought process, reasoning, self-correction, or commentary about the conversation. NEVER output phrases like 'Looking at the conversation', 'Let me think', 'I notice', 'Actually', 'Re-reading', or 'Hmm'. NEVER wrap your answer in <think>, <reasoning>, or <reflection> tags. If you catch yourself starting to explain your reasoning, STOP and give the answer directly. When data is needed, call the right tool immediately without explaining why. Keep answers short unless the user asks for detail. Never use formal corporate language — you're a mate, not a robot.";
    private bool _ragEnabled = true;
    private int _maxContextChunks = 5;
    private float _temperature = 0.7f;
    private int _maxTokens = 2048;
    private int _toolCallCounter;
    private string _sessionTitle = "New Chat";
    private Guid? _sessionId;
    public Guid? SessionId => _sessionId;
    private TaskCompletionSource<ClarificationResult>? _clarificationTcs;
    private bool _attachmentsBucketEnsured;

    private const string AttachmentsBucket = "chat-attachments";
    private const int MaxArtifactInlineBytes = 512 * 1024;
    private const int MaxArtifactDocumentBytes = 10 * 1024 * 1024;
    private const int MaxArtifactExtractedTextLength = 200_000;

    // ── Gateway status ───────────────────────────────────────────────────────

    public string GatewayStatusText =>
        _gateway is ClusterChatGateway cg ? cg.StatusText :
        _gateway.IsEnabled ? "Chat backend active" :
        "No model configured";

    public bool GatewayIsCluster => _gateway is ClusterChatGateway;

    public IChatGateway Gateway => _gateway;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _ui.Set("Chat", "Agent inference");
        ProjectId = _ui.CurrentProjectId;
        ProjectName = _ui.CurrentProjectName ?? "";
        if (ProjectId.HasValue)
        {
            NewSession();
            NotifyStateChanged();
            // Fire-and-forget: populate sidebar data in the background so the UI renders immediately.
            _ = LoadAndRestoreSessionAsync();
        }
    }

    private async Task LoadAndRestoreSessionAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadAgentConfigAsync(),
                LoadSessionsAsync(),
                LoadMcpConnectionsAsync(),
                LoadPanelArtifactsAsync());
            if (Sessions.Count > 0)
                await SelectSessionAsync(Sessions[0]);
        }
        catch { }
        NotifyStateChanged();
    }

    public void OnProjectChanged()
    {
        if (ProjectId == _ui.CurrentProjectId) return;
        ProjectId = _ui.CurrentProjectId;
        ProjectName = _ui.CurrentProjectName ?? "";
        _ = OnProjectChangedAsync();
    }

    private async Task OnProjectChangedAsync()
    {
        if (!ProjectId.HasValue) return;
        await LoadAgentConfigAsync();
        await LoadGraphAsync();
        ActiveActions.Clear();
        FetchedData.Clear();
        ToolHistory.Clear();
        await LoadSessionsAsync();
        await LoadMcpConnectionsAsync();
        await LoadPanelArtifactsAsync();
        NewSession();
        NotifyStateChanged();
    }

    // ── Session management ───────────────────────────────────────────────────

    public async Task LoadSessionsAsync()
    {
        if (!ProjectId.HasValue) return;
        try { Sessions = await _memoryStore.ListSessionsAsync(ProjectId.Value); }
        catch { Sessions = Array.Empty<ChatSessionSummary>(); }
    }

    public void NewSession()
    {
        if (Streaming) return;
        _sessionId = Guid.NewGuid();
        _sessionTitle = "New Chat";
        Messages.Clear();
        ActiveActions.Clear();
        FetchedData.Clear();
        ToolHistory.Clear();
        PendingClarification = null;
        ClarificationSelected.Clear();
        RenderedChartIds.Clear();
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        StreamBuffer = "";
        NotifyStateChanged();
    }

    public async Task DeleteSessionAsync(ChatSessionSummary session)
    {
        if (Streaming) return;
        try { await _memoryStore.DeleteSessionAsync(session.Id); } catch { }
        if (session.Id == _sessionId) NewSession();
        await LoadSessionsAsync();
        NotifyStateChanged();
    }

    public async Task ClearCurrentSessionAsync()
    {
        if (Streaming) return;
        if (_sessionId.HasValue)
        {
            try { await _memoryStore.ClearSessionMemoryAsync(_sessionId.Value); } catch { }
        }
        _sessionTitle = "New Chat";
        Messages.Clear();
        ActiveActions.Clear();
        FetchedData.Clear();
        ToolHistory.Clear();
        PendingClarification = null;
        ClarificationSelected.Clear();
        RenderedChartIds.Clear();
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        StreamBuffer = "";
        await LoadSessionsAsync();
        NotifyStateChanged();
    }

    public async Task SelectSessionAsync(ChatSessionSummary session)
    {
        if (Streaming || !ProjectId.HasValue) return;
        try
        {
            var memory = await _memoryStore.GetSessionAsync(session.Id);
            if (memory != null)
            {
                _sessionId = session.Id;
                _sessionTitle = memory.Title;
                Messages.Clear();
                foreach (var m in memory.Messages)
                {
                    var msg = new AgentMessage(m.Role, m.Content)
                    {
                        Thinking = m.Thinking,
                        AttachmentName = m.AttachmentName,
                        AttachmentKey = m.AttachmentKey,
                        AttachmentContentType = m.AttachmentContentType,
                        AttachmentSizeBytes = m.AttachmentSizeBytes,
                    };
                    if (m.ToolCalls != null)
                        msg.ToolCalls.AddRange(m.ToolCalls.Select(tc => new ToolCallInfo
                        {
                            ToolName = tc.ToolName,
                            Args = tc.Args,
                            Status = Enum.TryParse<AgentToolCallStatus>(tc.Status, out var s) ? s : AgentToolCallStatus.Completed,
                            Result = tc.Result,
                            ResultType = tc.ResultType,
                        }));
                    RecoverLostMapCalls(msg);
                    Messages.Add(msg);
                }
                NotifyStateChanged();
            }
        }
        catch { }
    }

    private async Task SaveCurrentSessionAsync()
    {
        if (!ProjectId.HasValue || _sessionId == null) return;
        var memory = new ChatSessionMemory(
            _sessionId.Value, ProjectId.Value, _sessionTitle,
            Messages.Select(m => new ChatMemoryMessage(
                m.Role, m.Content, DateTimeOffset.Now,
                m.ToolCalls.Select(tc => new ChatMemoryToolCall(
                    tc.ToolName, tc.Args, tc.Status.ToString(), tc.Result, tc.ResultType)).ToList(),
                m.AttachmentName, m.AttachmentKey, m.AttachmentContentType, m.AttachmentSizeBytes, m.Thinking)).ToList(),
            DateTimeOffset.Now, DateTimeOffset.Now);
        try { await _memoryStore.SaveSessionAsync(_sessionId.Value, memory); } catch { }
        await LoadSessionsAsync();
    }

    // ── Agent config ─────────────────────────────────────────────────────────

    private async Task LoadAgentConfigAsync()
    {
        if (!ProjectId.HasValue) return;
        try
        {
            var config = await _svc.GetAgentConfigAsync(ProjectId.Value);
            _systemPrompt = config.SystemPrompt;
            _temperature = config.Temperature;
            _maxContextChunks = config.MaxContextChunks;
        }
        catch { }
    }

    public void OpenSettings()
    {
        PendingSystemPrompt = _systemPrompt;
        PendingTemperature = _temperature;
        PendingMaxTokens = _maxTokens;
        PendingRagEnabled = _ragEnabled;
        PendingMaxContextChunks = _maxContextChunks;
        SettingsTab = "prompt";
        ShowAddMcp = false;
        ShowSettings = true;
        NotifyStateChanged();
    }

    public void CloseSettings() { ShowSettings = false; NotifyStateChanged(); }

    public async Task SaveSettingsAsync()
    {
        _systemPrompt = PendingSystemPrompt;
        _temperature = PendingTemperature;
        _maxTokens = PendingMaxTokens;
        _ragEnabled = PendingRagEnabled;
        _maxContextChunks = PendingMaxContextChunks;
        ShowSettings = false;
        if (ProjectId.HasValue)
        {
            try
            {
                var config = await _svc.GetAgentConfigAsync(ProjectId.Value);
                await _svc.UpdateAgentConfigAsync(new UpdateAgentConfigCommand(
                    config.ProjectId, config.BaseModel, _systemPrompt, _maxContextChunks,
                    _temperature, config.TopP, config.Enabled));
            }
            catch { }
        }
        NotifyStateChanged();
    }

    // ── MCP connections ──────────────────────────────────────────────────────

    public async Task LoadMcpConnectionsAsync()
    {
        if (!ProjectId.HasValue) return;
        try { McpConnections = await _svc.ListMcpConnectionsAsync(ProjectId.Value); }
        catch { McpConnections = Array.Empty<McpConnectionView>(); }
    }

    public void ShowAddMcpForm()
    {
        NewMcpName = "";
        NewMcpTransport = "http";
        NewMcpEndpoint = "";
        NewMcpCommand = "";
        NewMcpArgs = "";
        NewMcpAuthType = "none";
        NewMcpAuthToken = "";
        NewMcpAuthHeader = "";
        NewMcpOAuthScopes = "";
        ShowAuthFields = false;
        ShowAddMcp = true;
        NotifyStateChanged();
    }

    public async Task AddMcpConnectionAsync()
    {
        if (!ProjectId.HasValue || string.IsNullOrWhiteSpace(NewMcpName)) return;
        try
        {
            var conn = await _svc.CreateMcpConnectionAsync(new CreateMcpConnectionCommand(
                ProjectId.Value, NewMcpName, NewMcpTransport,
                NewMcpTransport != "stdio" ? NewMcpEndpoint : null,
                NewMcpTransport == "stdio" ? NewMcpCommand : null,
                NewMcpTransport == "stdio" ? NewMcpArgs : null,
                NewMcpAuthType != "none" ? NewMcpAuthType : null,
                NewMcpAuthType != "none" && NewMcpAuthType != "oauth" ? NewMcpAuthToken : null,
                NewMcpAuthType == "header" ? NewMcpAuthHeader : null,
                null,
                NewMcpAuthType == "oauth" ? NewMcpOAuthScopes : null));
            ShowAddMcp = false;
            await LoadMcpConnectionsAsync();
            NotifyStateChanged();
        }
        catch { }
    }

    public async Task TestMcpConnectionAsync(Guid id)
    {
        try { await _svc.TestMcpConnectionAsync(id); await LoadMcpConnectionsAsync(); NotifyStateChanged(); }
        catch { }
    }

    public async Task DeleteMcpConnectionAsync(Guid id)
    {
        try { await _svc.DeleteMcpConnectionAsync(id); await LoadMcpConnectionsAsync(); NotifyStateChanged(); }
        catch { }
    }

    public string GetOAuthUrl(Guid connectionId) => $"/mcp-oauth/start?connectionId={connectionId}";

    // ── Panel artifacts ──────────────────────────────────────────────────────

    private async Task LoadPanelArtifactsAsync()
    {
        if (!ProjectId.HasValue) return;
        try
        {
            var artifacts = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 8, null);
            PanelArtifacts.Clear();
            PanelArtifacts.AddRange(artifacts);
        }
        catch { }
    }

    public void MergePanelArtifacts(IEnumerable<ArtifactFileView> found)
    {
        var incoming = found.ToList();
        if (incoming.Count == 0) return;
        PanelArtifacts.RemoveAll(a => incoming.Any(f => f.Id == a.Id));
        PanelArtifacts.InsertRange(0, incoming);
        if (PanelArtifacts.Count > 10)
            PanelArtifacts.RemoveRange(10, PanelArtifacts.Count - 10);
    }

    // ── Graph ────────────────────────────────────────────────────────────────

    private async Task LoadGraphAsync()
    {
        if (!ProjectId.HasValue) return;
        try
        {
            var graph = await _svc.GetGraphVizAsync(ProjectId.Value);
            GraphNodes.Clear();
            GraphNodes.AddRange(graph.Nodes);
            GraphLinks = graph.LinkCount;
        }
        catch { }
    }

    // ── Chat send ────────────────────────────────────────────────────────────

    public async Task SendAsync(Func<Task> scrollToBottom, Func<Task> scrollAfterRender)
    {
        var text = Input.Trim();
        if (string.IsNullOrEmpty(text) || Streaming || !ProjectId.HasValue) return;

        var userText = text;
        if (AttachedFileName != null && AttachedFileText != null)
            userText += $"\n\n## Attached file: {AttachedFileName}\n\n{AttachedFileText}";

        var userMsg = new AgentMessage("user", userText);

        if (AttachedFile != null && AttachedFileName != null && _sessionId.HasValue && _objectStore.IsEnabled)
        {
            try
            {
                if (!_attachmentsBucketEnsured)
                {
                    await _objectStore.EnsureBucketAsync(AttachmentsBucket);
                    _attachmentsBucketEnsured = true;
                }
                var contentType = ContentTypeFor(AttachedFileName);
                var key = $"chat/{_tenant.TenantId}/{ProjectId!.Value}/{_sessionId.Value}/{Guid.NewGuid():N}-{SanitizeFileName(AttachedFileName)}";
                await _objectStore.PutAsync(AttachmentsBucket, key, AttachedFile, contentType);
                userMsg.AttachmentName = AttachedFileName;
                userMsg.AttachmentKey = key;
                userMsg.AttachmentContentType = contentType;
                userMsg.AttachmentSizeBytes = AttachedFile.Length;
            }
            catch { }
        }
        else if (AttachedFileName != null)
        {
            userMsg.AttachmentName = AttachedFileName;
            userMsg.AttachmentSizeBytes = AttachedFile?.Length ?? 0;
        }

        Messages.Add(userMsg);
        Input = "";
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        Streaming = true;
        StreamBuffer = "";

        if (Messages.Count(m => m.Role == "user") == 1)
            _sessionTitle = text.Length > 50 ? text[..50] + "…" : text;

        NotifyStateChanged();
        await scrollToBottom();

        // Build RAG context in parallel — don't block the typing indicator.
        var ragTask = _ragEnabled
            ? _contextBuilder.BuildContextAsync(ProjectId.Value, text, _maxContextChunks).ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : "")
            : Task.FromResult("");

        try
        {
            var ct = CancellationToken.None;
            var maxToolRounds = 3;
            var round = 0;

            while (round < maxToolRounds)
            {
                round++;
                var ragContext = ragTask.IsCompleted ? ragTask.Result : await ragTask;
                var messages = BuildChatMessages(ragContext);
                StreamBuffer = "";
                CurrentToolCall = null;
                NotifyStateChanged();

                if (round > 1 || Messages.Any(m => m.ToolCalls.Count > 0))
                    await Task.Delay(300, ct);

                if (_gateway is ClusterChatGateway cg && cg.IsEnabled)
                {
                    var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                    var tokenCount = 0;
                    await foreach (var token in cg.ChatStreamAsync(messages, settings, ct))
                    {
                        StreamBuffer += token;
                        tokenCount++;
                        if (IsRepetitionLoopTail(StreamBuffer))
                        {
                            StreamBuffer = TruncateRepeatedLines(StreamBuffer);
                            break;
                        }
                        NotifyStateChanged();
                        await scrollAfterRender();
                    }
                    if (tokenCount == 0)
                        StreamBuffer = "The model returned an empty response. This might be due to the conversation context. Please try rephrasing your question.";
                }
                else if (_gateway is ClusterChatGateway configuredButNotReady && !configuredButNotReady.IsEnabled)
                {
                    StreamBuffer = "Cluster is starting — please try again in a moment.";
                }
                else
                {
                    var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                    StreamBuffer = await _gateway.ChatAsync(messages, settings, ct);
                }

                var toolCalls = ParseToolCalls(StreamBuffer);
                if (toolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(StreamBuffer))
                        StreamBuffer = "I've processed your request but didn't generate a text response. The tool results are shown above.";
                    break;
                }

                var toolResults = new System.Text.StringBuilder();
                toolResults.Append("\n\n## Tool Results\n\n");
                foreach (var tc in toolCalls)
                {
                    CurrentToolCall = tc;
                    tc.Id = _toolCallCounter++;
                    tc.Status = AgentToolCallStatus.Running;
                    NotifyStateChanged();

                    const int maxRetries = 2;
                    const int timeoutSeconds = 15;
                    ToolCallResult result = ToolCallResult.Fail("uninitialized");
                    for (var attempt = 0; attempt <= maxRetries; attempt++)
                    {
                        tc.RetryCount = attempt;
                        NotifyStateChanged();
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        try { result = await ExecuteToolAsync(tc.ToolName, tc.Args, linkedCts.Token); }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                        { result = ToolCallResult.Fail($"Timed out after {timeoutSeconds}s"); }
                        if (result.Success || !IsTransientError(result.Error ?? "")) break;
                        if (attempt < maxRetries) await Task.Delay(1000, ct);
                    }

                    tc.Status = result.Success ? AgentToolCallStatus.Completed : AgentToolCallStatus.Error;
                    tc.Result = result.Data ?? result.Error;
                    tc.ResultType = result.IsMap ? "map" : result.IsGraph ? "graph" : result.IsArtifact ? "artifact" : "text";

                    var action = ActiveActions.FirstOrDefault(a => a.ToolName == tc.ToolName && a.Status == AgentToolCallStatus.Running);
                    if (action != null)
                    {
                        action.Status = result.Success ? AgentToolCallStatus.Completed : AgentToolCallStatus.Error;
                        action.Detail = result.Success ? "done" : (result.Error?.Length > 40 ? result.Error[..40] + "…" : result.Error ?? "error");
                    }

                    toolResults.Append("### " + tc.ToolName + "\n");
                    toolResults.Append("Args: " + tc.Args + "\n");
                    toolResults.Append(result.Success ? "Result:\n" : "Error:\n");
                    toolResults.Append(result.Data ?? result.Error);
                    toolResults.Append("\n\n");
                }

                var textContent = StripToolCallSyntax(StreamBuffer).Trim();
                var rawCleaned = textContent;
                var split = SplitThinking(textContent);
                textContent = split.Answer;
                if (string.IsNullOrWhiteSpace(textContent) && toolCalls.Count == 0 && !IsAllReasoning(rawCleaned))
                    textContent = FriendlyLoadingQuip();

                var assistantMsg = new AgentMessage("assistant", textContent);
                assistantMsg.Thinking = string.IsNullOrWhiteSpace(split.Thinking) ? null : split.Thinking;
                assistantMsg.ToolCalls.AddRange(toolCalls);
                Messages.Add(assistantMsg);
                Messages.Add(new AgentMessage("system", toolResults.ToString()));
                StreamBuffer = "";
                NotifyStateChanged();
                await scrollAfterRender();
            }

            if (Messages.Count == 0 || Messages.Last().Role != "assistant")
            {
                var finalSplit = SplitThinking(StreamBuffer ?? "");
                var finalContent = string.IsNullOrWhiteSpace(finalSplit.Answer) ? FriendlyLoadingQuip() : finalSplit.Answer;
                var finalMsg = new AgentMessage("assistant", finalContent);
                finalMsg.Thinking = string.IsNullOrWhiteSpace(finalSplit.Thinking) ? null : finalSplit.Thinking;
                Messages.Add(finalMsg);
            }

            var hallucination = DetectHallucination();
            if (hallucination.Detected)
            {
                Messages.Add(new AgentMessage("assistant", hallucination.CorrectionPrompt ?? "Let me try that again…"));
                NotifyStateChanged();
                await scrollAfterRender();

                if (hallucination.ArtifactId != null)
                {
                    Streaming = true;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    var showResult = await ExecuteShowArtifactAsync(hallucination.ArtifactId, ct);
                    if (showResult.Success && !string.IsNullOrWhiteSpace(showResult.Data))
                    {
                        var followUp = $"The artifact content is below. Summarise it accurately — do not fabricate anything.\n\n{showResult.Data}";
                        Messages.Add(new AgentMessage("system", followUp));
                        NotifyStateChanged();
                        var retryMessages = BuildChatMessages("");
                        StreamBuffer = "";
                        var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                        if (_gateway is ClusterChatGateway cg2 && cg2.IsEnabled)
                        {
                            await foreach (var token in cg2.ChatStreamAsync(retryMessages, settings, ct))
                            {
                                StreamBuffer += token;
                                if (IsRepetitionLoopTail(StreamBuffer)) { StreamBuffer = TruncateRepeatedLines(StreamBuffer); break; }
                                NotifyStateChanged();
                                await scrollAfterRender();
                            }
                        }
                        else { StreamBuffer = await _gateway.ChatAsync(retryMessages, settings, ct); }
                        var sumSplit = SplitThinking(StreamBuffer);
                        var summarised = TruncateRepeatedLines(sumSplit.Answer);
                        if (!string.IsNullOrWhiteSpace(summarised))
                        {
                            var sumMsg = new AgentMessage("assistant", summarised);
                            sumMsg.Thinking = string.IsNullOrWhiteSpace(sumSplit.Thinking) ? null : sumSplit.Thinking;
                            Messages.Add(sumMsg);
                        }
                    }
                    else
                    {
                        Messages.Add(new AgentMessage("assistant", "Couldn't load the artifact content — it may be missing or corrupted."));
                    }
                    Streaming = false;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    await scrollAfterRender();
                }
                else
                {
                    Streaming = true;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    var retryMessages = BuildChatMessages("");
                    StreamBuffer = "";
                    var settings = new ChatSettings(Temperature: _temperature, MaxTokens: _maxTokens);
                    if (_gateway is ClusterChatGateway cg3 && cg3.IsEnabled)
                    {
                        await foreach (var token in cg3.ChatStreamAsync(retryMessages, settings, ct))
                        {
                            StreamBuffer += token;
                            if (IsRepetitionLoopTail(StreamBuffer)) { StreamBuffer = TruncateRepeatedLines(StreamBuffer); break; }
                            NotifyStateChanged();
                            await scrollAfterRender();
                        }
                    }
                    else { StreamBuffer = await _gateway.ChatAsync(retryMessages, settings, ct); }
                    var retrySplit = SplitThinking(StreamBuffer);
                    var retried = TruncateRepeatedLines(retrySplit.Answer);
                    if (!string.IsNullOrWhiteSpace(retried))
                    {
                        if (DetectGenericHallucination(retried).Detected)
                            retried = "The model got stuck repeating itself — please try asking again.";
                        var retryMsg = new AgentMessage("assistant", retried);
                        retryMsg.Thinking = string.IsNullOrWhiteSpace(retrySplit.Thinking) ? null : retrySplit.Thinking;
                        Messages.Add(retryMsg);
                    }
                    Streaming = false;
                    StreamBuffer = "";
                    NotifyStateChanged();
                    await scrollAfterRender();
                }
            }
        }
        catch (Exception ex)
        {
            Messages.Add(new AgentMessage("assistant", $"[error: {ex.Message}]"));
        }
        finally
        {
            Streaming = false;
            StreamBuffer = "";
            CurrentToolCall = null;
            PendingClarification = null;
            ClarificationSelected.Clear();
            _clarificationTcs = null;
            await SaveCurrentSessionAsync();
            NotifyStateChanged();
            await scrollAfterRender();
        }
    }

    // ── Build chat messages ──────────────────────────────────────────────────

    internal List<ChatMessage> BuildChatMessages(string ragContext = "")
    {
        var antiCoT = "CRITICAL: NEVER think out loud. NEVER write your thought process, reasoning, self-correction, or commentary about the conversation. " +
            "NEVER output text like 'Looking at the conversation', 'Let me think', 'I notice', 'Actually', 'Re-reading', or 'Hmm'. " +
            "NEVER wrap your answer in <think>, <reasoning>, or <reflection> tags. " +
            "If you catch yourself starting to explain your reasoning, STOP and give the answer directly. " +
            "You are a casual Australian mate. Talk like one: use 'mate', 'no worries', 'righto', 'cheers', 'sweet as'. " +
            "Provide only the final answer or the tool call. If a tool is needed, emit it immediately without explanation.\n\n";

        var toolDesc = "Available tools (use [[tool:toolName|args]] syntax). " +
            "IMPORTANT: Always pass ALL known parameters (table names, column names, IDs, etc.) from the conversation context. " +
            "Do not ask the user for information you already have. " +
            "Tool routing: use get_artifacts for reports/files/artifacts, search only for run output text, query_table for table data.\n\n" +
            "Built-in tools:\n" +
            "- [[tool:list_tables|]] - List all project data tables\n" +
            "- [[tool:query_table|tableName|page]] - Query a table (pass table name from context)\n" +
            "- [[tool:list_jobs|]] - List all jobs\n" +
            "- [[tool:list_job_runs|jobId]] - List runs for a job\n" +
            "- [[tool:render_graph|chartType|tableName|columnName]] - Render a chart (pass table AND column names from context, e.g. [[tool:render_graph|bar|cashflow_runs|amount]])\n" +
            "- [[tool:query_graph|]] - Query project dependency graph\n" +
            "- [[tool:search|query]] - Semantic search over job run output text/logs only (not files/reports)\n" +
            "- [[tool:get_artifacts|query]] - Search project artifacts by title/kind. Returns METADATA ONLY: title, kind, size, and id. Does NOT return file content. Use this to find the artifact id, then ALWAYS call show_artifact to get the actual content. Do NOT summarize or describe artifact content based on get_artifacts results alone — you have not seen the content yet.\n" +
            "- [[tool:show_artifact|artifactId]] - Fetches and returns the ACTUAL CONTENT of an artifact (extracted text for docs, raw content for text files). You MUST call this after get_artifacts before summarizing, describing, or answering questions about artifact content. This is the only way to see what's inside an artifact.\n" +
            "- [[tool:schedule_job|jobId|name|cron]] - Create a cron schedule\n" +
            "- [[tool:list_schedules|jobId]] - List job schedules\n" +
            "- [[tool:toggle_schedule|triggerId|true|false]] - Enable/disable schedule\n" +
            "- [[tool:run_job|jobId]] - Run a job now\n" +
            "- [[tool:call_mcp|serverName|toolName|argsJson]] - Call a tool on an external MCP server\n" +
            "- [[tool:list_mcp_tools|serverName]] - List available tools on an MCP server\n" +
            "- [[tool:render_map|specJson]] - Render a Leaflet map (JSON spec with {markers:[{lat,lng,label,color}], polygons:[{coords,color}], center:[lat,lng], zoom}). Example: [[tool:render_map|{\\\"markers\\\":[{\\\"lat\\\":48.135,\\\"lng\\\":11.582,\\\"label\\\":\\\"Munich\\\"}]}]]";

        var systemPrompt = antiCoT + _systemPrompt + "\n\n" + toolDesc;
        if (!string.IsNullOrWhiteSpace(ragContext))
            systemPrompt += "\n\n## Project context (retrieved automatically)\n\n" + ragContext;

        var result = new List<ChatMessage> { new("system", systemPrompt) };
        var history = new List<ChatMessage>();
        for (var i = 0; i < Messages.Count; i++)
        {
            var m = Messages[i];
            if (m.Role == "system") continue;
            var content = m.Content;
            if (m.Role == "assistant")
            {
                if (m.ToolCalls.Count > 0)
                {
                    var toolCallText = string.Join("\n", m.ToolCalls.Select(tc => $"[[tool:{tc.ToolName}|{tc.Args}]]"));
                    content = string.IsNullOrWhiteSpace(content) ? toolCallText : content + "\n\n" + toolCallText;
                }
                if (i + 1 < Messages.Count && Messages[i + 1].Role == "system" && Messages[i + 1].Content.Contains("Tool Results"))
                {
                    content += "\n\n" + Messages[i + 1].Content;
                    i++;
                }
                if (m.Role == "assistant") content = CleanAssistantOutput(content);
            }
            history.Add(new ChatMessage(m.Role, content));
        }
        foreach (var m in history.TakeLast(20)) result.Add(m);
        return result;
    }

    // ── Attachments ──────────────────────────────────────────────────────────

    public string? ExtractText(byte[] data, string name) => _docExtractor.ExtractText(data, name);

    public void SetAttachment(byte[] data, string name, string? extractedText)
    {
        AttachedFile = data;
        AttachedFileName = name;
        AttachedFileText = extractedText;
        NotifyStateChanged();
    }

    public void RemoveAttachment()
    {
        AttachedFile = null;
        AttachedFileName = null;
        AttachedFileText = null;
        NotifyStateChanged();
    }

    // ── Quick actions ────────────────────────────────────────────────────────

    public async Task QuickActionAsync(string context, string action, Func<Task> scrollToBottom)
    {
        if (Streaming || !ProjectId.HasValue) return;
        Input = action switch
        {
            "summarize" => "Summarize the following in 3 bullet points:\n\n" + context,
            "explain" => "Explain the following in more detail, breaking down each concept:\n\n" + context,
            "code" => "Generate clean, well-commented code based on the following description:\n\n" + context,
            "graph" => "Create a visual graph/chart description based on the following:\n\n" + context,
            _ => context
        };
        await SendAsync(scrollToBottom, scrollToBottom);
    }

    public async Task StarterPromptAsync(string prompt, Func<Task> scrollToBottom)
    {
        if (Streaming || !ProjectId.HasValue) return;
        Input = prompt;
        await SendAsync(scrollToBottom, scrollToBottom);
    }

    // ── Clarification ────────────────────────────────────────────────────────

    public void ToggleClarificationOption(string id)
    {
        if (ClarificationSelected.Contains(id)) ClarificationSelected.Remove(id);
        else ClarificationSelected.Add(id);
        NotifyStateChanged();
    }

    public void CancelClarification()
    {
        PendingClarification = null;
        ClarificationSelected.Clear();
        _clarificationTcs?.TrySetResult(new ClarificationResult { Confirmed = false });
        _clarificationTcs = null;
        NotifyStateChanged();
    }

    public void SubmitClarification()
    {
        if (PendingClarification == null) return;
        var selectedLabels = PendingClarification.Options
            .Where(o => ClarificationSelected.Contains(o.Id)).Select(o => o.Label).ToList();
        var userResponse = selectedLabels.Count == 1 ? $"Selected: {selectedLabels[0]}" : $"Selected: {string.Join(", ", selectedLabels)}";
        Messages.Add(new AgentMessage("user", userResponse));
        var result = new ClarificationResult { Confirmed = true, SelectedIds = ClarificationSelected.ToList() };
        PendingClarification = null;
        ClarificationSelected.Clear();
        _clarificationTcs?.TrySetResult(result);
        _clarificationTcs = null;
        NotifyStateChanged();
    }

    public async Task<ClarificationResult> AskClarificationAsync(ClarificationRequest request)
    {
        ClarificationSelected.Clear();
        PendingClarification = request;
        _clarificationTcs = new TaskCompletionSource<ClarificationResult>();
        NotifyStateChanged();
        return await _clarificationTcs.Task;
    }

    // ── Active actions / fetched data / tool history ─────────────────────────

    public void AddActiveAction(string toolName, string detail)
        => ActiveActions.Add(new AgentAction { ToolName = toolName, Detail = detail, Status = AgentToolCallStatus.Running });

    public void CompleteActiveAction(string toolName, bool success)
    {
        var action = ActiveActions.FirstOrDefault(a => a.ToolName == toolName && a.Status == AgentToolCallStatus.Running);
        if (action != null) action.Status = success ? AgentToolCallStatus.Completed : AgentToolCallStatus.Error;
    }

    public void AddFetchedData(string source, int rowCount, string preview)
        => FetchedData.Add(new FetchedData { Source = source, RowCount = rowCount, Preview = preview });

    public void AddToolHistory(string toolName, bool success, string status)
        => ToolHistory.Add(new ToolHistoryEntry { ToolName = toolName, Success = success, Status = status, Timestamp = DateTimeOffset.Now });

    // ── Recover lost map calls from old sessions ─────────────────────────────

    private static void RecoverLostMapCalls(AgentMessage msg)
    {
        if (msg.Role != "assistant" || string.IsNullOrEmpty(msg.Content)) return;
        if (!msg.Content.Contains("[[tool:render_map|", StringComparison.Ordinal)) return;
        foreach (var call in ScanToolCalls(msg.Content).Where(c => c.Name == "render_map"))
        {
            if (msg.ToolCalls.Any(t => t.ResultType == "map" && t.Args == call.Args)) continue;
            try { System.Text.Json.JsonDocument.Parse(call.Args); } catch { continue; }
            msg.ToolCalls.Add(new ToolCallInfo
            {
                ToolName = "render_map",
                Args = call.Args,
                Status = AgentToolCallStatus.Completed,
                Result = call.Args,
                ResultType = "map",
            });
        }
    }

    // ── Content parsing (static, shared with ContentFormatter) ───────────────

    internal static List<ToolCallInfo> ParseToolCalls(string response)
    {
        var calls = new List<ToolCallInfo>();
        foreach (var c in ScanToolCalls(response))
            calls.Add(new ToolCallInfo { ToolName = c.Name, Args = c.Args });
        return calls;
    }

    internal static List<(string Name, string Args, int Start, int Length)> ScanToolCalls(string text)
    {
        var calls = new List<(string Name, string Args, int Start, int Length)>();
        if (string.IsNullOrEmpty(text)) return calls;
        var pos = 0;
        while (pos < text.Length)
        {
            var start = text.IndexOf("[[tool:", pos, StringComparison.Ordinal);
            if (start < 0) break;
            var nameStart = start + "[[tool:".Length;
            var pipe = text.IndexOf('|', nameStart);
            var nextCall = text.IndexOf("[[tool:", nameStart, StringComparison.Ordinal);
            if (pipe < 0 || (nextCall >= 0 && pipe > nextCall)) { pos = nameStart; continue; }
            var searchEnd = nextCall >= 0 ? nextCall : text.Length;
            var close = searchEnd - pipe - 1 > 0
                ? text.LastIndexOf("]]", searchEnd - 1, searchEnd - pipe - 1, StringComparison.Ordinal) : -1;
            if (close < 0) { pos = nameStart; continue; }
            calls.Add((text[nameStart..pipe], text[(pipe + 1)..close], start, close + 2 - start));
            pos = close + 2;
        }
        return calls;
    }

    internal static string StripToolCallSyntax(string text)
    {
        var calls = ScanToolCalls(text);
        if (calls.Count == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        var pos = 0;
        foreach (var c in calls) { sb.Append(text, pos, c.Start - pos); pos = c.Start + c.Length; }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    internal static string FormatContent(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        if (raw.StartsWith("Called "))
        {
            var escaped = System.Net.WebUtility.HtmlEncode(raw);
            return $"<em>{escaped}</em>";
        }
        var attachIdx = raw.IndexOf("\n\n## Attached file:", StringComparison.Ordinal);
        if (attachIdx >= 0) raw = raw[..attachIdx];
        raw = StripThinkTags(raw);
        var cleaned = StripToolCallSyntax(raw).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return "";
        var escaped2 = System.Net.WebUtility.HtmlEncode(cleaned);
        escaped2 = escaped2.Replace("\n", "<br/>");
        escaped2 = System.Text.RegularExpressions.Regex.Replace(escaped2, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        escaped2 = System.Text.RegularExpressions.Regex.Replace(escaped2, @"`(.+?)`", "<code>$1</code>");
        return escaped2;
    }

    internal static string StripThinkTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var tags = new[] { "think", "reasoning", "reflection" };
        foreach (var tag in tags)
        {
            var open = "<" + tag;
            var close = "</" + tag + ">";
            var idx = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var closeIdx = text.IndexOf(close, idx, StringComparison.OrdinalIgnoreCase);
                if (closeIdx < 0) { text = text[..idx]; break; }
                text = text[..idx] + text[(closeIdx + close.Length)..];
                idx = text.IndexOf(open, idx, StringComparison.OrdinalIgnoreCase);
            }
        }
        return text;
    }

    internal static string CleanAssistantOutput(string raw) => SplitThinking(raw).Answer;

    internal static (string Thinking, string Answer) SplitThinking(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", raw ?? "");
        var thinking = new List<string>();
        var tagPattern = @"\x3c(?:think|reasoning|reflection)\b[^>]*>(.*?)(?:\x3c/(?:think|reasoning|reflection)\x3e|$)";
        raw = System.Text.RegularExpressions.Regex.Replace(raw, tagPattern, m =>
        {
            var inner = m.Groups[1].Value.Trim();
            if (inner.Length > 0) thinking.Add(inner);
            return "";
        }, System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\x3c/(?:think|reasoning|reflection)\x3e", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (ScanToolCalls(raw).Count == 0 && IsAllReasoning(raw))
        {
            if (!string.IsNullOrWhiteSpace(raw)) thinking.Add(raw.Trim());
            raw = "";
        }
        var lines = raw.Split('\n');
        var noiseStarts = new[]
        {
            "the user has provided", "looking at the conversation", "however, i notice",
            "let me re-read", "actually, i think", "let me think", "i notice there's",
            "there is no stream(gpu", "[cluster error:", "[error:", "thinking:", "reasoning:", "step-by-step:",
            "based on the", "i see that", "from the conversation", "the user is asking",
            "the user wants", "examining the", "reviewing the", "analyzing the",
            "hmm", "oh wait", "let me consider", "i should", "i need to",
            "wait, let me", "first, let me", "to answer this", "answering this question",
            "the context shows", "looking at this", "re-reading the",
            "now, i", "so, i", "next, i", "i need", "i will need",
            "looking at the tool", "the tool call", "calling the tool",
            "to display", "to show", "to render", "to fetch", "to get",
            "first, let me call", "let me call", "i'll call", "i should call",
            "the correct tool", "the right tool", "using the tool"
        };
        var cleaned = new List<string>();
        foreach (var l in lines)
        {
            if (noiseStarts.Any(p => l.TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                var t = l.Trim();
                if (t.Length > 0 && !t.StartsWith("[", StringComparison.Ordinal)) thinking.Add(t);
            }
            else cleaned.Add(l);
        }
        var result = string.Join("\n", cleaned).Trim();
        result = System.Text.RegularExpressions.Regex.Replace(result, @"^(answer|final answer)[:\-]\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return (string.Join("\n", thinking).Trim(), result);
    }

    internal static bool IsAllReasoning(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        var lines = content.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return false;
        foreach (var l in lines)
        {
            if (l.Contains('|') || l.Contains("http") || System.Text.RegularExpressions.Regex.IsMatch(l, @"\b\d{4,}\b"))
                return false;
        }
        var reasoningStarts = new[]
        {
            "i ", "i'll ", "let me ", "now ", "so ", "next ", "first ",
            "the user", "looking at", "based on", "from the", "to display",
            "to show", "to render", "to fetch", "to get", "calling",
            "the tool", "the correct", "the right", "hmm", "wait",
            "actually", "so, i", "now, i", "next, i", "i need",
            "i should", "i will", "i would", "i can", "i could",
            "to answer", "to summar", "this is", "that is", "here's",
            "one thing", "another", "however", "also"
        };
        var reasoningCount = lines.Count(l => reasoningStarts.Any(p => l.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
        return (double)reasoningCount / lines.Count > 0.7;
    }

    // ── Repetition detection ─────────────────────────────────────────────────

    internal static string NormalizeLineForRepetition(string line)
    {
        var l = line.Trim().ToLowerInvariant();
        l = System.Text.RegularExpressions.Regex.Replace(l, @"^(?:[-*•]|\d+[.)])\s*", "");
        l = System.Text.RegularExpressions.Regex.Replace(l, @"\s+", " ");
        return l;
    }

    internal static bool IsRepetitionLoopTail(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;
        var tail = content.Length > 4000 ? content[^4000..] : content;
        var significant = tail.Split('\n').Select(NormalizeLineForRepetition).Where(l => l.Length > 10).ToList();
        var run = 1;
        for (var i = 1; i < significant.Count; i++)
        {
            run = significant[i] == significant[i - 1] ? run + 1 : 1;
            if (run >= 3 && i == significant.Count - 1) return true;
        }
        return false;
    }

    internal static string TruncateRepeatedLines(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        var lines = content.Split('\n');
        var significant = lines.Select((l, idx) => (norm: NormalizeLineForRepetition(l), idx)).Where(x => x.norm.Length > 10).ToList();
        var run = 1;
        for (var k = 1; k < significant.Count; k++)
        {
            run = significant[k].norm == significant[k - 1].norm ? run + 1 : 1;
            if (run >= 3)
            {
                var firstOccurrenceIndex = significant[k - run + 1].idx;
                return string.Join("\n", lines.Take(firstOccurrenceIndex + 1)).TrimEnd();
            }
        }
        return content;
    }

    // ── Hallucination detection ──────────────────────────────────────────────

    private sealed class HallucinationResult
    {
        public bool Detected { get; init; }
        public string Reason { get; init; } = "";
        public string? ArtifactId { get; init; }
        public string? CorrectionPrompt { get; init; }
    }

    private HallucinationResult DetectHallucination()
    {
        var lastAssistant = Messages.LastOrDefault(m => m.Role == "assistant");
        if (lastAssistant == null) return new() { Detected = false };
        var content = StripToolCallSyntax(lastAssistant.Content).Trim();
        var allToolCalls = Messages.Where(m => m.Role == "assistant").SelectMany(m => m.ToolCalls)
            .Where(tc => tc.Status == AgentToolCallStatus.Completed).ToList();
        var toolResults = Messages.Where(m => m.Role == "system" && m.Content.Contains("Tool Results")).Select(m => m.Content).ToList();

        if (content.Length == 0 || FriendlyLoadingQuips.Contains(content))
            return new() { Detected = true, Reason = "Model produced no answer", CorrectionPrompt = "You did not answer the user's question. Answer it directly now, or call a tool with [[tool:name|args]] if you need data. Do not output your reasoning — just the answer or the tool call." };

        var genericResult = DetectGenericHallucination(content);
        if (genericResult.Detected) return genericResult;

        if (allToolCalls.Count > 0)
        {
            var artifactResult = DetectArtifactHallucination(allToolCalls, content);
            if (artifactResult.Detected) return artifactResult;
            var tableResult = DetectTableHallucination(allToolCalls, toolResults, content);
            if (tableResult.Detected) return tableResult;
            var searchResult = DetectSearchHallucination(allToolCalls, toolResults, content);
            if (searchResult.Detected) return searchResult;
            var runsResult = DetectRunsHallucination(allToolCalls, toolResults, content);
            if (runsResult.Detected) return runsResult;
            var jobsResult = DetectJobsHallucination(allToolCalls, toolResults, content);
            if (jobsResult.Detected) return jobsResult;
            var errorResult = DetectErrorMaskingHallucination(allToolCalls, content);
            if (errorResult.Detected) return errorResult;
            var emptyResult = DetectEmptyAfterTools(allToolCalls, content);
            if (emptyResult.Detected) return emptyResult;
        }
        var intentResult = DetectIntentMismatch(content);
        if (intentResult.Detected) return intentResult;
        return new() { Detected = false };
    }

    private static HallucinationResult DetectGenericHallucination(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 10) return new() { Detected = false };
        var lower = content.ToLowerInvariant();
        var words = lower.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 2; i < words.Length; i++)
        {
            if (words[i] == words[i - 1] && words[i - 1] == words[i - 2] && words[i].Length > 2)
                return new() { Detected = true, Reason = $"Word repetition: '{words[i]}'", CorrectionPrompt = "Your response contained repeated words. Please provide a clear, concise answer without repeating words or phrases." };
        }
        {
            var significant = content.Split('\n').Select(NormalizeLineForRepetition).Where(l => l.Length > 10).ToList();
            var run = 1;
            for (var i = 1; i < significant.Count; i++)
            {
                run = significant[i] == significant[i - 1] ? run + 1 : 1;
                if (run >= 3) return new() { Detected = true, Reason = $"Line repetition: {run}+ times", CorrectionPrompt = "Your response repeated the same line over and over. State each point once, then stop." };
            }
        }
        if (words.Length >= 6)
        {
            var phraseCounts = new Dictionary<string, int>();
            for (var i = 0; i <= words.Length - 3; i++) { var phrase = $"{words[i]} {words[i + 1]} {words[i + 2]}"; phraseCounts.TryGetValue(phrase, out var count); phraseCounts[phrase] = count + 1; }
            var maxPhrase = phraseCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (maxPhrase.Value >= 3) return new() { Detected = true, Reason = $"Phrase repetition: '{maxPhrase.Key}' x{maxPhrase.Value}", CorrectionPrompt = "Your response contained repeated phrases. Please provide a clear, concise answer without repeating the same phrases." };
        }
        if (words.Length > 8) { var shortWords = words.Count(w => w.Length <= 2); if ((float)shortWords / words.Length > 0.6f) return new() { Detected = true, Reason = "Gibberish", CorrectionPrompt = "Your response was unclear. Please provide a meaningful answer." }; }
        return new() { Detected = false };
    }

    private static HallucinationResult DetectArtifactHallucination(List<ToolCallInfo> toolCalls, string content)
    {
        var getArtifactCalls = toolCalls.Where(tc => tc.ToolName == "get_artifacts").ToList();
        if (getArtifactCalls.Count == 0 || toolCalls.Any(tc => tc.ToolName == "show_artifact")) return new() { Detected = false };
        var hasRealArtifacts = getArtifactCalls.Any(tc => tc.Result != null && !tc.Result.StartsWith("No artifacts") && !tc.Result.StartsWith("No artifacts matched") && tc.Result.Contains("id:"));
        if (!hasRealArtifacts) return new() { Detected = false };
        return new() { Detected = true, Reason = "Agent did not call show_artifact after get_artifacts", CorrectionPrompt = "You found artifacts but did not fetch their content. You MUST call [[tool:show_artifact|id]] to get the actual content before summarizing. Pick the most relevant artifact and call show_artifact now." };
    }

    private static HallucinationResult DetectEmptyAfterTools(List<ToolCallInfo> toolCalls, string content)
    {
        if (toolCalls.Count == 0) return new() { Detected = false };
        var successfulCalls = toolCalls.Where(tc => tc.Status == AgentToolCallStatus.Completed).ToList();
        if (successfulCalls.Count == 0 || content.Length >= 40) return new() { Detected = false };
        return new() { Detected = true, Reason = $"Empty/short response after {successfulCalls.Count} successful tool call(s)", CorrectionPrompt = "You called tools but did not provide an answer. Use the tool results to give the user a direct, helpful response. Do not output your thinking process — just answer." };
    }

    private static HallucinationResult DetectTableHallucination(List<ToolCallInfo> toolCalls, List<string> toolResults, string content)
    {
        var tableCalls = toolCalls.Where(tc => tc.ToolName == "query_table").ToList();
        if (tableCalls.Count == 0) return new() { Detected = false };
        var allResultText = string.Join("\n", toolCalls.Select(tc => tc.Result ?? ""));
        var resultWords = ExtractMeaningfulWords(allResultText);
        if (resultWords.Count == 0) return new() { Detected = false };
        var quotedInResponse = System.Text.RegularExpressions.Regex.Matches(content, @"""([^""]{3,})""")
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Where(v => !new[] { "the", "and", "for", "not", "you", "are", "was", "has", "but", "can", "may", "all", "its" }.Contains(v)).ToList();
        var fabricated = quotedInResponse.Where(q => !resultWords.Contains(q)).ToList();
        if (fabricated.Count >= 2) return new() { Detected = true, Reason = $"Fabricated values: {string.Join(", ", fabricated.Take(3))}", CorrectionPrompt = "Your response contained values that were not in the table data. Only reference values that appear in the tool results." };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectSearchHallucination(List<ToolCallInfo> toolCalls, List<string> toolResults, string content)
    {
        var searchCalls = toolCalls.Where(tc => tc.ToolName == "search").ToList();
        if (searchCalls.Count == 0) return new() { Detected = false };
        var noMatch = searchCalls.Any(tc => tc.Result != null && tc.Result.Contains("No matching run outputs found"));
        if (noMatch && content.Length > 40 && (content.Contains("found") || content.Contains("match") || content.Contains("result") || content.Contains("showed")))
            return new() { Detected = true, Reason = "Agent claimed search found results when search returned no matches", CorrectionPrompt = "The search returned no matches. Do not fabricate results. Tell the user no matches were found." };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectRunsHallucination(List<ToolCallInfo> toolCalls, List<string> toolResults, string content)
    {
        var runCalls = toolCalls.Where(tc => tc.ToolName == "list_job_runs").ToList();
        if (runCalls.Count == 0) return new() { Detected = false };
        var allResultText = string.Join("\n", runCalls.Select(tc => tc.Result ?? ""));
        if (allResultText.Contains("Job runs: 0") && content.Length > 50)
        {
            var statusWords = new[] { "completed", "failed", "running", "pending", "success", "error" };
            if (statusWords.Any(s => content.ToLowerInvariant().Contains(s)))
                return new() { Detected = true, Reason = "Agent described job runs when list_job_runs returned 0 runs", CorrectionPrompt = "There are no job runs to describe. The tool returned 0 runs." };
        }
        return new() { Detected = false };
    }

    private static HallucinationResult DetectJobsHallucination(List<ToolCallInfo> toolCalls, List<string> toolResults, string content)
    {
        var jobCalls = toolCalls.Where(tc => tc.ToolName == "list_jobs").ToList();
        if (jobCalls.Count == 0) return new() { Detected = false };
        var allResultText = string.Join("\n", jobCalls.Select(tc => tc.Result ?? ""));
        if (allResultText.Contains("Jobs: 0") && content.Length > 50)
            return new() { Detected = true, Reason = "Agent described jobs when list_jobs returned 0 jobs", CorrectionPrompt = "There are no jobs in this project. The tool returned 0 jobs." };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectErrorMaskingHallucination(List<ToolCallInfo> toolCalls, string content)
    {
        var failedCalls = toolCalls.Where(tc =>
            (tc.Status == AgentToolCallStatus.Error || (tc.Result != null && tc.Result.StartsWith("Error:"))) && tc.ToolName != "show_artifact").ToList();
        if (failedCalls.Count == 0 || content.Length < 60) return new() { Detected = false };
        var acknowledgesError = content.Contains("error", StringComparison.OrdinalIgnoreCase) || content.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || content.Contains("couldn't", StringComparison.OrdinalIgnoreCase) || content.Contains("unable", StringComparison.OrdinalIgnoreCase)
            || content.Contains("sorry", StringComparison.OrdinalIgnoreCase) || content.Contains("no worries", StringComparison.OrdinalIgnoreCase);
        if (!acknowledgesError) return new() { Detected = true, Reason = $"Agent produced content after {failedCalls.First().ToolName} error", CorrectionPrompt = $"The tool {failedCalls.First().ToolName} returned an error: {(failedCalls.First().Result?.Length > 200 ? failedCalls.First().Result[..200] + "…" : failedCalls.First().Result)}. Acknowledge the error to the user." };
        return new() { Detected = false };
    }

    private static HallucinationResult DetectIntentMismatch(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20) return new() { Detected = false };
        var lower = content.ToLowerInvariant();
        var rephrasePatterns = new[] { "you asked about", "you want to know", "your question is", "you're asking", "the question asks", "you're looking for" };
        if (rephrasePatterns.Any(p => lower.Contains(p)) && content.Length < 150)
        {
            var answerIndicators = new[] { "here", "the answer", "is ", "are ", "was ", "has ", "shows", "includes", "contains" };
            if (!(answerIndicators.Any(a => lower.Contains(a)) && content.Length > 80))
                return new() { Detected = true, Reason = "Response rephrases the question without answering it", CorrectionPrompt = "You rephrased the user's question but didn't answer it. Use the available tools to find the answer and provide it directly." };
        }
        var fillerWords = new[] { "basically", "essentially", "actually", "literally", "just", "well", "so", "like", "um", "uh", "hmm" };
        var fillerCount = fillerWords.Count(f => System.Text.RegularExpressions.Regex.IsMatch(lower, $@"\b{System.Text.RegularExpressions.Regex.Escape(f)}\b"));
        if (fillerCount >= 4 && content.Length < 200)
            return new() { Detected = true, Reason = $"Excessive filler words ({fillerCount})", CorrectionPrompt = "Your response contained too many filler words. Please give a direct, clear answer without padding." };
        return new() { Detected = false };
    }

    private static HashSet<string> ExtractMeaningfulWords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "are", "but", "not", "you", "all", "can", "had", "her", "was", "one",
            "our", "out", "has", "his", "how", "its", "may", "new", "now", "old", "see", "way", "who",
            "did", "get", "let", "say", "she", "too", "use", "this", "that", "with", "have", "from",
            "they", "been", "said", "each", "make", "like", "just", "over", "such", "take", "year",
            "them", "some", "than", "time", "very", "when", "come", "could", "what", "there",
            "result", "results", "rows", "row", "table", "data", "matches", "match", "score"
        };
        return text.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '"', '\'', '/', '|', '-' },
            StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).Where(w => w.Length >= 3 && !stopWords.Contains(w)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Friendly quips ───────────────────────────────────────────────────────

    private static readonly string[] FriendlyLoadingQuips = new[]
    {
        "Cool sussing that out for you…",
        "Give us a sec working out what you need…",
        "Righto, figuring out the best way to answer this…",
        "Hang tight cobber, I'm on it…",
        "Sweet as, just pulling that together…",
        "One sec legend, sorting it out…",
    };

    private static string FriendlyLoadingQuip() => FriendlyLoadingQuips[Random.Shared.Next(FriendlyLoadingQuips.Length)];

    // ── Utility methods ──────────────────────────────────────────────────────

    private static bool IsTransientError(string error)
    {
        if (string.IsNullOrEmpty(error)) return false;
        var lower = error.ToLowerInvariant();
        return lower.Contains("timeout") || lower.Contains("econnreset") || lower.Contains("econnrefused")
            || lower.Contains("socket") || lower.Contains("503") || lower.Contains("502")
            || lower.Contains("429") || lower.Contains("rate limit");
    }

    public static string SanitizeFileName(string name)
    {
        var cleaned = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-'));
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    public static string ContentTypeFor(string fileName) =>
        System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" or ".md" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };

    internal static string FormatToolResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return "<em>Empty result</em>";
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        var separatorIdx = Array.FindIndex(lines, l => l.Trim() == "---");
        if (separatorIdx > 0)
        {
            var headerLine = lines[separatorIdx - 1];
            var headers = headerLine.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var dataLines = lines.Skip(separatorIdx + 1).ToArray();
            sb.Append("<table class=\"tool-table\"><thead><tr>");
            foreach (var h in headers) sb.Append($"<th>{System.Net.WebUtility.HtmlEncode(h)}</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var row in dataLines)
            {
                var cells = row.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                sb.Append("<tr>");
                foreach (var c in cells) sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(c)}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
            return sb.ToString();
        }
        var listItems = lines.Where(l => l.TrimStart().StartsWith("- ")).ToArray();
        if (listItems.Length > 0)
        {
            var headerLine = lines.FirstOrDefault(l => !l.TrimStart().StartsWith("- "));
            if (!string.IsNullOrEmpty(headerLine)) sb.Append($"<div class=\"tool-result-header\">{System.Net.WebUtility.HtmlEncode(headerLine)}</div>");
            sb.Append("<ul class=\"tool-list\">");
            foreach (var item in listItems)
            {
                var text = item.TrimStart()[2..];
                var formatted = System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlEncode(text), @"\(([^)]+)\)", "<span class=\"tool-meta\">($1)</span>");
                sb.Append($"<li>{formatted}</li>");
            }
            sb.Append("</ul>");
            return sb.ToString();
        }
        var escaped = System.Net.WebUtility.HtmlEncode(result).Replace("\n", "<br/>");
        return $"<div class=\"tool-plain\">{escaped}</div>";
    }

    // ── Tool execution ───────────────────────────────────────────────────────

    private async Task<ToolCallResult> ExecuteToolAsync(string toolName, string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail("No project selected");
        try
        {
            return toolName switch
            {
                "query_table" => await ExecuteQueryTableAsync(args, ct),
                "list_tables" => await ExecuteListTablesAsync(ct),
                "list_jobs" => await ExecuteListJobsAsync(ct),
                "list_job_runs" => await ExecuteListJobRunsAsync(args, ct),
                "render_graph" => await ExecuteRenderGraphAsync(args, ct),
                "query_graph" => await ExecuteQueryGraphAsync(ct),
                "search" => await ExecuteSearchAsync(args, ct),
                "get_artifacts" => await ExecuteGetArtifactsAsync(args, ct),
                "show_artifact" => await ExecuteShowArtifactAsync(args, ct),
                "schedule_job" => await ExecuteScheduleJobAsync(args, ct),
                "list_schedules" => await ExecuteListSchedulesAsync(args, ct),
                "toggle_schedule" => await ExecuteToggleScheduleAsync(args, ct),
                "run_job" => await ExecuteRunJobAsync(args, ct),
                "call_mcp" => await ExecuteCallMcpAsync(args, ct),
                "list_mcp_tools" => await ExecuteListMcpToolsAsync(args, ct),
                "render_map" => await ExecuteRenderMapAsync(args, ct),
                _ => ToolCallResult.Fail($"Unknown tool: {toolName}"),
            };
        }
        catch (Exception ex) { return ToolCallResult.Fail(ex.Message); }
    }

    private async Task<ToolCallResult> ExecuteQueryTableAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        var tableName = parts.Length > 0 ? parts[0].Trim() : "";
        var page = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        AddActiveAction("query_table", $"Querying {tableName}...");
        var result = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, page, 50, ct: ct);
        var preview = string.Join("\n", result.Rows.Take(3).Select(r => string.Join(", ", r.Take(4))));
        AddFetchedData(tableName, (int)result.TotalCount, preview);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Table: {tableName} ({result.TotalCount} rows)\n");
        sb.Append(string.Join(" | ", result.Columns));
        sb.Append("\n---\n");
        foreach (var row in result.Rows) { sb.Append(string.Join(" | ", row.Select(v => v?.ToString() ?? "null"))); sb.Append("\n"); }
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteListTablesAsync(CancellationToken ct)
    {
        AddActiveAction("list_tables", "Loading tables...");
        var tables = await _svc.ListProjectDataTablesAsync(ProjectId!.Value, ct);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Project tables: {tables.Count}\n");
        foreach (var t in tables) sb.Append($"- {t.Name} ({t.RowEstimate} rows)\n");
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteListJobsAsync(CancellationToken ct)
    {
        AddActiveAction("list_jobs", "Loading jobs...");
        var jobs = await _svc.ListJobsAsync(ProjectId!.Value, ct);
        var sb = new System.Text.StringBuilder();
        sb.Append($"Jobs: {jobs.Count}\n");
        foreach (var j in jobs) sb.Append($"- {j.Name} (id: {j.Id})\n");
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteListJobRunsAsync(string args, CancellationToken ct)
    {
        AddActiveAction("list_job_runs", args != "" ? $"Runs for {args[..8]}..." : "Loading all runs...");
        var jobId = Guid.TryParse(args, out var id) ? id : Guid.Empty;
        var runs = jobId != Guid.Empty ? await _svc.ListJobRunsAsync(jobId, ct) : new List<JobRunView>();
        var sb = new System.Text.StringBuilder();
        sb.Append($"Job runs: {runs.Count}\n");
        foreach (var r in runs.Take(20)) sb.Append($"- {r.Status} ({r.StartedAt:yyyy-MM-dd HH:mm})\n");
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteSearchAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail("No project selected");
        var query = args.Trim();
        if (query.Length == 0) return ToolCallResult.Fail("Usage: [[tool:search|query]]");
        AddActiveAction("search", $"Searching \"{(query.Length > 30 ? query[..30] + "…" : query)}\"...");
        try
        {
            var matches = await _svc.SearchRunOutputsAsync(ProjectId.Value, query, 8, ct);
            if (matches.Count == 0) { CompleteActiveAction("search", true); return ToolCallResult.Ok("No matching run outputs found. Semantic search may be disabled (no embedding API key configured)."); }
            var sb = new System.Text.StringBuilder();
            sb.Append($"Matches for \"{query}\": {matches.Count}\n");
            foreach (var m in matches) { var snippet = m.Text.Length > 300 ? m.Text[..300] + "…" : m.Text; sb.Append($"- (score {m.Score:0.00}, run {m.JobRunId.ToString()[..8]}) {snippet}\n"); }
            CompleteActiveAction("search", true);
            AddToolHistory("search", true, $"{matches.Count} matches");
            return ToolCallResult.Ok(sb.ToString());
        }
        catch (Exception ex) { CompleteActiveAction("search", false); return ToolCallResult.Fail(ex.Message); }
    }

    private async Task<ToolCallResult> ExecuteGetArtifactsAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail("No project selected");
        var query = args.Trim();
        AddActiveAction("get_artifacts", string.IsNullOrEmpty(query) ? "Loading artifacts..." : $"Searching artifacts for \"{query}\"...");
        try
        {
            IReadOnlyList<ArtifactFileView> artifacts;
            if (string.IsNullOrEmpty(query))
            {
                artifacts = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 100, null, ct);
            }
            else
            {
                var terms = ArtifactSearchTerms(query);
                if (terms.Count == 0) artifacts = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 100, null, ct);
                else { var broad = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 2000, null, ct); artifacts = ScoreAndFilterArtifacts(broad, terms); }
            }
            if (artifacts.Count == 0) { CompleteActiveAction("get_artifacts", true); return ToolCallResult.Ok(string.IsNullOrEmpty(query) ? "No artifacts found for this project yet." : $"No artifacts matched \"{query}\"."); }
            MergePanelArtifacts(artifacts);
            if (!string.IsNullOrEmpty(query) && artifacts.Count == 1) { CompleteActiveAction("get_artifacts", true); return await ExecuteShowArtifactAsync(artifacts[0].Id.ToString(), ct); }
            var sb = new System.Text.StringBuilder();
            sb.Append($"Artifacts: {artifacts.Count}\n");
            foreach (var a in artifacts) sb.Append($"- {a.Title} | {a.Kind} | {Helpers.FormatHelper.Bytes(a.SizeBytes)} | {a.CreatedAt:yyyy-MM-dd HH:mm} | id:{a.Id} | /runs/{a.RunId}/artifacts/{a.Id}\n");
            sb.Append("\nTo display one, call [[tool:show_artifact|id]].");
            CompleteActiveAction("get_artifacts", true);
            AddToolHistory("get_artifacts", true, $"{artifacts.Count} artifacts");
            return ToolCallResult.Ok(sb.ToString());
        }
        catch (Exception ex) { CompleteActiveAction("get_artifacts", false); return ToolCallResult.Fail(ex.Message); }
    }

    private static IReadOnlyList<string> ArtifactSearchTerms(string query)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for", "with", "from", "is", "are", "was", "were", "me", "show", "give", "list", "report", "reports", "file", "files", "artifact", "artifacts", "find", "search", "get" };
        return query.Split(new[] { ' ', ',', '.', '-', '_', '/', '\\', '|', '&' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 2 && !stopWords.Contains(t)).Distinct().ToList();
    }

    private static IReadOnlyList<ArtifactFileView> ScoreAndFilterArtifacts(IReadOnlyList<ArtifactFileView> artifacts, IReadOnlyList<string> terms)
        => artifacts.Select(a => { var haystack = $"{a.Title} {a.Kind}".ToLowerInvariant(); return (Artifact: a, Score: terms.Count(t => haystack.Contains(t))); }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenByDescending(x => x.Artifact.CreatedAt).Take(25).Select(x => x.Artifact).ToList();

    internal async Task<ToolCallResult> ExecuteShowArtifactAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail("No project selected");
        var idStr = args.Trim().Split('|')[0].Trim();
        if (!Guid.TryParse(idStr, out var artifactId)) return ToolCallResult.Fail("Usage: [[tool:show_artifact|artifactId]]");
        AddActiveAction("show_artifact", $"Loading artifact {artifactId.ToString()[..8]}...");
        try
        {
            var link = await _links.GetByIdAsync(artifactId, ct);
            if (link is null) { CompleteActiveAction("show_artifact", false); return ToolCallResult.Fail("Artifact not found."); }
            var isTextLike = IsTextArtifactContentType(link.ContentType);
            var isDocument = IsDocumentContentType(link.ContentType);
            string? content = null, extractedText = null;
            var truncated = false; var extractedTruncated = false;
            if ((isTextLike || isDocument) && _objectStore.IsEnabled)
            {
                try
                {
                    var obj = await _objectStore.OpenReadAsync(link.Bucket, link.ObjectKey, ct);
                    if (obj is not null)
                    {
                        await using var stream = obj.Content;
                        using var ms = new MemoryStream();
                        var buffer = new byte[81920]; int read, total = 0;
                        var maxBytes = isDocument ? MaxArtifactDocumentBytes : MaxArtifactInlineBytes;
                        while ((read = await stream.ReadAsync(buffer, ct)) > 0) { total += read; if (total > maxBytes) { truncated = true; break; } ms.Write(buffer, 0, read); }
                        var bytes = ms.ToArray();
                        if (isDocument) { var rawText = _docExtractor.ExtractText(bytes, link.Title); if (!string.IsNullOrWhiteSpace(rawText)) { if (rawText.Length > MaxArtifactExtractedTextLength) { extractedText = rawText[..MaxArtifactExtractedTextLength]; extractedTruncated = true; } else extractedText = rawText; } }
                        else content = System.Text.Encoding.UTF8.GetString(bytes);
                    }
                }
                catch { }
            }
            if (_contentIndexer is { IsEnabled: true })
            {
                var indexText = extractedText ?? content;
                if (!string.IsNullOrWhiteSpace(indexText))
                { try { await _contentIndexer.IndexAsync(ProjectId!.Value, ContentKind.Document, $"artifact:{link.Id}", $"{link.Title}\n\n{indexText}", ct); } catch { } }
            }
            var payload = new { link.Id, link.RunId, link.Title, link.ContentType, link.SizeBytes, IsText = isTextLike, Content = content, ExtractedText = extractedText, ExtractedTruncated = extractedTruncated, Truncated = truncated };
            CompleteActiveAction("show_artifact", true);
            AddToolHistory("show_artifact", true, link.Title);
            return ToolCallResult.Artifact(System.Text.Json.JsonSerializer.Serialize(payload));
        }
        catch (Exception ex) { CompleteActiveAction("show_artifact", false); return ToolCallResult.Fail(ex.Message); }
    }

    private static bool IsTextArtifactContentType(string ct) => ct.StartsWith("text/") || ct.Contains("json", StringComparison.OrdinalIgnoreCase) || ct.Contains("csv", StringComparison.OrdinalIgnoreCase) || ct.Contains("html", StringComparison.OrdinalIgnoreCase) || ct.Contains("xml", StringComparison.OrdinalIgnoreCase) || ct.Contains("svg", StringComparison.OrdinalIgnoreCase);
    private static bool IsDocumentContentType(string ct) => ct == "application/pdf" || ct == "application/msword" || ct == "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private async Task<ToolCallResult> ExecuteRenderGraphAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail("No project selected");
        var parts = args.Split('|');
        var chartType = parts.Length > 0 ? parts[0].Trim() : "bar";
        var tableName = parts.Length > 1 ? parts[1].Trim() : "";
        var column = parts.Length > 2 ? parts[2].Trim() : "";
        var tableValid = false;
        if (!string.IsNullOrEmpty(tableName)) { try { var probe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 1, ct: ct); tableValid = probe.Columns.Count > 0; } catch { tableValid = false; } }
        if (!tableValid)
        {
            var tables = await _svc.ListProjectDataTablesAsync(ProjectId!.Value, ct);
            if (tables.Count == 0) return ToolCallResult.Fail("No data tables found in this project.");
            var clarify = await AskClarificationAsync(new ClarificationRequest { ToolName = "render_graph", Args = args, Question = $"Table '{tableName}' not found. Which table would you like to chart?", MultiSelect = false, Options = tables.Where(t => t.RowEstimate > 0).Select(t => new ClarificationOption { Id = t.Name, Label = t.Name, Description = $"~{t.RowEstimate} rows" }).ToList() });
            if (!clarify.Confirmed || clarify.SelectedIds.Count == 0) return ToolCallResult.Fail("Cancelled — no table selected.");
            tableName = clarify.SelectedIds[0];
        }
        var columns = new List<string>();
        if (!string.IsNullOrEmpty(column))
        { var probe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 1, ct: ct); if (probe.Columns.Any(c => c.Equals(column, StringComparison.OrdinalIgnoreCase))) columns.Add(column); }
        if (columns.Count == 0)
        {
            var probe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 1, ct: ct);
            if (probe.Columns.Count == 0) return ToolCallResult.Fail($"Table '{tableName}' has no columns.");
            var numericProbe = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 10, ct: ct);
            var numericCols = new List<string>();
            for (var i = 0; i < numericProbe.Columns.Count; i++) { if (numericProbe.Rows.Any(r => i < r.Count && double.TryParse(r[i]?.ToString(), out _))) numericCols.Add(numericProbe.Columns[i]); }
            var clarify = await AskClarificationAsync(new ClarificationRequest { ToolName = "render_graph", Args = args, Question = $"Which column(s) in '{tableName}' should be charted?", MultiSelect = true, Options = numericCols.Select(c => new ClarificationOption { Id = c, Label = c }).ToList() });
            if (!clarify.Confirmed || clarify.SelectedIds.Count == 0) return ToolCallResult.Fail("Cancelled — no column selected.");
            columns = clarify.SelectedIds;
        }
        var result = await _svc.QueryProjectTablePageAsync(ProjectId!.Value, tableName, null, 1, 100, ct: ct);
        var labels = new List<string>(); var seriesList = new List<(string Name, List<double> Values)>();
        foreach (var col in columns) { var colIndex = result.Columns.ToList().FindIndex(c => c.Equals(col, StringComparison.OrdinalIgnoreCase)); if (colIndex >= 0) seriesList.Add((col, new List<double>())); }
        if (seriesList.Count == 0) return ToolCallResult.Fail($"None of the selected columns found in '{tableName}'.");
        foreach (var row in result.Rows) { var label = row[0]?.ToString() ?? ""; labels.Add(label); for (var s = 0; s < seriesList.Count; s++) { var colIndex = result.Columns.ToList().FindIndex(c => c.Equals(columns[s], StringComparison.OrdinalIgnoreCase)); var valStr = colIndex >= 0 && colIndex < row.Count ? row[colIndex]?.ToString() ?? "0" : "0"; seriesList[s].Values.Add(double.TryParse(valStr, out var val) ? val : 0); } }
        var graphData = new { type = chartType, title = $"{tableName}" + (columns.Count > 1 ? $" — {string.Join(", ", columns)}" : $" — {columns[0]}"), labels = labels.Take(24).ToList(), series = seriesList.Select(s => new { name = s.Name, values = s.Values.Take(24).ToList() }).ToList() };
        return ToolCallResult.Graph(System.Text.Json.JsonSerializer.Serialize(graphData));
    }

    private async Task<ToolCallResult> ExecuteQueryGraphAsync(CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail("No project selected");
        AddActiveAction("query_graph", "Loading project graph...");
        try
        {
            var graph = await _svc.GetGraphVizAsync(ProjectId!.Value, ct);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Project Dependency Graph\n- **Nodes:** {graph.NodeCount}\n- **Links:** {graph.LinkCount}\n");
            var byKind = graph.Nodes.GroupBy(n => n.Kind ?? "unknown").OrderByDescending(g => g.Count()).ToList();
            sb.AppendLine("## Entity types");
            foreach (var kind in byKind.Take(10)) sb.AppendLine($"- **{kind.Key}:** {kind.Count()} nodes");
            sb.AppendLine();
            var hubs = graph.Nodes.Where(n => n.Degree >= 5).OrderByDescending(n => n.Degree).Take(10).ToList();
            if (hubs.Count > 0) { sb.AppendLine("## Key entities (hubs)"); foreach (var n in hubs) sb.AppendLine($"- **{n.Label}** ({n.Degree} connections){(n.IsGod ? " ⭐" : "")}"); sb.AppendLine(); }
            GraphNodes.Clear(); GraphNodes.AddRange(graph.Nodes.OrderByDescending(n => n.Degree).Take(50)); GraphLinks = graph.LinkCount;
            var nodeLabels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
            if (graph.Links.Count > 0) { sb.AppendLine("\n**All relationships:**"); foreach (var edge in graph.Links) sb.AppendLine($"- {nodeLabels.GetValueOrDefault(edge.Source) ?? "?"} → {nodeLabels.GetValueOrDefault(edge.Target) ?? "?"} ({edge.Confidence})"); }
            CompleteActiveAction("query_graph", true); AddToolHistory("query_graph", true, $"{graph.NodeCount} nodes");
            return ToolCallResult.Ok(sb.ToString());
        }
        catch (Exception ex) { CompleteActiveAction("query_graph", false); AddToolHistory("query_graph", false, ex.Message); return ToolCallResult.Fail($"Graph query failed: {ex.Message}"); }
    }

    private async Task<ToolCallResult> ExecuteRenderMapAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args)) return ToolCallResult.Fail("Usage: render_map|specJson");
        try { System.Text.Json.JsonDocument.Parse(args.Trim()); } catch { return ToolCallResult.Fail("Invalid JSON spec for render_map"); }
        AddActiveAction("render_map", "Rendering map...");
        await Task.Delay(10, ct);
        CompleteActiveAction("render_map", true); AddToolHistory("render_map", true, "Map rendered");
        return ToolCallResult.Map(args.Trim());
    }

    private async Task<ToolCallResult> ExecuteScheduleJobAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        if (parts.Length < 3) return ToolCallResult.Fail("Usage: schedule_job|jobId|name|cron");
        var jobId = Guid.TryParse(parts[0].Trim(), out var id) ? id : Guid.Empty;
        if (jobId == Guid.Empty) return ToolCallResult.Fail("Invalid jobId");
        AddActiveAction("schedule_job", $"Creating schedule for job {jobId.ToString()[..8]}...");
        var trigger = await _svc.CreateTriggerAsync(new CreateTriggerCommand(jobId, parts[1].Trim(), "Schedule", parts[2].Trim(), null), ct);
        CompleteActiveAction("schedule_job", true); AddToolHistory("schedule_job", true, $"Next: {trigger.NextRunAt?.ToString("HH:mm") ?? "—"}");
        return ToolCallResult.Ok($"Schedule created: {trigger.Name} (id: {trigger.Id})\nCron: {trigger.CronExpression}\nNext run: {trigger.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}\nEnabled: {trigger.Enabled}");
    }

    private async Task<ToolCallResult> ExecuteListSchedulesAsync(string args, CancellationToken ct)
    {
        if (!ProjectId.HasValue) return ToolCallResult.Fail("No project selected");
        var jobId = Guid.TryParse(args.Trim(), out var id) ? id : Guid.Empty;
        var triggers = await _svc.ListTriggersAsync(ProjectId!.Value, ct);
        if (jobId != Guid.Empty) triggers = triggers.Where(t => t.JobId == jobId).ToList();
        var sb = new System.Text.StringBuilder(); sb.Append($"Schedules: {triggers.Count}\n");
        foreach (var t in triggers) { sb.Append($"- {t.Name} ({t.Kind})\n"); if (t.Kind == "Schedule") sb.Append($"  Cron: {t.CronExpression} | Next: {t.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}\n"); sb.Append($"  Enabled: {t.Enabled} | Last fired: {t.LastFiredAt?.ToString("yyyy-MM-dd HH:mm") ?? "never"}\n"); }
        return ToolCallResult.Ok(sb.ToString());
    }

    private async Task<ToolCallResult> ExecuteToggleScheduleAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        if (parts.Length < 2) return ToolCallResult.Fail("Usage: toggle_schedule|triggerId|true|false");
        var triggerId = Guid.TryParse(parts[0].Trim(), out var id) ? id : Guid.Empty;
        if (triggerId == Guid.Empty) return ToolCallResult.Fail("Invalid triggerId");
        var enabled = parts[1].Trim().ToLower() == "true";
        AddActiveAction("toggle_schedule", $"Toggling schedule {triggerId.ToString()[..8]}...");
        var trigger = await _svc.SetTriggerEnabledAsync(triggerId, enabled, ct);
        CompleteActiveAction("toggle_schedule", true); AddToolHistory("toggle_schedule", true, enabled ? "enabled" : "disabled");
        return ToolCallResult.Ok($"Schedule '{trigger.Name}' {(enabled ? "enabled" : "disabled")}.\nNext run: {trigger.NextRunAt?.ToString("yyyy-MM-dd HH:mm") ?? "—"}");
    }

    private async Task<ToolCallResult> ExecuteRunJobAsync(string args, CancellationToken ct)
    {
        var jobId = Guid.TryParse(args.Trim(), out var id) ? id : Guid.Empty;
        if (jobId == Guid.Empty) return ToolCallResult.Fail("Invalid jobId");
        AddActiveAction("run_job", $"Running job {jobId.ToString()[..8]}...");
        var run = await _svc.RunJobAsync(jobId, null, null, ct);
        CompleteActiveAction("run_job", true); AddToolHistory("run_job", true, run.Status);
        return ToolCallResult.Ok($"Job run started: {run.Id}\nStatus: {run.Status}\nStarted: {run.StartedAt:yyyy-MM-dd HH:mm}");
    }

    private async Task<ToolCallResult> ExecuteCallMcpAsync(string args, CancellationToken ct)
    {
        var parts = args.Split('|');
        if (parts.Length < 2) return ToolCallResult.Fail("Usage: call_mcp|serverName|toolName|[jsonArgs]");
        var serverName = parts[0].Trim(); var toolName = parts[1].Trim(); var jsonArgs = parts.Length > 2 ? parts[2].Trim() : "{}";
        AddActiveAction("call_mcp", $"Calling {serverName}.{toolName}...");
        var connections = await _svc.ListMcpConnectionsAsync(ProjectId!.Value, ct);
        var connection = connections.FirstOrDefault(c => c.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
        if (connection == null) { CompleteActiveAction("call_mcp", false); return ToolCallResult.Fail($"MCP server '{serverName}' not found. Available: {string.Join(", ", connections.Select(c => c.Name))}"); }
        try
        {
            var arguments = System.Text.Json.JsonDocument.Parse(jsonArgs).RootElement;
            var result = await _mcpClient.CallToolAsync(connection.Id, toolName, arguments, ct);
            CompleteActiveAction("call_mcp", result.Success); AddToolHistory("call_mcp", result.Success, result.Success ? "ok" : result.Error ?? "error");
            return result.Success ? ToolCallResult.Ok(result.Content ?? "Tool executed successfully") : ToolCallResult.Fail($"MCP tool error: {result.Error}");
        }
        catch (Exception ex) { CompleteActiveAction("call_mcp", false); return ToolCallResult.Fail($"MCP call failed: {ex.Message}"); }
    }

    private async Task<ToolCallResult> ExecuteListMcpToolsAsync(string args, CancellationToken ct)
    {
        var serverName = args.Trim();
        var connections = await _svc.ListMcpConnectionsAsync(ProjectId!.Value, ct);
        if (connections.Count == 0) return ToolCallResult.Ok("No MCP servers configured. Add one in Settings → MCP Servers.");
        var sb = new System.Text.StringBuilder();
        if (string.IsNullOrEmpty(serverName))
        {
            sb.AppendLine("Available MCP servers:");
            foreach (var conn in connections) sb.AppendLine($"- {conn.Name} ({conn.Transport}) - {(conn.Enabled ? "enabled" : "disabled")}");
            sb.AppendLine("\nUse list_mcp_tools|serverName to see available tools.");
        }
        else
        {
            var connection = connections.FirstOrDefault(c => c.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
            if (connection == null) return ToolCallResult.Fail($"MCP server '{serverName}' not found.");
            var tools = await _mcpClient.ListToolsAsync(connection.Id, ct);
            sb.AppendLine($"Tools on {serverName}:");
            foreach (var tool in tools) sb.AppendLine($"- {tool.Name}: {tool.Description ?? "No description"}");
        }
        return ToolCallResult.Ok(sb.ToString());
    }

    public static List<string> ParseNumberedOptions(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new();
        var matches = System.Text.RegularExpressions.Regex.Matches(content, @"^\s*\d+[.)]\s+(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
        if (matches.Count < 2) return new();
        return matches.Select(m => m.Groups[1].Value.Trim()).ToList();
    }
}

using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Chat;
using PlaceContext.Infrastructure.Caching;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel : PageViewModel
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
    public string NewMcpTransport { get; set; } = McpTransport.Http;
    public string NewMcpEndpoint { get; set; } = "";
    public string NewMcpCommand { get; set; } = "";
    public string NewMcpArgs { get; set; } = "";
    public string NewMcpAuthType { get; set; } = McpAuthType.None;
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

    private string _systemPrompt = ChatCopy.DefaultSystemPrompt;
    private bool _ragEnabled = true;
    private int _maxContextChunks = 5;
    private float _temperature = 0.7f;
    private int _maxTokens = 2048;
    private int _toolCallCounter;
    private string _sessionTitle = ChatCopy.DefaultSessionTitle;
    private Guid? _sessionId;
    public Guid? SessionId => _sessionId;
    private TaskCompletionSource<ClarificationResult>? _clarificationTcs;
    private bool _attachmentsBucketEnsured;

    private const string AttachmentsBucket = ChatCopy.AttachmentsBucket;
    private const int MaxArtifactInlineBytes = 512 * 1024;
    private const int MaxArtifactDocumentBytes = 10 * 1024 * 1024;
    private const int MaxArtifactExtractedTextLength = 200_000;

    // ── Gateway status ───────────────────────────────────────────────────────

    public string GatewayStatusText =>
        _gateway is ClusterChatGateway cg ? cg.StatusText :
        _gateway.IsEnabled ? ChatCopy.GatewayActive :
        ChatCopy.GatewayUnconfigured;

    public bool GatewayIsCluster => _gateway is ClusterChatGateway;

    public bool GatewayReady => _gateway is ClusterChatGateway cg ? cg.IsEnabled : _gateway.IsEnabled;

    /// <summary>True once sessions, MCP, artifacts, and agent config have finished loading.</summary>
    public bool WorkspaceLoaded { get; private set; }

    public IChatGateway Gateway => _gateway;

}

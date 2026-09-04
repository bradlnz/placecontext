using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Host.Components.ViewModels;

public enum GraphNodeKind
{
    Unknown,
    Artifact,
}

public enum GraphLinkConfidence
{
    Normal,
    Ambiguous,
}

public sealed class GraphCanvasViewModel(IJSRuntime js, PlaceContextService? service = null)
    : PageViewModel,
        IComponentViewModel,
        IAsyncDisposable
{
    private const int InitialNodeChunk = 2500;
    private const int NodeChunk = 2500;
    private const int MaxOpenPanels = 8;

    private readonly IJSRuntime _js = js;
    private DotNetObjectReference<GraphCanvasViewModel>? _selfReference;
    private GraphVizView? _graph;
    private Func<GraphNodeView, string?>? _nodeUrl;
    private Func<string?, Task>? _nodeClick;
    private string _search = string.Empty;
    private string? _renderedGraphKey;
    private GraphVizView? _graphForRender;
    private Dictionary<string, int> _nodeIndex = [];
    private HashSet<string> _sentLinkKeys = [];
    private int _renderedNodeCount;
    private readonly Dictionary<string, JobRunDetailView> _runDetails = [];
    private readonly Dictionary<string, string> _runDetailErrors = [];
    private readonly HashSet<string> _runDetailRequests = [];
    private readonly Dictionary<string, PanelPosition> _panelPositions = [];
    private string? _draggingPanelId;
    private double _dragStartX;
    private double _dragStartY;
    private double _windowStartLeft;
    private double _windowStartTop;
    public string Id { get; } = "pcgraph-" + Guid.NewGuid().ToString("N");
    public GraphVizView? Graph => _graph;
    public int Height { get; private set; } = 340;
    public Func<GraphNodeView, string?>? NodeUrl => _nodeUrl;
    public bool Searchable { get; private set; }
    public bool AllowFullscreen { get; private set; }
    public bool DockDetails { get; private set; }
    public bool SinglePanel { get; private set; }
    public bool Fullscreen { get; private set; }
    public bool IsPanelDragging => _draggingPanelId is not null;
    public string SearchTerm => _search;
    public GraphNodeView? Selected { get; private set; }
    public IReadOnlyList<GraphNodeView> OpenPanels { get; private set; } = [];
    public IReadOnlyList<GraphNodeView> Matches { get; private set; } =
        Array.Empty<GraphNodeView>();
    public IReadOnlyList<(GraphNodeView Node, GraphLinkConfidence Confidence)> SelectedNeighbors
    {
        get;
        private set;
    } = [];
    public IReadOnlyList<(GraphNodeView Node, int Hops)> ReachableArtifacts { get; private set; } =
    [];
    public string ContainerStyle =>
        Fullscreen
            ? DockDetails
                ? "position:fixed; inset:0; z-index:200; background:var(--bg); padding:14px; display:grid"
                : "position:fixed; inset:0; z-index:200; background:var(--bg); padding:14px; display:flex; flex-direction:column"
            : "position:relative";
    public string ChromeStyle =>
        Fullscreen ? string.Empty : "position:absolute; top:8px; left:10px; right:10px; z-index:5";
    public string CanvasStyle => Fullscreen
        ? DockDetails ? "min-height:0; height:100%" : "flex:1; min-height:0"
        : $"height:{Height}px";
    public string DetailStyle => $"max-height:{Height - 20}px";
    public string FullscreenLabel => Fullscreen ? "✕ Exit full screen" : "⛶ Full screen";
    public bool HasMoreGraphNodes => _graph is not null && _renderedNodeCount < _graph.Nodes.Count;

    public string ShortLabel(string label)
    {
        var index = label.LastIndexOf('/');
        var name = index >= 0 ? label[(index + 1)..] : label;
        return name.Length > 40 ? name[..39] + "…" : name;
    }

    public void SetParameters(
        GraphVizView? graph,
        int height,
        Func<GraphNodeView, string?>? nodeUrl,
        bool searchable,
        bool allowFullscreen,
        Func<string?, Task>? nodeClick = null,
        bool dockDetails = false,
        bool singlePanel = false
    )
    {
        if (!ReferenceEquals(_graph, graph))
        {
            _graph = graph;
            Selected = null;
            Matches = [];
            _graphForRender = null;
            _renderedGraphKey = null;
            _nodeIndex = [];
            _sentLinkKeys = [];
            _renderedNodeCount = 0;
            OpenPanels = [];
            _runDetails.Clear();
            _runDetailErrors.Clear();
            _runDetailRequests.Clear();
            _panelPositions.Clear();
            _draggingPanelId = null;
            if (_graph is not null)
                for (var i = 0; i < _graph.Nodes.Count; i++)
                    _nodeIndex[_graph.Nodes[i].Id] = i;
        }
        Height = height;
        _nodeUrl = nodeUrl;
        Searchable = searchable;
        AllowFullscreen = allowFullscreen;
        DockDetails = dockDetails;
        SinglePanel = singlePanel;
        _nodeClick = nodeClick;
    }

    public async Task LoadMoreAsync()
    {
        if (_graph is null || _renderedNodeCount >= _graph.Nodes.Count)
            return;
        var nextCount = Math.Min(_graph.Nodes.Count, _renderedNodeCount + NodeChunk);
        await LoadToNodeCountAsync(nextCount);
    }

    public async Task EnsureNodeVisibleAsync(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || _graph is null)
            return;
        if (!_nodeIndex.TryGetValue(nodeId, out var targetIndex))
            return;
        if (targetIndex < _renderedNodeCount)
            return;
        await LoadToNodeCountAsync(targetIndex + 1);
    }

    private async Task LoadToNodeCountAsync(int targetNodeCount)
    {
        if (_graph is null)
            return;

        var nextCount = Math.Min(_graph.Nodes.Count, targetNodeCount);
        if (nextCount <= _renderedNodeCount)
            return;

        var payload = BuildGraphPayload(nextCount);
        if (payload.Nodes.Count == 0)
            return;
        await _js.InvokeVoidAsync("pcgraph.append", Id, BuildPayloadForCall(payload));
    }

    private GraphPayload BuildGraphPayload(int? targetNodeCount = null)
    {
        if (_graph is null)
            return GraphPayload.Empty;

        var requestedCount = targetNodeCount ?? Math.Min(InitialNodeChunk, _graph.Nodes.Count);
        var targetCount = Math.Max(requestedCount, _renderedNodeCount + 1);
        if (targetCount > _graph.Nodes.Count)
            targetCount = _graph.Nodes.Count;

        var nodesToRender = new List<GraphNodeView>(_graph.Nodes.Take(targetCount).Skip(_renderedNodeCount));
        if (nodesToRender.Count == 0)
            return GraphPayload.Empty;

        var visibleIds = new HashSet<string>(_graph.Nodes.Take(targetCount).Select(n => n.Id), StringComparer.Ordinal);
        var linksToRender = new List<GraphLinkView>();
        foreach (var link in _graph.Links)
        {
            if (linksToRender.Count > 50000) break;
            if (!visibleIds.Contains(link.Source) || !visibleIds.Contains(link.Target))
                continue;
            var key = BuildLinkKey(link.Source, link.Target);
            if (_sentLinkKeys.Add(key))
                linksToRender.Add(link);
        }

        _renderedNodeCount = targetCount;

        return new GraphPayload(
            nodesToRender,
            linksToRender,
            _graph.Nodes.Count,
            _graph.Links.Count
        );
    }

    private static string BuildLinkKey(string source, string target)
    {
        return string.CompareOrdinal(source, target) < 0
            ? $"{source}:{target}"
            : $"{target}:{source}";
    }

    private sealed record GraphPayload(
        IReadOnlyList<GraphNodeView> Nodes,
        IReadOnlyList<GraphLinkView> Links,
        int TotalNodes,
        int TotalLinks
    )
    {
        public static readonly GraphPayload Empty = new(Array.Empty<GraphNodeView>(), Array.Empty<GraphLinkView>(), 0, 0);
    }

    private static object BuildNodePayload(GraphNodeView n)
    {
        return new
        {
            id = n.Id,
            label = n.Label,
            degree = n.Degree,
            god = n.IsGod,
            kind = n.Kind,
            labeled = n.Labeled,
        };
    }

    private static object BuildLinkPayload(GraphLinkView l)
    {
        return new
        {
            source = l.Source,
            target = l.Target,
            confidence = l.Confidence,
        };
    }

    private static string BuildGraphRenderKey(GraphVizView graph)
    {
        if (graph.Nodes.Count == 0)
            return $"{graph.ProjectId}:0:0";

        var first = graph.Nodes.First();
        var last = graph.Nodes[^1];
        return
            $"{graph.ProjectId}|{graph.Nodes.Count}|{graph.Links.Count}|{first.Id}|{first.Label}|{last.Id}|{last.Label}";
    }

    [JSInvokable]
    public async Task OnNodeClick(string? nodeId)
    {
        var selected = nodeId is null
            ? null
            : _graph?.Nodes.FirstOrDefault(node => node.Id == nodeId);
        Selected = selected;
        ReachableArtifacts =
            Selected is null || GraphCatalog.NodeKind(Selected.Kind) == GraphNodeKind.Artifact
                ? []
                : WalkToArtifacts(Selected.Id);
        SelectedNeighbors = Selected is null ? [] : NeighborsFor(Selected);
        if (selected is null && SinglePanel)
        {
            OpenPanels = [];
        }
        else if (selected is not null)
        {
            OpenPanels = SinglePanel
                ? [selected]
                : OpenPanels
                    .Where(node => node.Id != selected.Id)
                    .Append(selected)
                    .TakeLast(MaxOpenPanels)
                    .ToList();
        }
        NotifyStateChanged();
        if (_nodeClick is not null)
            await _nodeClick(nodeId);
        if (selected is not null)
            await LoadRunDetailsAsync(selected);
    }

    public IReadOnlyList<(GraphNodeView Node, GraphLinkConfidence Confidence)> NeighborsFor(
        GraphNodeView selected
    ) =>
        _graph is null
            ? []
            : _graph
                .Links.Where(link => link.Source == selected.Id || link.Target == selected.Id)
                .Select(link =>
                    (
                        Other: link.Source == selected.Id ? link.Target : link.Source,
                        Confidence: GraphCatalog.LinkConfidence(link.Confidence)
                    )
                )
                .Select(item =>
                    (
                        Node: _graph.Nodes.FirstOrDefault(node => node.Id == item.Other),
                        item.Confidence
                    )
                )
                .Where(item => item.Node is not null)
                .Select(item => (item.Node!, item.Confidence))
                .DistinctBy(item => item.Item1.Id)
                .OrderByDescending(item => IsArtifact(item.Item1))
                .ThenByDescending(item => item.Item1.Degree)
                .ToList();

    public IReadOnlyList<(GraphNodeView Node, int Hops)> ReachableArtifactsFor(
        GraphNodeView selected
    ) => IsArtifact(selected) ? [] : WalkToArtifacts(selected.Id);

    public GraphNodeArtifactRef? FirstPreviewableArtifactFor(GraphNodeView selected) =>
        ReachableArtifactsFor(selected)
            .Select(item => item.Node.Artifact)
            .FirstOrDefault(artifact => artifact is not null && IsPreviewable(artifact));

    public bool IsJobRun(GraphNodeView node) =>
        string.Equals(node.Kind, "JobRun", StringComparison.OrdinalIgnoreCase);

    public JobRunDetailView? RunDetailsFor(GraphNodeView node) =>
        _runDetails.GetValueOrDefault(node.Id);

    public string? RunDetailErrorFor(GraphNodeView node) =>
        _runDetailErrors.GetValueOrDefault(node.Id);

    public bool RunDetailsLoading(GraphNodeView node) =>
        IsJobRun(node)
        && _runDetailRequests.Contains(node.Id)
        && !_runDetails.ContainsKey(node.Id)
        && !_runDetailErrors.ContainsKey(node.Id);

    public async Task ClosePanelAsync(string nodeId)
    {
        if (_draggingPanelId == nodeId)
            _draggingPanelId = null;
        _panelPositions.Remove(nodeId);
        OpenPanels = OpenPanels.Where(node => node.Id != nodeId).ToList();
        _runDetails.Remove(nodeId);
        _runDetailErrors.Remove(nodeId);
        _runDetailRequests.Remove(nodeId);
        if (Selected?.Id == nodeId)
        {
            Selected = null;
            SelectedNeighbors = [];
            ReachableArtifacts = [];
            NotifyStateChanged();
            await SelectAsync(null);
            return;
        }
        NotifyStateChanged();
    }

    public string PanelStyle(string nodeId)
    {
        var position = EnsurePanelPosition(nodeId);
        var zIndex = 20 + Math.Max(0, OpenPanels.ToList().FindIndex(node => node.Id == nodeId));
        return FormattableString.Invariant(
            $"top:{position.Top:0.##}px; left:{position.Left:0.##}px; z-index:{zIndex}; max-height:{Math.Max(Height - 20, 220)}px;"
        );
    }

    public string DetailPanelStyle(string nodeId) =>
        DockDetails
            ? Fullscreen
                ? "height:100%; max-height:none;"
                : $"height:{Height}px; max-height:{Height}px;"
            : PanelStyle(nodeId);

    public void StartPanelDrag(string nodeId, double clientX, double clientY)
    {
        var position = EnsurePanelPosition(nodeId);
        BringPanelToFront(nodeId);
        _draggingPanelId = nodeId;
        _dragStartX = clientX;
        _dragStartY = clientY;
        _windowStartLeft = position.Left;
        _windowStartTop = position.Top;
    }

    public void MovePanel(double clientX, double clientY)
    {
        if (_draggingPanelId is null)
            return;

        _panelPositions[_draggingPanelId] = new PanelPosition(
            Math.Max(0, _windowStartLeft + clientX - _dragStartX),
            Math.Max(0, _windowStartTop + clientY - _dragStartY)
        );
        NotifyStateChanged();
    }

    public void StopPanelDrag() => _draggingPanelId = null;

    public void PrunePanelPositions()
    {
        var openIds = OpenPanels.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var nodeId in _panelPositions.Keys.Where(nodeId => !openIds.Contains(nodeId)).ToList())
            _panelPositions.Remove(nodeId);
        if (_draggingPanelId is not null && !openIds.Contains(_draggingPanelId))
            _draggingPanelId = null;
    }

    private PanelPosition EnsurePanelPosition(string nodeId)
    {
        if (_panelPositions.TryGetValue(nodeId, out var position))
            return position;

        var offset = _panelPositions.Count % 7 * 26;
        position = new PanelPosition(14 + offset, 14 + offset);
        _panelPositions[nodeId] = position;
        return position;
    }

    public void BringPanelToFront(string nodeId)
    {
        var panel = OpenPanels.FirstOrDefault(node => node.Id == nodeId);
        if (panel is null || OpenPanels.LastOrDefault()?.Id == nodeId)
            return;
        OpenPanels = OpenPanels.Where(node => node.Id != nodeId).Append(panel).ToList();
        NotifyStateChanged();
    }

    private async Task LoadRunDetailsAsync(GraphNodeView node)
    {
        if (!IsJobRun(node) || !_runDetailRequests.Add(node.Id))
            return;

        const string prefix = "run:";
        if (service is null
            || !node.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(node.Id[prefix.Length..], out var runId))
        {
            _runDetailErrors[node.Id] = "Run details are unavailable for this graph node.";
            NotifyStateChanged();
            return;
        }

        try
        {
            var detail = await service.GetJobRunAsync(runId);
            if (detail is null)
                _runDetailErrors[node.Id] = "This run could not be found.";
            else
                _runDetails[node.Id] = detail;
        }
        catch (Exception ex)
        {
            _runDetailErrors[node.Id] = ex.Message;
        }
        NotifyStateChanged();
    }

    public void SearchNodes(string? input)
    {
        _search = input ?? string.Empty;
        var term = _search.Trim();
        Matches =
            term.Length < 2 || _graph is null
                ? []
                : _graph
                    .Nodes.Where(node =>
                        node.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || node.Content?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
                    )
                    .OrderByDescending(node =>
                        node.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                    )
                    .ThenByDescending(node => node.Degree)
                    .Take(8)
                    .ToList();
        NotifyStateChanged();
    }

    public bool IsArtifact(GraphNodeView node) =>
        GraphCatalog.NodeKind(node.Kind) == GraphNodeKind.Artifact;

    public bool IsAmbiguous(GraphLinkConfidence confidence) =>
        confidence == GraphLinkConfidence.Ambiguous;

    public bool IsFailed(string? outcome) =>
        ScopedPresentationCatalog.JobStatus(outcome) == JobRunStatus.Failed;

    public string PreviewUrl(GraphNodeArtifactRef artifact) =>
        $"/runs/{artifact.RunId}/artifacts/{artifact.Id}?v={artifact.CreatedAt.ToUnixTimeSeconds()}";

    public bool IsImage(GraphNodeArtifactRef artifact)
    {
        if (artifact.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;
        var extension = Path.GetExtension(artifact.Title);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".avif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsPdf(GraphNodeArtifactRef artifact) =>
        artifact.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);

    public bool IsPreviewable(GraphNodeArtifactRef artifact) =>
        artifact.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || artifact.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        || artifact.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
        || artifact.ContentType == "application/pdf"
        || artifact.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    public GraphNodeArtifactRef? FirstPreviewableArtifact =>
        Selected is null ? null : FirstPreviewableArtifactFor(Selected);

    public async Task JumpAsync(string nodeId)
    {
        Matches = [];
        _search = string.Empty;
        await SelectAsync(nodeId);
    }

    public async Task ToggleFullscreenAsync()
    {
        Fullscreen = !Fullscreen;
        await Task.Yield();
        if (Selected is not null)
            await SelectAsync(Selected.Id);
    }

    public Task ClearAsync() =>
        Selected is null ? Task.CompletedTask : ClosePanelAsync(Selected.Id);

    public async Task SelectAsync(string? nodeId)
    {
        await EnsureNodeVisibleAsync(nodeId);
        await _js.InvokeVoidAsync("pcgraph.select", Id, nodeId);
    }

    private object BuildPayloadForCall(GraphPayload payload) =>
        new
        {
            graphKey = _renderedGraphKey,
            totalNodes = payload.TotalNodes,
            totalLinks = payload.TotalLinks,
            nodes = payload.Nodes.Select(BuildNodePayload),
            links = payload.Links.Select(BuildLinkPayload),
        };

    public async Task AfterRenderAsync()
    {
        if (_graph is null || _graph.Nodes.Count == 0)
            return;

        var graphKey = BuildGraphRenderKey(_graph);
        if (_renderedGraphKey != graphKey || !ReferenceEquals(_graphForRender, _graph) || _renderedNodeCount == 0)
        {
            _selfReference ??= DotNetObjectReference.Create(this);
            var payload = BuildGraphPayload();
            if (payload.Nodes.Count > 0)
            {
                _graphForRender = _graph;
                _renderedGraphKey = graphKey;
                await _js.InvokeVoidAsync("pcgraph.init", Id, BuildPayloadForCall(payload), _selfReference);
            }
        }

        if (DockDetails && OpenPanels.Count > 0)
        {
            await _js.InvokeVoidAsync(
                "pcgraph.splitter",
                $"{Id}-detail-splitter",
                new
                {
                    cssProperty = "--graph-detail-width",
                    side = "next",
                    defaultWidth = 380,
                    min = 300,
                    max = 620,
                    reserve = 360,
                }
            );
        }
    }

    private GraphPayload BuildGraphPayload()
    {
        return BuildGraphPayload(InitialNodeChunk);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("pcgraph.destroy", Id);
        }
        catch { }
        _selfReference?.Dispose();
        Detach();
    }

    private IReadOnlyList<(GraphNodeView Node, int Hops)> WalkToArtifacts(string fromId)
    {
        if (_graph is null)
            return [];
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var link in _graph.Links)
        {
            (
                adjacency.TryGetValue(link.Source, out var source)
                    ? source
                    : adjacency[link.Source] = []
            ).Add(link.Target);
            (
                adjacency.TryGetValue(link.Target, out var target)
                    ? target
                    : adjacency[link.Target] = []
            ).Add(link.Source);
        }
        var byId = _graph.Nodes.ToDictionary(node => node.Id);
        var found = new List<(GraphNodeView, int)>();
        var visited = new HashSet<string> { fromId };
        var frontier = new Queue<(string Id, int Hops)>([(fromId, 0)]);
        while (frontier.Count > 0 && found.Count < 10)
        {
            var (id, hops) = frontier.Dequeue();
            if (hops >= 4)
                continue;
            foreach (var next in adjacency.GetValueOrDefault(id) ?? [])
            {
                if (!visited.Add(next))
                    continue;
                if (
                    byId.TryGetValue(next, out var node)
                    && GraphCatalog.NodeKind(node.Kind) == GraphNodeKind.Artifact
                )
                    found.Add((node, hops + 1));
                frontier.Enqueue((next, hops + 1));
            }
        }
        return found.OrderBy(item => item.Item2).ToList();
    }

    private sealed record PanelPosition(double Left, double Top);
}

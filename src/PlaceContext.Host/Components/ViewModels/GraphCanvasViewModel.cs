using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;

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

public static class GraphCatalog
{
    public static GraphNodeKind NodeKind(string? value) =>
        string.Equals(value, "good", StringComparison.OrdinalIgnoreCase)
            ? GraphNodeKind.Artifact
            : GraphNodeKind.Unknown;

    public static GraphLinkConfidence LinkConfidence(string? value) =>
        string.Equals(value, "Ambiguous", StringComparison.OrdinalIgnoreCase)
            ? GraphLinkConfidence.Ambiguous
            : GraphLinkConfidence.Normal;
}

public sealed class GraphCanvasViewModel(IJSRuntime js)
    : PageViewModel,
        IComponentViewModel,
        IAsyncDisposable
{
    private readonly IJSRuntime _js = js;
    private DotNetObjectReference<GraphCanvasViewModel>? _selfReference;
    private GraphVizView? _graph;
    private Func<GraphNodeView, string?>? _nodeUrl;
    private Func<string?, Task>? _nodeClick;
    private string _search = string.Empty;
    public string Id { get; } = "pcgraph-" + Guid.NewGuid().ToString("N");
    public GraphVizView? Graph => _graph;
    public int Height { get; private set; } = 340;
    public Func<GraphNodeView, string?>? NodeUrl => _nodeUrl;
    public bool Searchable { get; private set; }
    public bool AllowFullscreen { get; private set; }
    public bool Fullscreen { get; private set; }
    public string SearchTerm => _search;
    public GraphNodeView? Selected { get; private set; }
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
            ? "position:fixed; inset:0; z-index:200; background:var(--bg); padding:14px; display:flex; flex-direction:column"
            : "position:relative";
    public string ChromeStyle =>
        Fullscreen ? string.Empty : "position:absolute; top:8px; left:10px; right:10px; z-index:5";
    public string CanvasStyle => Fullscreen ? "flex:1; min-height:0" : $"height:{Height}px";
    public string DetailStyle => $"max-height:{Height - 20}px";
    public string FullscreenLabel => Fullscreen ? "✕ Exit full screen" : "⛶ Full screen";

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
        Func<string?, Task>? nodeClick = null
    )
    {
        if (!ReferenceEquals(_graph, graph))
        {
            _graph = graph;
            Selected = null;
            Matches = [];
        }
        Height = height;
        _nodeUrl = nodeUrl;
        Searchable = searchable;
        AllowFullscreen = allowFullscreen;
        _nodeClick = nodeClick;
    }

    public async Task AfterRenderAsync()
    {
        if (_graph is null || _graph.Nodes.Count == 0)
            return;
        _selfReference ??= DotNetObjectReference.Create(this);
        var payload = new
        {
            nodes = _graph
                .Nodes.Take(180)
                .Select(node => new
                {
                    id = node.Id,
                    label = node.Label,
                    degree = node.Degree,
                    god = node.IsGod,
                    kind = node.Kind,
                    labeled = node.Labeled,
                }),
            links = _graph
                .Links.Take(500)
                .Select(link => new
                {
                    source = link.Source,
                    target = link.Target,
                    confidence = link.Confidence,
                }),
        };
        await _js.InvokeVoidAsync("pcgraph.init", Id, payload, _selfReference);
    }

    [JSInvokable]
    public async Task OnNodeClick(string? nodeId)
    {
        Selected = nodeId is null ? null : _graph?.Nodes.FirstOrDefault(node => node.Id == nodeId);
        ReachableArtifacts =
            Selected is null || GraphCatalog.NodeKind(Selected.Kind) == GraphNodeKind.Artifact
                ? []
                : WalkToArtifacts(Selected.Id);
        SelectedNeighbors =
            Selected is null || _graph is null
                ? []
                : _graph
                    .Links.Where(link => link.Source == Selected.Id || link.Target == Selected.Id)
                    .Select(link =>
                        (
                            Other: link.Source == Selected.Id ? link.Target : link.Source,
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
                    .OrderByDescending(item =>
                        GraphCatalog.NodeKind(item.Item1.Kind) == GraphNodeKind.Artifact
                    )
                    .ThenByDescending(item => item.Item1.Degree)
                    .ToList();
        NotifyStateChanged();
        if (_nodeClick is not null)
            await _nodeClick(nodeId);
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
        ReachableArtifacts
            .Select(r => r.Node.Artifact)
            .FirstOrDefault(a => a is not null && IsPreviewable(a));

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

    public Task ClearAsync() => SelectAsync(null);

    public Task SelectAsync(string? nodeId) =>
        _js.InvokeVoidAsync("pcgraph.select", Id, nodeId).AsTask();

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
}

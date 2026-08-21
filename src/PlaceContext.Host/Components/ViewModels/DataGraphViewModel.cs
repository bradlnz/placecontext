using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DataGraphViewModel : PageViewModel
{
    private readonly IPlaceContextService _service;
    private readonly IJSRuntime _js;

    public DataGraphViewModel(IPlaceContextService service, IJSRuntime js)
    {
        _service = service;
        _js = js;
    }

    public Guid ProjectId { get; private set; }
    public GraphVizView? Graph { get; private set; }
    public bool Loading { get; private set; }
    public string? Error { get; private set; }

    public string? SelectedNodeId { get; private set; }

    private string _searchTerm = string.Empty;
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (_searchTerm == value)
                return;
            _searchTerm = value;
            NotifyStateChanged();
        }
    }

    public event Action<string?>? NodeSelected;

    public IReadOnlyList<GraphNodeView> FilteredNodes => BuildFilteredNodes();

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        Loading = true;
        Error = null;
        Graph = null;
        SelectedNodeId = null;
        SearchTerm = string.Empty;
        NotifyStateChanged();

        try
        {
            Graph = await _service.GetGraphVizAsync(projectId);
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
    }

    public void Search(string? term)
    {
        SearchTerm = term ?? string.Empty;
        NotifyStateChanged();
    }

    public void SelectNode(string? nodeId)
    {
        if (SelectedNodeId == nodeId)
            return;

        SelectedNodeId = nodeId;
        NodeSelected?.Invoke(nodeId);
        NotifyStateChanged();
    }

    public static string NodeKindClass(string? kind) =>
        string.IsNullOrWhiteSpace(kind) ? "kind-default" : $"kind-{kind.ToLowerInvariant()}";

    public void ClearSelection()
    {
        SelectedNodeId = null;
        SearchTerm = string.Empty;
        NodeSelected?.Invoke(null);
        NotifyStateChanged();
    }

    public Task AfterRenderAsync() =>
        _js.InvokeVoidAsync("pcgraph.splitter", "data-graph-splitter").AsTask();

    private IReadOnlyList<GraphNodeView> BuildFilteredNodes()
    {
        if (Graph is null)
            return Array.Empty<GraphNodeView>();

        var nodes = Graph.Nodes;
        if (!string.IsNullOrWhiteSpace(SelectedNodeId))
        {
            var neighborIds = new HashSet<string> { SelectedNodeId };
            foreach (var link in Graph.Links)
            {
                if (link.Source == SelectedNodeId)
                    neighborIds.Add(link.Target);
                else if (link.Target == SelectedNodeId)
                    neighborIds.Add(link.Source);
            }
            nodes = nodes.Where(n => neighborIds.Contains(n.Id)).ToList();
        }

        var term = SearchTerm.Trim();
        if (term.Length > 0)
        {
            nodes = nodes
                .Where(n =>
                    n.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || n.Content?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
                    || n.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return nodes
            .OrderByDescending(n => n.Id == SelectedNodeId)
            .ThenByDescending(n => n.Degree)
            .ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string ShortLabel(string label)
    {
        var index = label.LastIndexOf('/');
        var name = index >= 0 ? label[(index + 1)..] : label;
        return name.Length > 40 ? name[..39] + "…" : name;
    }
}

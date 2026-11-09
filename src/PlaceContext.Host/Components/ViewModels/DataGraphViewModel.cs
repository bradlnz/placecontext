using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Features;
using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;
using PlaceContext.Application;
using PlaceContext.Application.Access;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DataGraphViewModel : PageViewModel
{
    private readonly PlaceContextService _service;
    private readonly IJSRuntime _js;
    private readonly IPermissionService _permissions;
    private int _generation;
    private int _artifactSkip;

    public DataGraphViewModel(PlaceContextService service, IJSRuntime js, IPermissionService permissions)
    {
        _service = service;
        _js = js;
        _permissions = permissions;
    }

    public Guid ProjectId
    {
        get; private set;
    }
    public GraphVizView? Graph
    {
        get; private set;
    }
    public bool Loading
    {
        get; private set;
    }
    public string? Error
    {
        get; private set;
    }

    public string? SelectedNodeId
    {
        get; private set;
    }

    private string _searchTerm = string.Empty;
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (_searchTerm == value)
            {
                return;
            }
            _searchTerm = value;
            NotifyStateChanged();
        }
    }

    public event Action<string?>? NodeSelected;

    public IReadOnlyList<GraphNodeView> FilteredNodes => BuildFilteredNodes();

    public async Task LoadAsync(Guid projectId)
    {
        var generation = ++_generation;
        ProjectId = projectId;
        Artifacts = [];
        _artifactSkip = 0;
        ArtifactsError = null;
        ArtifactsLoading = false;
        HasMoreArtifacts = true;
        CanViewArtifacts = false;
        Loading = true;
        Error = null;
        Graph = null;
        SelectedNodeId = null;
        SearchTerm = string.Empty;
        NotifyStateChanged();

        try
        {
            var graph = await _service.GetGraphVizAsync(projectId);
            var canViewArtifacts = await _permissions.HasAsync(Permission.ArtifactsView);
            if (generation != _generation)
            {
                return;
            }
            Graph = graph;
            CanViewArtifacts = canViewArtifacts;
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                Error = ex.Message;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                Loading = false;
                NotifyStateChanged();
            }
        }
    }

    private const int ArtifactPageSize = 50;
    public IReadOnlyList<ArtifactFileView> Artifacts { get; private set; } = [];
    public bool CanViewArtifacts { get; private set; }
    public bool ArtifactsLoading { get; private set; }
    public bool HasMoreArtifacts { get; private set; } = true;
    public string? ArtifactsError { get; private set; }

    public async Task LoadArtifactsAsync()
    {
        if (!CanViewArtifacts || ArtifactsLoading || !HasMoreArtifacts)
        {
            return;
        }
        var generation = _generation;
        ArtifactsLoading = true;
        ArtifactsError = null;
        try
        {
            var files = await _service.ListProjectArtifactsAsync(ProjectId, ArtifactPageSize + 1, skip: _artifactSkip);
            if (generation != _generation)
            {
                return;
            }
            HasMoreArtifacts = files.Count > ArtifactPageSize;
            _artifactSkip += Math.Min(files.Count, ArtifactPageSize);
            Artifacts = Artifacts.Concat(files.Take(ArtifactPageSize)).DistinctBy(file => file.Id).ToArray();
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                ArtifactsError = ex.Message;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                ArtifactsLoading = false;
                NotifyStateChanged();
            }
        }
    }

    public string MappingUrl(Guid jobId) => $"/project/{ProjectId}/datamap?job={jobId}";

    public string? NodeUrl(GraphNodeView node)
    {
        if (node.Id.StartsWith("table:", StringComparison.Ordinal))
        {
            return $"/project/{ProjectId}/data?table={Uri.EscapeDataString(node.Id[6..])}";
        }
        return node.Id.StartsWith("mapping:", StringComparison.Ordinal)
            ? $"/project/{ProjectId}/datamap" : null;
    }

    public void Search(string? term)
    {
        SearchTerm = term ?? string.Empty;
        NotifyStateChanged();
    }

    public void SelectNode(string? nodeId)
    {
        if (SelectedNodeId == nodeId)
        {
            return;
        }
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
        {
            return Array.Empty<GraphNodeView>();
        }
        var nodes = Graph.Nodes;
        if (!string.IsNullOrWhiteSpace(SelectedNodeId))
        {
            var neighborIds = new HashSet<string> { SelectedNodeId };
            foreach (var link in Graph.Links)
            {
                if (link.Source == SelectedNodeId)
                {
                    neighborIds.Add(link.Target);
                    continue;
                }
                if (link.Target == SelectedNodeId)
                {
                    neighborIds.Add(link.Source);
                }
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

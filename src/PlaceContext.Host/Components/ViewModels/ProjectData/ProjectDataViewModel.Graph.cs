using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    public GraphVizView? GraphSidePaneGraph { get; private set; }
    public string? GraphSidePaneTitle { get; private set; }

    public async Task OpenGraphPaneAsync(string tableName, IReadOnlyDictionary<string, string?> values, int rowIndex)
    {
        try
        {
            var links = await _svc.RelatedRecordLinksForRowAsync(ProjectId, tableName, values);
            GraphSidePaneGraph = BuildRecordLinkGraph(tableName, rowIndex, links);
            GraphSidePaneTitle = $"{tableName} · row {rowIndex + 1}";
        }
        catch (Exception ex)
        {
            GraphSidePaneGraph = BuildRecordLinkGraph(tableName, rowIndex, Array.Empty<RecordLink>(), ex.Message);
            GraphSidePaneTitle = $"{tableName} · row {rowIndex + 1}";
        }
        NotifyStateChanged();
    }

    public void CloseGraphPane()
    {
        GraphSidePaneGraph = null;
        GraphSidePaneTitle = null;
        NotifyStateChanged();
    }

    private static GraphVizView BuildRecordLinkGraph(string tableName, int rowIndex,
        IReadOnlyList<RecordLink> links, string? error = null)
    {
        var nodes = new List<GraphNodeView>();
        var edges = new List<GraphLinkView>();
        var degree = new Dictionary<string, int>();

        void AddEdge(string a, string b)
        {
            edges.Add(new GraphLinkView(a, b, "Direct"));
            degree[a] = degree.GetValueOrDefault(a) + 1;
            degree[b] = degree.GetValueOrDefault(b) + 1;
        }

        var centralLabel = $"{tableName}:{rowIndex + 1}";
        var centralId = $"row:{tableName}:{rowIndex + 1}";
        nodes.Add(new GraphNodeView(centralId, centralLabel, 0, true,
            error is null ? $"{tableName} row {rowIndex + 1}" : $"Error: {error}", Kind: "human", Labeled: true));

        var relatedGroups = links
            .GroupBy(l => (l.TableName, l.RowKey))
            .Take(40)
            .ToList();

        var valueNodes = new Dictionary<string, string>();
        foreach (var group in relatedGroups)
        {
            var relatedId = $"rel:{group.Key.TableName}:{group.Key.RowKey}";
            var relatedLabel = string.IsNullOrWhiteSpace(group.Key.RowKey)
                ? $"{group.Key.TableName} row"
                : group.Key.RowKey;
            var kinds = string.Join(", ", group.Select(x => x.Kind).Distinct().OrderBy(k => k));
            nodes.Add(new GraphNodeView(relatedId, relatedLabel, 0, false,
                $"{group.Key.TableName} · shared {kinds}", Kind: "good"));
            AddEdge(centralId, relatedId);

            foreach (var link in group)
            {
                if (!valueNodes.TryGetValue(link.NormalizedValue, out var valueId))
                {
                    valueId = $"val:{link.Kind}:{link.NormalizedValue}";
                    valueNodes[link.NormalizedValue] = valueId;
                    nodes.Add(new GraphNodeView(valueId, link.DisplayValue, 0, false,
                        $"shared {link.Kind}", Kind: "artifact", Labeled: true));
                }

                if (!edges.Any(e =>
                        (e.Source == relatedId && e.Target == valueId) ||
                        (e.Source == valueId && e.Target == relatedId)))
                {
                    AddEdge(relatedId, valueId);
                }
            }
        }

        nodes = nodes.Select(n => n with { Degree = degree.GetValueOrDefault(n.Id) }).ToList();
        return new GraphVizView(Guid.Empty, nodes.Count, edges.Count, nodes, edges);
    }
}

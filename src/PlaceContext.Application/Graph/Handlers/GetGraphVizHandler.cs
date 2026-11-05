using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetGraphVizHandler(IProjectRepository projects, IProjectDataStore data,
    IDataMappingRepository mappings)
    : IQueryHandler<GetGraphVizQuery, GraphVizView>
{
    public async Task<GraphVizView> HandleAsync(GetGraphVizQuery query, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, query.ProjectId, ct);
        var tables = await data.ListTablesAsync(query.ProjectId, ct);
        var rules = await mappings.ListForProjectAsync(query.ProjectId, ct);
        var nodes = new Dictionary<string, GraphNodeView>(StringComparer.Ordinal);
        var links = new List<GraphLinkView>();

        foreach (var table in tables)
        {
            var id = "table:" + table.Name;
            nodes[id] = new(id, table.Name, 0, false,
                $"{(table.IsView ? "View" : "Table")} · approximately {table.RowEstimate:N0} rows",
                Kind: "Table", Labeled: true);
        }
        foreach (var rule in rules)
        {
            var tableId = "table:" + rule.TargetTable;
            if (!nodes.ContainsKey(tableId))
            {
                nodes[tableId] = new(tableId, rule.TargetTable, 0, false,
                    "Mapping target · table has not been created.", Kind: "Table", Labeled: true);
            }
            var mappingId = $"mapping:{rule.Id:N}";
            nodes[mappingId] = new(mappingId, rule.TargetTable + (rule.Enabled ? " mapping" : " mapping (disabled)"),
                0, false, $"Rows: {rule.RowsPath ?? "(root)"}\n" +
                string.Join("\n", rule.Fields.Select(field => $"{field.SourcePath} → {field.Column} ({field.Type})")),
                Kind: "Mapping", Labeled: true);
            var confidence = rule.Enabled ? "Direct" : "Ambiguous";
            links.Add(new(mappingId, tableId, confidence));
        }
        var degrees = links.SelectMany(link => new[] { link.Source, link.Target })
            .GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        var result = nodes.Values.Select(node => node with { Degree = degrees.GetValueOrDefault(node.Id) }).ToArray();
        return new(query.ProjectId, result.Length, links.Count, result, links);
    }
}

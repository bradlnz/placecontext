using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetGraphVizHandler(IProjectRepository projects, IProjectDataStore data,
    IDataMappingRepository mappings, IDataEntityRepository entities)
    : IQueryHandler<GetGraphVizQuery, GraphVizView>
{
    private const int MaxItemsPerEntity = 25;

    public async Task<GraphVizView> HandleAsync(GetGraphVizQuery query, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, query.ProjectId, ct);
        var tables = await data.ListTablesAsync(query.ProjectId, ct);
        var rules = await mappings.ListForProjectAsync(query.ProjectId, ct);
        var entityList = await entities.ListForProjectAsync(query.ProjectId, ct);
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
        foreach (var entity in entityList)
        {
            var entityId = $"entity:{entity.Id:N}";
            nodes[entityId] = new(entityId, entity.Name, 0, false,
                $"Entity · {entity.TableName}\n" +
                (entity.LabelColumn is null ? "Label: first column" : $"Label: {entity.LabelColumn}") +
                (entity.Relations.Count == 0 ? "" : "\n" + string.Join("\n", entity.Relations.Select(relation =>
                    $"{relation.Column} → {relation.TargetEntity}.{relation.TargetColumn}"))),
                Kind: "Entity", Labeled: true);
            var tableId = "table:" + entity.TableName;
            if (!nodes.ContainsKey(tableId))
            {
                nodes[tableId] = new(tableId, entity.TableName, 0, false,
                    "Entity backing table · table has not been created.", Kind: "Table", Labeled: true);
            }
            links.Add(new(entityId, tableId, "Direct"));
            if (tables.FirstOrDefault(table => string.Equals(table.Name, entity.TableName, StringComparison.OrdinalIgnoreCase)) is not null)
            {
                var rows = await data.ReadTableAsync(query.ProjectId, entity.TableName, MaxItemsPerEntity, ct);
                var labelIndex = entity.LabelColumn is null
                    ? 0
                    : rows.Columns
                        .Select((column, index) => (column, index))
                        .FirstOrDefault(item => string.Equals(item.column, entity.LabelColumn, StringComparison.OrdinalIgnoreCase)).index;
                foreach (var (row, index) in rows.Rows.Select((row, index) => (row, index)))
                {
                    var label = labelIndex < row.Count && !string.IsNullOrWhiteSpace(row[labelIndex])
                        ? row[labelIndex]!
                        : $"Item {index + 1}";
                    var itemId = $"item:{entity.Id:N}:{index}";
                    nodes[itemId] = new(itemId, $"{entity.Name}: {label}", 0, false,
                        string.Join("\n", rows.Columns.Select((column, columnIndex) =>
                            $"{column}: {(columnIndex < row.Count ? row[columnIndex] : null) ?? "—"}")),
                        Kind: "Item", Labeled: true);
                    links.Add(new(itemId, entityId, "Direct"));
                }
            }
            foreach (var relation in entity.Relations)
            {
                var target = entityList.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, relation.TargetEntity, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    continue;
                }
                links.Add(new(entityId, $"entity:{target.Id:N}",
                    $"{relation.Column} → {relation.TargetColumn}"));
            }
        }
        var degrees = links.SelectMany(link => new[] { link.Source, link.Target })
            .GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        var result = nodes.Values.Select(node => node with { Degree = degrees.GetValueOrDefault(node.Id) }).ToArray();
        return new(query.ProjectId, result.Length, links.Count, result, links);
    }
}

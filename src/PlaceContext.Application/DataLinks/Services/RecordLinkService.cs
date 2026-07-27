using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds and maintains the record-link index: scans the identity-ish columns (address/email/phone/
/// name/url) of a project's tables, normalizes each value, and stores every occurrence so rows in
/// different tables sharing a value (say, the same address) can be shown as linked. The index is
/// refreshed on write (per table) or rebuilt by a full project rescan. Entirely best-effort —
/// a scan failure never fails the write that triggered it.
/// </summary>
public sealed class RecordLinkService
{
    private const int MaxRowsPerTable = 5000;

    private readonly IProjectDataStore _store;
    private readonly IRecordLinkStore _links;
    private readonly IDataEntityRepository _entities;
    private readonly ILogger<RecordLinkService>? _log;

    public RecordLinkService(IProjectDataStore store, IRecordLinkStore links, IDataEntityRepository entities,
        ILogger<RecordLinkService>? log = null)
    {
        _store = store;
        _links = links;
        _entities = entities;
        _log = log;
    }

    /// <summary>Outcome of a full project rescan: tables actually scanned, link occurrences stored.</summary>
    public sealed record RescanResult(int TablesScanned, int LinksFound);

    /// <summary>
    /// Rebuilds the project's whole index. Each table is scanned independently — one failing table
    /// is skipped (logged) and never stops the rest.
    /// </summary>
    public async Task<RescanResult> RescanProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var tables = await _store.ListTablesAsync(projectId, ct);
        var entities = await EntitiesAsync(projectId, ct);

        var links = new List<RecordLink>();
        var scanned = 0;
        foreach (var table in tables)
        {
            try
            {
                var found = await ScanTableAsync(projectId, table.Name, entities, ct);
                if (found is null) continue; // no identity columns — nothing to index here
                links.AddRange(found);
                scanned++;
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Record-link rescan skipped table '{Table}' of project {ProjectId}.", table.Name, projectId);
            }
        }

        await _links.ReplaceForProjectAsync(projectId, links, ct);
        _log?.LogInformation("Record-link rescan of project {ProjectId}: {Tables} table(s) scanned, {Links} link(s) stored.",
            projectId, scanned, links.Count);
        return new RescanResult(scanned, links.Count);
    }

    /// <summary>Re-scans one table and replaces its slice of the index. Never throws.</summary>
    public async Task RefreshTableAsync(Guid projectId, string table, CancellationToken ct = default)
    {
        try
        {
            var entities = await EntitiesAsync(projectId, ct);
            var found = await ScanTableAsync(projectId, table, entities, ct);
            await _links.ReplaceForTableAsync(projectId, table, found ?? Array.Empty<RecordLink>(), ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Record-link refresh of '{Table}' in project {ProjectId} failed — the write itself is unaffected.",
                table, projectId);
        }
    }

    /// <summary>
    /// Warn-only duplicate check for rows about to be added: fetches the table's existing identity
    /// values once and matches the new rows' normalized identity values in memory. Returns warnings
    /// like "possible duplicate of &lt;RowKey&gt; (shared &lt;column&gt;: &lt;value&gt;)", capped at
    /// <paramref name="sample"/>. The caller must treat this as best-effort (try/catch) — a failed
    /// check must never block the write.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindDuplicatesAsync(Guid projectId, string table,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> newRows, int sample = 5, CancellationToken ct = default)
    {
        if (newRows.Count == 0) return Array.Empty<string>();

        var columns = await _store.ListColumnsAsync(projectId, table, ct);
        var identity = columns.Where(c => LinkValues.IsIdentityColumn(c.Name)).Select(c => c.Name).ToList();
        if (identity.Count == 0) return Array.Empty<string>();

        var entities = await EntitiesAsync(projectId, ct);
        var keyColumns = RowKeyColumns(table, columns, entities);
        var selected = identity.Concat(keyColumns).Distinct().ToList();
        var existing = await _store.ExecuteAsync(projectId, SelectSql(table, selected), ct);

        // First row key seen holding each (column, normalized value) — who a new row would duplicate.
        var holders = existing.Rows
            .SelectMany(row => RowOccurrences(selected, identity, keyColumns, row))
            .GroupBy(o => (o.Column, o.Normalized))
            .ToDictionary(g => g.Key, g => g.First().RowKey);
        if (holders.Count == 0) return Array.Empty<string>();

        return newRows
            .SelectMany(row => identity
                .Select(col => (Column: col, Display: GetIgnoreCase(row, col)?.Trim()))
                .Where(v => v.Display is { Length: > 0 })
                .Select(v => (v.Column, Display: v.Display!, Normalized: LinkValues.Normalize(v.Display),
                    Kind: LinkValues.Classify(v.Column, v.Display!)))
                .Where(v => LinkValues.IsLinkable(v.Kind, v.Normalized))
                .Where(v => holders.ContainsKey((v.Column, v.Normalized)))
                .Select(v => $"possible duplicate of {RowLabel(holders[(v.Column, v.Normalized)])} (shared {v.Column}: {v.Display})"))
            .Distinct()
            .Take(Math.Clamp(sample, 1, 50))
            .ToList();
    }

    // Null means the table has no identity columns and was skipped (not scanned, not an error).
    private async Task<IReadOnlyList<RecordLink>?> ScanTableAsync(Guid projectId, string table,
        IReadOnlyList<DataEntity> entities, CancellationToken ct)
    {
        var columns = await _store.ListColumnsAsync(projectId, table, ct);
        var identity = columns.Where(c => LinkValues.IsIdentityColumn(c.Name)).Select(c => c.Name).ToList();
        if (identity.Count == 0) return null;

        var keyColumns = RowKeyColumns(table, columns, entities);
        var selected = identity.Concat(keyColumns).Distinct().ToList();
        var result = await _store.ExecuteAsync(projectId, SelectSql(table, selected), ct);

        return result.Rows
            .SelectMany(row => RowOccurrences(selected, identity, keyColumns, row))
            .Select(o => new RecordLink(projectId, o.Kind, o.Normalized, o.Display, table, o.Column, o.RowKey))
            .Distinct()
            .ToList();
    }

    // The linkable identity occurrences of one scanned row: its row key plus each identity cell
    // that normalizes to something worth linking.
    private static IEnumerable<(string Column, string Display, string Kind, string Normalized, string RowKey)> RowOccurrences(
        IReadOnlyList<string> selected, IReadOnlyList<string> identity, IReadOnlyList<string> keyColumns,
        IReadOnlyList<string?> row)
    {
        var cells = selected.Select((name, i) => (Name: name, Value: i < row.Count ? row[i]?.Trim() : null)).ToList();
        var rowKey = string.Join(" · ", keyColumns
            .Select(k => cells.FirstOrDefault(c => c.Name == k).Value)
            .Where(v => !string.IsNullOrEmpty(v)));
        return identity
            .Select(col => (Column: col, Display: cells.FirstOrDefault(c => c.Name == col).Value))
            .Where(v => v.Display is { Length: > 0 })
            .Select(v => (v.Column, Display: v.Display!, Kind: LinkValues.Classify(v.Column, v.Display!),
                Normalized: LinkValues.Normalize(v.Display), RowKey: rowKey))
            .Where(v => LinkValues.IsLinkable(v.Kind, v.Normalized));
    }

    // The label a row is keyed by: the entity's label column when the table backs a DataEntity,
    // else the first ≤ 3 text columns of the table.
    private static IReadOnlyList<string> RowKeyColumns(string table, IReadOnlyList<ProjectColumnInfo> columns,
        IReadOnlyList<DataEntity> entities)
    {
        var entity = entities.FirstOrDefault(e => string.Equals(e.TableName, table, StringComparison.OrdinalIgnoreCase));
        if (entity?.LabelColumn is { Length: > 0 } label && columns.Any(c => c.Name == label))
            return new[] { label };
        return columns.Where(IsTextColumn).Take(3).Select(c => c.Name).ToList();
    }

    private static bool IsTextColumn(ProjectColumnInfo c)
        => c.Type is DataColumnTypes.Text or "citext"
            || c.Type.StartsWith("character", StringComparison.Ordinal)
            || c.Type.StartsWith("varchar", StringComparison.Ordinal);

    // Label hints are a nicety, never a requirement — a failing entity repo must not stop a scan.
    private async Task<IReadOnlyList<DataEntity>> EntitiesAsync(Guid projectId, CancellationToken ct)
    {
        try { return await _entities.ListForProjectAsync(projectId, ct); }
        catch { return Array.Empty<DataEntity>(); }
    }

    private static string SelectSql(string table, IReadOnlyList<string> columns)
        => $"SELECT {string.Join(", ", columns.Select(c => $"\"{Q(c)}\"::text"))} FROM \"{Q(table)}\" LIMIT {MaxRowsPerTable}";

    private static string Q(string identifier) => identifier.Replace("\"", "");

    private static string? GetIgnoreCase(IReadOnlyDictionary<string, string?> row, string column)
        => row.FirstOrDefault(kv => string.Equals(kv.Key, column, StringComparison.OrdinalIgnoreCase)).Value;

    private static string RowLabel(string rowKey) => rowKey.Length > 0 ? rowKey : "an existing row";
}

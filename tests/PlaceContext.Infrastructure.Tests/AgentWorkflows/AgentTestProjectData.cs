using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

internal sealed class AgentTestProjectData : IProjectDataStore
{
    public Dictionary<string, ProjectTablePageResult> Tables { get; } = new(StringComparer.Ordinal);
    public List<(Guid ProjectId, string Table, int PageSize, bool IncludeCount)> Reads { get; } = [];
    public int ListCalls { get; private set; }
    public bool FailList { get; set; }
    public HashSet<string> FailReads { get; } = [];

    public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
    {
        ListCalls++;
        if (FailList) throw new InvalidOperationException("Private database error");
        return Task.FromResult<IReadOnlyList<ProjectTableInfo>>(Tables.Select(x => new ProjectTableInfo(x.Key, x.Value.Rows.Count)).ToArray());
    }
    public Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
        int page, int pageSize, string? sortColumn = null, bool sortDescending = false, CancellationToken ct = default, bool includeTotalCount = true)
    {
        Reads.Add((projectId, tableName, pageSize, includeTotalCount));
        if (FailReads.Contains(tableName)) throw new InvalidOperationException("Private source error");
        return Task.FromResult(Tables[tableName]);
    }

    public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProjectTableReadResult> ReadTableAsync(Guid projectId, string tableName, long maxRows = 10000, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProjectTableSchema>> ListTableSchemasAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default) => throw new NotSupportedException();
    public Task InsertRowAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> UpdateRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
        IReadOnlyDictionary<string, string?> values, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> DeleteRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default) => throw new NotSupportedException();
}

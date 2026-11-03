using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using PlaceContext.Application.DatabaseInspection;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class DatabaseInspector(AppDbContext db, ICurrentTenant tenant) : IDatabaseInspector
{
    // Only reviewed operational fields are exposed; new fields are masked until explicitly added.
    private static readonly HashSet<string> VisibleColumns = new(StringComparer.Ordinal)
    {
        "Id", "TenantId", "ProjectId", "UserId", "JobId", "RunId", "ChainId", "ChainRunId",
        "Name", "DisplayName", "Role", "Status", "Kind", "Type", "Enabled", "IsDefaultAdmin", "IsExternalAdmin",
        "CreatedAt", "UpdatedAt", "StartedAt", "FinishedAt", "LastSeenAt", "DeletedAt",
        "PeriodStart", "PeriodEnd", "Plan", "UsedRuns", "IncludedRuns", "NextDispatchAt"
    };

    public Task<IReadOnlyList<DatabaseTable>> ListTablesAsync(CancellationToken ct = default)
    {
        RequireTenant();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DatabaseTable>>(Entities().Select(Describe)
            .OrderBy(table => table.Name, StringComparer.Ordinal).ToArray());
    }

    public async Task<DatabaseTablePage> ReadTableAsync(string table, int offset, CancellationToken ct = default)
    {
        var result = await QueryAsync(new(table, [], false, [], [], DatabaseTablePage.PageSize, offset), ct);
        var description = Describe(Entities().Single(entity => TableName(entity) == table));
        return new(description, result.Rows, offset, result.Truncated);
    }

    public async Task<ProjectQueryResult> QueryAsync(DatabaseSelectStatement query, CancellationToken ct = default)
    {
        RequireTenant();
        if (query.Offset < 0 || query.Offset > 1000000 || query.Limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }
        var entity = Entities().SingleOrDefault(item => TableName(item) == query.Table)
            ?? throw new ArgumentException("Unknown application table.");
        var description = Describe(entity);
        if (!description.CanReadRows)
        {
            throw new ArgumentException("Global tables expose their schema only.");
        }
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var properties = Properties(entity, store).ToDictionary(property => property.GetColumnName(store)!, StringComparer.Ordinal);
        IProperty Column(string name, bool requireVisible = false)
        {
            if (!properties.TryGetValue(name, out var property))
            {
                throw new ArgumentException($"Unknown column '{name}'. Quote mixed-case column names with double quotes.");
            }
            if (requireVisible && !VisibleColumns.Contains(property.Name))
            {
                throw new ArgumentException("Masked columns cannot be used in filters or ordering.");
            }
            return property;
        }
        var names = query.Columns.Count == 0 ? properties.Keys.ToArray() : query.Columns.ToArray();
        var projection = names.Select(name => VisibleColumns.Contains(Column(name).Name)
            ? $"left(CAST({Quote(name)} AS text), 2048) AS {Quote(name)}"
            : $"'[redacted]'::text AS {Quote(name)}").ToArray();
        var owner = entity.ClrType == typeof(TenantRow) ? "Id" : "TenantId";
        var ownerColumn = entity.FindProperty(owner)!.GetColumnName(store)!;
        var qualifiedTable = entity.GetSchema() is { } schema
            ? $"{Quote(schema)}.{Quote(entity.GetTableName()!)}"
            : Quote(entity.GetTableName()!);
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await using var command = new NpgsqlCommand { Connection = connection, CommandTimeout = 10 };
        var predicates = new List<string> { $"{Quote(ownerColumn)} = @tenant" };
        foreach (var filter in query.Filters)
        {
            var property = Column(filter.Column, requireVisible: true);
            var operation = filter.Operator;
            if (operation is "IS NULL" or "IS NOT NULL")
            {
                predicates.Add($"{Quote(filter.Column)} {operation}");
                continue;
            }
            if (operation is not ("=" or "!=" or "<>" or "<" or ">" or "<=" or ">=" or "LIKE" or "ILIKE") || filter.Value is null)
            {
                throw new ArgumentException("Unsupported comparison.");
            }
            var parameter = "value" + predicates.Count;
            command.Parameters.AddWithValue(parameter, filter.Value);
            if (operation is "LIKE" or "ILIKE")
            {
                predicates.Add($"CAST({Quote(filter.Column)} AS text) {operation} @{parameter}");
                continue;
            }
            predicates.Add($"{Quote(filter.Column)} {operation} CAST(@{parameter} AS {property.GetColumnType()})");
        }
        var ordering = query.Order.Select(order =>
        {
            Column(order.Column, requireVisible: true);
            return $"{Quote(order.Column)} {(order.Descending ? "DESC" : "ASC")}";
        }).ToArray();
        if (ordering.Length == 0 && !query.Count)
        {
            ordering = entity.FindPrimaryKey()!.Properties.Select(property => Quote(property.GetColumnName(store)!)).ToArray();
        }
        var orderBy = ordering.Length > 0 ? " ORDER BY " + string.Join(", ", ordering) : "";
        var select = query.Count ? "CAST(COUNT(*) AS text) AS count" : string.Join(", ", projection);
        command.CommandText = $"SELECT {select} FROM {qualifiedTable} WHERE {string.Join(" AND ", predicates)}" +
            orderBy + " LIMIT @limit OFFSET @offset";
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        command.Parameters.AddWithValue("limit", query.Limit + 1);
        command.Parameters.AddWithValue("offset", query.Offset);
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<IReadOnlyList<string?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new string?[reader.FieldCount];
            for (var index = 0; index < row.Length; index++)
            {
                row[index] = reader.IsDBNull(index) ? null : reader.GetString(index);
            }
            rows.Add(row);
        }
        var hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }
        return new(query.Count ? ["count"] : names, rows, 0, hasMore);
    }

    private IEnumerable<IEntityType> Entities() => db.Model.GetEntityTypes()
        .Where(entity => entity.GetTableName() is not null && entity.FindPrimaryKey() is not null);

    private static DatabaseTable Describe(IEntityType entity)
    {
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var columns = Properties(entity, store).Select(property => new DatabaseColumn(
            property.GetColumnName(store)!, property.GetColumnType() ?? property.ClrType.Name,
            property.IsNullable, property.IsPrimaryKey(), !VisibleColumns.Contains(property.Name))).ToArray();
        var scoped = typeof(ITenantOwned).IsAssignableFrom(entity.ClrType)
            && entity.FindProperty("TenantId")?.ClrType == typeof(Guid);
        return new(TableName(entity), columns, scoped || entity.ClrType == typeof(TenantRow));
    }

    private static IProperty[] Properties(IEntityType entity, StoreObjectIdentifier store) => entity.GetProperties()
        .Where(property => property.GetColumnName(store) is not null)
        .OrderBy(property => property.GetColumnName(store), StringComparer.Ordinal).ToArray();

    private static string TableName(IEntityType entity) => $"{entity.GetSchema() ?? "public"}.{entity.GetTableName()}";
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private void RequireTenant()
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A workspace is required.");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using PlaceContext.Application.DatabaseInspection;
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
        RequireTenant();
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        var entity = Entities().SingleOrDefault(item => TableName(item) == table)
            ?? throw new ArgumentException("Unknown application table.", nameof(table));
        var description = Describe(entity);
        if (!description.CanReadRows)
        {
            throw new UnauthorizedAccessException("Global tables expose their schema only.");
        }
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var properties = Properties(entity, store);
        var projection = properties.Select(property => VisibleColumns.Contains(property.Name)
            ? $"left(CAST({Quote(property.GetColumnName(store)!)} AS text), 2048)"
            : "'[redacted]'::text");
        var owner = entity.ClrType == typeof(TenantRow) ? "Id" : "TenantId";
        var ownerColumn = entity.FindProperty(owner)!.GetColumnName(store)!;
        var ordering = entity.FindPrimaryKey()!.Properties.Select(property => Quote(property.GetColumnName(store)!));
        var qualifiedTable = entity.GetSchema() is { } schema
            ? $"{Quote(schema)}.{Quote(entity.GetTableName()!)}"
            : Quote(entity.GetTableName()!);
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            $"SELECT {string.Join(", ", projection)} FROM {qualifiedTable} " +
            $"WHERE {Quote(ownerColumn)} = @tenant ORDER BY {string.Join(", ", ordering)} LIMIT @limit OFFSET @offset",
            connection) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        command.Parameters.AddWithValue("limit", DatabaseTablePage.PageSize + 1);
        command.Parameters.AddWithValue("offset", offset);
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
        var hasMore = rows.Count > DatabaseTablePage.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }
        return new(description, rows, offset, hasMore);
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

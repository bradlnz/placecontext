using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.DataSources;

public sealed class DatabaseSourceReader(AppDbContext db, IDataEncryptor encryption, ICurrentTenant tenant) : IDataSourceReader
{
    public async Task<IReadOnlyList<SourceRecord>> ReadAsync(DataSourceDefinition source, CancellationToken ct)
    {
        DataSourceRules.Validate(source);
        var row = await db.DataSources.AsNoTracking().SingleOrDefaultAsync(value => value.Id == source.Id
            && value.ProjectId == source.ProjectId, ct) ?? throw new InvalidOperationException("Source not found.");
        var connectionString = JsonSerializer.Deserialize<string>(encryption.Unprotect(row.ConnectionCipher,
            $"data-source.v1/{tenant.TenantId:N}/{source.Id:N}/connection"));
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Source credentials could not be decrypted.");
        }
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await using (var setup = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '30s'", connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(ct);
            }
            var columns = string.Join(", ", source.Columns.Select(Quote));
            await using var command = new NpgsqlCommand($"SELECT t.{Quote(source.KeyColumn)}::text, row_to_json(t)::text FROM (SELECT {columns} FROM {Quote(source.Schema)}.{Quote(source.Table)}) t ORDER BY t.{Quote(source.KeyColumn)} LIMIT {DataSourceRules.RecordLimit(source) + 1}", connection, transaction);
            var result = new List<SourceRecord>();
            long chars = 0;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var key = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var json = reader.GetString(1);
                chars += json.Length;
                if (json.Length > DataSourceRules.MaxRecordChars || chars > DataSourceRules.SnapshotCharLimit(source))
                {
                    throw new InvalidOperationException("Source snapshot exceeds its record or total character limit.");
                }
                result.Add(new(key, json));
            }
            return result;
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("Source could not be read. Check the connection, read-only account, selected columns and source limits.");
        }
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
}

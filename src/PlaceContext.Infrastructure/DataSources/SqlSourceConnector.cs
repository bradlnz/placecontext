using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Infrastructure.DataSources;

internal static class SqlSourceConnector
{
    private static DbConnection Connection(string provider, string settings)
    {
        if (provider == "postgres")
        {
            return new NpgsqlConnection(settings);
        }
        var values = SourceSettings.Parse(provider, settings);
        return provider == "mysql" ? new MySqlConnection(new MySqlConnectionStringBuilder
        {
            Server = values["host"], Port = uint.Parse(values["port"]), Database = values["database"], UserID = values["username"],
            Password = values["password"], SslMode = MySqlSslMode.VerifyFull, Pooling = false, AllowLoadLocalInfile = false,
            ConnectionTimeout = 10, DefaultCommandTimeout = 30
        }.ConnectionString) : new SqlConnection(new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{values["host"]},{values["port"]}", InitialCatalog = values["database"], UserID = values["username"],
            Password = values["password"], Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = false,
            Pooling = false, ConnectTimeout = 10, PersistSecurityInfo = false
        }.ConnectionString);
    }

    public static async Task<IReadOnlyList<SourceDataset>> DiscoverAsync(string provider, string settings, CancellationToken ct)
    {
        var filter = provider == "mysql" ? "c.table_schema = DATABASE()" : "c.table_schema NOT IN ('pg_catalog', 'information_schema', 'sys')";
        var sql = $"SELECT c.table_schema, c.table_name, c.column_name, CASE WHEN EXISTS (SELECT 1 FROM information_schema.table_constraints t JOIN information_schema.key_column_usage k ON t.constraint_name = k.constraint_name AND t.table_schema = k.table_schema AND t.table_name = k.table_name WHERE t.constraint_type = 'PRIMARY KEY' AND k.table_schema = c.table_schema AND k.table_name = c.table_name AND k.column_name = c.column_name) THEN 1 ELSE 0 END AS is_key FROM information_schema.columns c WHERE {filter} ORDER BY c.table_schema,c.table_name,c.ordinal_position";
        var rows = await QueryAsync(provider, settings, sql, 8001, ct);
        return Datasets(rows);
    }

    internal static IReadOnlyList<SourceDataset> Datasets(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows.Count > 8000)
        {
            throw new InvalidOperationException("Discovery exceeds 8,000 fields. Use an account with access to fewer datasets.");
        }
        return rows.GroupBy(row => (Schema: Convert.ToString(row.Values.ElementAt(0))!, Table: Convert.ToString(row.Values.ElementAt(1))!))
            .Select(group => new SourceDataset(group.Key.Schema, group.Key.Table,
                Convert.ToString(group.FirstOrDefault(row => Convert.ToString(row.Values.ElementAt(3)) == "1")?.Values.ElementAt(2)
                    ?? group.First().Values.ElementAt(2))!, group.Select(row => Convert.ToString(row.Values.ElementAt(2))!).ToArray()))
            .Where(dataset => dataset.Columns.Count <= 40).ToArray();
    }

    internal static string Select(DataSourceDefinition source)
    {
        DataSourceRules.Validate(source);
        string Quote(string value) => source.Provider == "mysql" ? $"`{value}`" : source.Provider == "mssql" ? $"[{value}]" : $"\"{value}\"";
        var columns = string.Join(",", source.Columns.Select(Quote));
        var limit = DataSourceRules.MaxRecords + 1;
        return $"SELECT {(source.Provider == "mssql" ? $"TOP ({limit}) " : "")}{columns} FROM {Quote(source.Schema)}.{Quote(source.Table)} ORDER BY {Quote(source.KeyColumn)}{(source.Provider == "mssql" ? "" : $" LIMIT {limit}")}";
    }

    public static async Task<IReadOnlyList<SourceRecord>> ReadAsync(DataSourceDefinition source, string settings, CancellationToken ct)
        => Records(await QueryAsync(source.Provider, settings, Select(source), DataSourceRules.MaxRecords + 1, ct), source.KeyColumn);

    internal static SourceRecord[] Records(IReadOnlyList<Dictionary<string, object?>> rows, string key)
        => rows.Select(row => new SourceRecord(Convert.ToString(row.GetValueOrDefault(key), CultureInfo.InvariantCulture) ?? "", JsonSerializer.Serialize(row))).ToArray();

    private static async Task<IReadOnlyList<Dictionary<string, object?>>> QueryAsync(string provider, string settings, string sql, int max, CancellationToken ct)
    {
        try
        {
            await using var connection = Connection(provider, settings);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;
            await using var reader = await command.ExecuteReaderAsync(ct);
            var rows = new List<Dictionary<string, object?>>();
            var chars = 0;
            while (rows.Count < max && await reader.ReadAsync(ct))
            {
                var row = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName,
                    index => reader.IsDBNull(index) ? null : reader.GetValue(index));
                var length = JsonSerializer.Serialize(row).Length;
                chars += length;
                if (length > DataSourceRules.MaxRecordChars || chars > 8_000_000)
                {
                    throw new InvalidOperationException("Source exceeds the snapshot size limit.");
                }
                rows.Add(row);
            }
            return rows;
        }
        catch (DbException)
        {
            throw new InvalidOperationException("Could not read source. Check credentials, certificate, read-only access and dataset selection.");
        }
    }
}

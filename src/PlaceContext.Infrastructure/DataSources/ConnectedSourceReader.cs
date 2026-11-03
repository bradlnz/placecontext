using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.DataSources;

public sealed class ConnectedSourceReader(AppDbContext db, IDataEncryptor encryption, ICurrentTenant tenant,
    IHttpClientFactory clients, DatabaseSourceReader postgres) : IDataSourceReader, IDataSourceDiscovery, IDataFeedAccess
{
    public async Task<IReadOnlyList<SourceDataset>> DiscoverAsync(string provider, string connection, CancellationToken ct)
    {
        var kind = SourceProviders.Get(provider).Kind;
        if (kind == "feed")
        {
            return [provider == "email" ? new("feed", "email", "id", ["id", "from", "to", "subject", "text", "receivedAt"]) : new("feed", "events", "id", ["id", "content"])];
        }
        var settings = SourceSettings.Validate(provider, connection);
        try
        {
            if (kind == "database")
            {
                return await SqlSourceConnector.DiscoverAsync(provider, settings, ct);
            }
            using var client = clients.CreateClient("data-source");
            if (kind == "warehouse")
            {
                return await new WarehouseSourceConnector(client).DiscoverAsync(provider, settings, ct);
            }
            await new LakeSourceConnector(client).ReadAsync(provider, settings, true, ct);
            return [LakeSourceConnector.Dataset];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Discovery failed. Check connection, read-only access, certificate and source limits.");
        }
    }

    public async Task<IReadOnlyList<SourceRecord>> ReadAsync(DataSourceDefinition source, CancellationToken ct)
    {
        DataSourceRules.Validate(source);
        if (source.Provider == "postgres")
        {
            return await postgres.ReadAsync(source, ct);
        }
        var settings = await CredentialsAsync(source.ProjectId, source.Id, ct);
        try
        {
            if (SourceProviders.Get(source.Provider).Kind == "database")
            {
                return await SqlSourceConnector.ReadAsync(source, settings, ct);
            }
            using var client = clients.CreateClient("data-source");
            var rows = SourceProviders.Get(source.Provider).Kind == "warehouse"
                ? await new WarehouseSourceConnector(client).ReadAsync(source, settings, ct)
                : await new LakeSourceConnector(client).ReadAsync(source.Provider, settings, false, ct);
            return rows.Select(row => new SourceRecord(row.Key, JsonSerializer.Serialize(
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Json)!.Where(field => source.Columns.Contains(field.Key))
                    .ToDictionary(field => field.Key, field => field.Value)))).ToArray();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Source read failed. Existing data was retained. Check connection, data format and limits.");
        }
    }

    public async Task<bool> AuthorizeAsync(Guid projectId, Guid sourceId, string token, CancellationToken ct)
    {
        if (token.Length is < 43 or > 128)
        {
            return false;
        }
        try
        {
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(await CredentialsAsync(projectId, sourceId, ct));
            return settings?.TryGetValue("token", out var expected) == true && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(token)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string> CredentialsAsync(Guid projectId, Guid sourceId, CancellationToken ct)
    {
        var row = await db.DataSources.AsNoTracking().SingleOrDefaultAsync(row => row.ProjectId == projectId && row.Id == sourceId, ct)
            ?? throw new InvalidOperationException("Source not found.");
        return JsonSerializer.Deserialize<string>(encryption.Unprotect(row.ConnectionCipher,
            $"data-source.v1/{tenant.TenantId:N}/{sourceId:N}/connection")) ?? throw new InvalidOperationException("Invalid connection.");
    }
}

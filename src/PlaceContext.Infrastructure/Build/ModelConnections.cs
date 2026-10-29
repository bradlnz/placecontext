using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Build;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Build;

public sealed class ModelConnections(IServiceScopeFactory scopes, ICurrentUser user, ICurrentTenant tenant,
    IPermissionService permissions, IMembershipService members, IDataEncryptor encryption,
    IHttpClientFactory clients, LocalModelDiscovery? local = null, LocalCliModels? cli = null) : IModelConnections, IBuildModelGateway
{
    private const string Purpose = "model.credentials.v1";
    private async Task CheckAsync(bool admin, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !tenant.IsResolved) throw new UnauthorizedAccessException("Sign in to use model connections.");
        if (admin && !await permissions.HasAsync(Permission.SettingsManage, ct))
            throw new UnauthorizedAccessException("Only a settings administrator can manage model connections.");
    }
    private async Task<bool> CanUseLocalCliAsync(CancellationToken ct) =>
        tenant.Slug == "default" && await members.IsDefaultAdminAsync(user.UserId, ct);

    private async Task CheckLocalCliAsync(CancellationToken ct)
    {
        if (!await CanUseLocalCliAsync(ct))
            throw new UnauthorizedAccessException("Host CLI accounts are only available to the default workspace administrator.");
    }
    public async Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default)
    {
        await CheckAsync(false, ct);
        if (local is not null) await local.DetectAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var canUseCli = await CanUseLocalCliAsync(ct);
        return (await db.ModelConnections.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct))
            .Where(x => canUseCli || !LocalCliModels.Supports(x.Protocol))
            .Select(x => new ModelConnection(x.Id, x.Name, x.Protocol, x.BaseUrl,
                JsonSerializer.Deserialize<string[]>(x.ModelsJson)!, x.Enabled, x.ApiKey.Length > 0)).ToList();
    }
    public static void Validate(SaveModelConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Name) || connection.Name.Length > 100) throw new ArgumentException("Enter a name of up to 100 characters.");
        var isCli = LocalCliModels.Supports(connection.Protocol);
        if (connection.Protocol is not ("openai" or "anthropic") && !isCli) throw new ArgumentException("Choose a supported model connection.");
        if (isCli && (connection.BaseUrl != LocalCliModels.Endpoint(connection.Protocol) || !string.IsNullOrEmpty(connection.ApiKey)))
            throw new ArgumentException("Local CLI connections use the installed command and its existing sign-in; they do not accept a URL or API key.");
        if (!isCli && (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0))
            throw new ArgumentException("Enter an HTTP(S) API base URL without credentials, query parameters, or fragments.");
        if (connection.Models.Count > 500 || connection.Models.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200))
            throw new ArgumentException("Enter up to 500 model IDs, each no longer than 200 characters.");
        if (connection.ApiKey?.Length > 8000 || connection.ApiKey?.Any(char.IsControl) == true)
            throw new ArgumentException("Invalid API key.");
    }
    public async Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default)
    {
        await CheckAsync(true, ct);
        Validate(connection);
        if (LocalCliModels.Supports(connection.Protocol)) await CheckLocalCliAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ModelConnections.SingleOrDefaultAsync(x => x.Id == connection.Id, ct);
        if (row is null) { row = new() { Id = connection.Id == Guid.Empty ? Guid.NewGuid() : connection.Id }; db.ModelConnections.Add(row); }
        var url = connection.BaseUrl.TrimEnd('/');
        if (row.ApiKey.Length > 0 && (row.BaseUrl != url || row.Protocol != connection.Protocol)
            && string.IsNullOrEmpty(connection.ApiKey) && !connection.ClearApiKey)
            throw new ArgumentException("Re-enter or clear the API key when changing its endpoint or protocol.");
        row.Name = connection.Name.Trim(); row.Protocol = connection.Protocol; row.BaseUrl = url;
        row.ModelsJson = JsonSerializer.Serialize(connection.Models.Select(x => x.Trim()).Distinct().ToArray());
        row.Enabled = connection.Enabled;
        row.AutoDiscovered = false;
        if (connection.ClearApiKey) row.ApiKey = "";
        else if (!string.IsNullOrEmpty(connection.ApiKey)) row.ApiKey = encryption.Protect(connection.ApiKey, Purpose);
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await CheckAsync(true, ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ModelConnections.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return;
        db.ModelConnections.Remove(row);
        await db.SaveChangesAsync(ct);
    }
    private async Task<ModelConnectionRow> GetAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ModelConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException("Model connection not found.");
    }
    public async Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default)
    {
        await CheckAsync(true, ct);
        var row = await GetAsync(id, ct);
        if (LocalCliModels.Supports(row.Protocol))
        {
            await CheckLocalCliAsync(ct);
            return cli is null ? [] : await cli.DiscoverAsync(row.Protocol, ct) ?? [];
        }
        var body = await SendAsync(row, "models", null, ct);
        return (body["data"] as JsonArray ?? throw new InvalidOperationException("This provider does not support model discovery. Enter model IDs manually."))
            .Select(x => x?["id"]?.GetValue<string>()).OfType<string>().Where(x => x.Length is > 0 and <= 200)
            .Distinct().Order().Take(500).ToArray();
    }
    public async Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct)
    {
        await CheckAsync(false, ct);
        if (!await permissions.HasAsync(Permission.ProjectsView, ct)) throw new UnauthorizedAccessException("Project access is required.");
        var row = await GetAsync(connectionId, ct);
        if (!row.Enabled || !JsonSerializer.Deserialize<string[]>(row.ModelsJson)!.Contains(model))
            throw new InvalidOperationException("Choose an enabled connection and one of its configured models.");
        if (LocalCliModels.Supports(row.Protocol))
        {
            await CheckLocalCliAsync(ct);
            if (cli is null) throw new InvalidOperationException("Local CLI support is unavailable on this host.");
            return await cli.CompleteAsync(row.Protocol, model, messages, tools, ct);
        }
        var body = await SendAsync(row, row.Protocol == "anthropic" ? "messages" : "chat/completions",
            ModelWireFormat.Request(row.Protocol, model, messages, tools), ct);
        return ModelWireFormat.Response(row.Protocol, body);
    }
    private async Task<JsonObject> SendAsync(ModelConnectionRow row, string path, JsonObject? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(body is null ? TimeSpan.FromMinutes(2) : BuildExecutionLimits.ModelResponse);
        using var client = clients.CreateClient("build-model");
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, row.BaseUrl + "/" + path);
        if (body is not null) request.Content = JsonContent.Create(body);
        var key = encryption.Unprotect(row.ApiKey, Purpose);
        if (row.Protocol == "anthropic")
        {
            request.Headers.Add("anthropic-version", "2023-06-01");
            if (key.Length > 0) request.Headers.Add("x-api-key", key);
        }
        else if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Model provider returned HTTP {(int)response.StatusCode}. Check the connection, model access, and provider quota in Settings → Models.");
            await response.Content.LoadIntoBufferAsync(2_000_000, timeout.Token);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token))?.AsObject()
                ?? throw new InvalidOperationException("The model provider returned an empty response.");
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The model provider did not finish its response within the {(body is null ? 2 : BuildExecutionLimits.ModelResponse.TotalMinutes):0}-minute limit. No further actions were started.", ex);
        }
    }
}

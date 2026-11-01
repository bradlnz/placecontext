using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Connections;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Connections;

/// <summary>Managed Microsoft Graph OAuth2: exact configured redirect, state + S256 PKCE,
/// tenant/user binding, encrypted tokens and serialized refresh-token rotation.</summary>
public sealed class ManagedMicrosoftOAuthConnections(
    AppDbContext db, ICurrentTenant tenant, ICurrentUser user,
    IPermissionService permissions, IDataEncryptor encrypt,
    IOptions<ManagedMicrosoftOAuthOptions> settings, IHttpClientFactory clients)
    : IManagedOAuthConnections
{
    private const string Purpose = "managed.oauth.microsoft.v1";
    private const string Scope = "offline_access User.Read Mail.Read";
    private const string Provider = "microsoft";
    private static readonly TimeSpan HandshakeLife = TimeSpan.FromMinutes(10);

    public async Task<Uri> BeginAsync(Guid projectId, CancellationToken ct = default)
    {
        await CheckAsync(projectId, ct);
        var options = Settings();
        var verifier = ManagedOAuthProtocol.RandomVerifier();
        var challenge = ManagedOAuthProtocol.Challenge(verifier);
        var state = ManagedOAuthProtocol.RandomState();
        db.ManagedOAuthStates.Add(new ManagedOAuthStateRow
        {
            Id = Guid.NewGuid(), TenantId = tenant.TenantId, ProjectId = projectId,
            UserId = user.UserId, Provider = Provider,
            StateHash = ManagedOAuthProtocol.HashState(state), PkceVerifier = encrypt.Protect(verifier, Purpose),
            ExpiresAt = DateTimeOffset.UtcNow.Add(HandshakeLife)
        });
        await db.SaveChangesAsync(ct);
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = options.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scope,
            ["state"] = state,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge
        };
        var query = string.Join("&", fields.Select(p =>
            Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return new Uri($"https://login.microsoftonline.com/{options.Authority}/oauth2/v2.0/authorize?{query}");
    }

    public async Task<Guid> CompleteAsync(string state, string authorizationCode, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || !user.IsAuthenticated)
            throw new UnauthorizedAccessException("Sign into the workspace before connecting.");
        if (state.Length is < 20 or > 300 || authorizationCode.Length is < 10 or > 5000)
            throw new InvalidOperationException("Invalid or expired connection response.");
        var hash = ManagedOAuthProtocol.HashState(state);
        var pending = await db.ManagedOAuthStates.SingleOrDefaultAsync(x => x.StateHash == hash, ct)
            ?? throw new InvalidOperationException("The connection request expired or was already used.");
        if (pending.UserId != user.UserId || pending.TenantId != tenant.TenantId
            || pending.ExpiresAt <= DateTimeOffset.UtcNow || pending.Provider != Provider)
            throw new UnauthorizedAccessException("The OAuth request does not belong to this session.");
        await CheckAsync(pending.ProjectId, ct);
        var options = Settings();
        var verifier = encrypt.Unprotect(pending.PkceVerifier, Purpose);
        // Consume the state before making network requests. Any failed code exchange requires
        // reconnecting, preventing replay of the same browser return.
        db.ManagedOAuthStates.Remove(pending);
        await db.SaveChangesAsync(ct);
        var response = await ExchangeAsync(options, new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = options.RedirectUri,
            ["code_verifier"] = verifier,
            ["scope"] = Scope
        }, ct);

        if (string.IsNullOrWhiteSpace(response.RefreshToken))
            throw new InvalidOperationException("Microsoft did not grant offline access. Reconnect with the required permissions.");

        var connection = await db.ManagedOAuthConnections.SingleOrDefaultAsync(x =>
            x.ProjectId == pending.ProjectId && x.Provider == Provider, ct);
        if (connection is null)
        {
            connection = new ManagedOAuthConnectionRow
            {
                Id = Guid.NewGuid(), TenantId = tenant.TenantId,
                ProjectId = pending.ProjectId, Provider = Provider
            };
            db.ManagedOAuthConnections.Add(connection);
        }
        connection.DisplayName = "Microsoft Graph";
        connection.Scopes = Scope;
        connection.AccessToken = encrypt.Protect(response.AccessToken, Purpose);
        connection.RefreshToken = encrypt.Protect(response.RefreshToken, Purpose);
        connection.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn);
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return pending.ProjectId;
    }

    public async Task<IReadOnlyList<ManagedConnectionView>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await CheckAsync(projectId, ct);
        var items = await db.ManagedOAuthConnections.AsNoTracking().Where(x => x.ProjectId == projectId)
            .Select(x => new { x.ProjectId, x.Provider, x.DisplayName, x.Scopes, x.ExpiresAt }).ToListAsync(ct);
        return items.Select(x => new ManagedConnectionView(
            x.ProjectId, x.Provider, x.DisplayName, x.Scopes, x.ExpiresAt)).ToArray();
    }

    public async Task DisconnectAsync(Guid projectId, CancellationToken ct = default)
    {
        await CheckAsync(projectId, ct);
        var items = await db.ManagedOAuthConnections.Where(x => x.ProjectId == projectId).ToListAsync(ct);
        db.ManagedOAuthConnections.RemoveRange(items);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> ResolveForJobAsync(Guid projectId, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("A workspace is required.");
        var options = Settings();
        // Row lock serialises refresh-token rotation when several jobs start concurrently.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.ManagedOAuthConnections.FromSqlInterpolated(
            $"""SELECT * FROM managed_oauth_connections WHERE "TenantId" = {tenant.TenantId} AND "ProjectId" = {projectId} AND "Provider" = {Provider} FOR UPDATE""")
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Connect Microsoft Graph in workspace Connections before running this job.");
        var now = DateTimeOffset.UtcNow;
        if (row.ExpiresAt <= now.AddMinutes(2))
        {
            var token = await ExchangeAsync(options, new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = encrypt.Unprotect(row.RefreshToken, Purpose),
                ["scope"] = Scope
            }, ct);
            row.AccessToken = encrypt.Protect(token.AccessToken, Purpose);
            // Microsoft rotates refresh tokens. Never discard a valid old token if a provider
            // omits the replacement.
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                row.RefreshToken = encrypt.Protect(token.RefreshToken, Purpose);
            row.ExpiresAt = now.AddSeconds(token.ExpiresIn);
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
        var access = encrypt.Unprotect(row.AccessToken, Purpose);
        await transaction.CommitAsync(ct);
        return access;
    }

    private async Task CheckAsync(Guid projectId, CancellationToken ct)
    {
        if (!tenant.IsResolved || !user.IsAuthenticated
            || !await permissions.HasAsync(Permission.SettingsManage, ct)
            || !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new UnauthorizedAccessException("A settings administrator and valid project are required.");
    }

    private ManagedMicrosoftOAuthOptions Settings()
    {
        var options = settings.Value;
        if (string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.ClientSecret)
            || !Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out var redirect)
            || (redirect.Scheme != Uri.UriSchemeHttps && !redirect.IsLoopback)
            || redirect.AbsolutePath != "/api/managed-oauth/microsoft/callback"
            || !string.IsNullOrEmpty(redirect.Query) || !string.IsNullOrEmpty(redirect.Fragment)
            || !(options.Authority == "organizations" || Guid.TryParse(options.Authority, out _)))
            throw new InvalidOperationException("Microsoft OAuth requires a registered HTTPS callback and configured application credentials.");
        return options;
    }

    private async Task<TokenResponse> ExchangeAsync(ManagedMicrosoftOAuthOptions options,
        Dictionary<string, string> fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://login.microsoftonline.com/{options.Authority}/oauth2/v2.0/token")
        { Content = new FormUrlEncodedContent(fields) };
        using var client = clients.CreateClient("managed-microsoft-oauth");
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Microsoft authorization is unavailable or was revoked. Reconnect the account.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var result = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = result.RootElement;
        var access = root.GetProperty("access_token").GetString();
        var refresh = root.TryGetProperty("refresh_token", out var token) ? token.GetString() ?? "" : "";
        var expires = root.GetProperty("expires_in").GetInt32();
        if (string.IsNullOrWhiteSpace(access) || expires is < 60 or > 86400)
            throw new InvalidOperationException("Microsoft returned an invalid connection token.");
        return new(access, refresh, expires);
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
}

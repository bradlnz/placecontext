using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Contracts.Ingestion;
using PlaceContext.Crm.Services;
using PlaceContext.Crm.Infrastructure.Persistence;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Infrastructure.Crm;

/// <summary>
/// Manages the narrowly-scoped public CRM ingestion credential. Only a SHA-256 token digest is
/// persisted; rotating returns the plaintext once. Origin matching is exact after normalization.
/// </summary>
public sealed class CrmIngestionSettingsService : ICrmIngestionSettingsService
{
    public const string TokenHeader = "X-PlaceContext-CRM-Token";
    private readonly CrmDbContext _db;
    private readonly ITenantCatalog _tenants;
    private readonly IProjectRepository _projects;
    private readonly ICurrentTenant _tenant;

    public CrmIngestionSettingsService(
        CrmDbContext db,
        ITenantCatalog tenants,
        IProjectRepository projects,
        ICurrentTenant tenant)
        => (_db, _tenants, _projects, _tenant) = (db, tenants, projects, tenant);

    public async Task<CrmIngestionSettingsView> GetAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        await EnsureProjectAsync(projectId, ct);
        var row = await _db.CrmIngestionSettings.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId, ct);
        return row is null
            ? new CrmIngestionSettingsView(projectId, "", false, null, null)
            : ToView(row);
    }

    public async Task<CrmIngestionSettingsView> SaveOriginAsync(
        Guid projectId,
        string origin,
        CancellationToken ct = default)
    {
        await EnsureProjectAsync(projectId, ct);
        var normalized = NormalizeOrigin(origin);
        var now = DateTimeOffset.UtcNow;
        var row = await _db.CrmIngestionSettings
            .FirstOrDefaultAsync(item => item.ProjectId == projectId, ct);
        if (row is null)
        {
            row = new CrmIngestionSettingsRow
            {
                ProjectId = projectId,
                TenantId = _tenant.TenantId,
                AllowedOrigin = normalized,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await _db.CrmIngestionSettings.AddAsync(row, ct);
        }
        else
        {
            row.AllowedOrigin = normalized;
            row.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return ToView(row);
    }

    public async Task<CrmIngestionTokenResult> RotateAsync(
        Guid projectId,
        string origin,
        CancellationToken ct = default)
    {
        await SaveOriginAsync(projectId, origin, ct);
        var row = await _db.CrmIngestionSettings.SingleAsync(
            item => item.ProjectId == projectId, ct);
        var token = NewToken();
        row.TokenHash = Hash(token);
        row.TokenPrefix = token[..Math.Min(15, token.Length)] + "…";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new CrmIngestionTokenResult(ToView(row), token);
    }

    public async Task DisableAsync(Guid projectId, CancellationToken ct = default)
    {
        await EnsureProjectAsync(projectId, ct);
        var row = await _db.CrmIngestionSettings
            .FirstOrDefaultAsync(item => item.ProjectId == projectId, ct);
        if (row is null) return;
        row.TokenHash = null;
        row.TokenPrefix = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ResolvedCrmIngestion?> ResolveAsync(
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return null;
        var hash = Hash(token.Trim());
        var settings = await _db.CrmIngestionSettings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == hash && item.AllowedOrigin != "", ct);
        if (settings is null) return null;

        var tenant = await _tenants.FindAsync(settings.TenantId, ct);
        return tenant is null
            ? null
            : new ResolvedCrmIngestion(
                settings.ProjectId,
                tenant,
                settings.AllowedOrigin);
    }

    public async Task<bool> IsKnownOriginAsync(string origin, CancellationToken ct = default)
    {
        string normalized;
        try { normalized = NormalizeOrigin(origin); }
        catch (ArgumentException) { return false; }
        return await _db.CrmIngestionSettings.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(item => item.TokenHash != null && item.AllowedOrigin == normalized, ct);
    }

    public static string NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException(
                "Enter an HTTPS origin only, for example https://www.example.com. Localhost HTTP is allowed for development.",
                nameof(origin));
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private async Task EnsureProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty
            || await _projects.GetByIdAsync(ProjectId.From(projectId), ct) is null)
            throw new InvalidOperationException("Project not found.");
    }

    private static CrmIngestionSettingsView ToView(CrmIngestionSettingsRow row)
        => new(row.ProjectId, row.AllowedOrigin, row.TokenHash is not null,
            row.TokenPrefix, row.UpdatedAt);

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(bytes).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
        return "pc_crm_" + encoded;
    }

    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

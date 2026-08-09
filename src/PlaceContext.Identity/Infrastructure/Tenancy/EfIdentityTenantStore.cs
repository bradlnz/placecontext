using Microsoft.EntityFrameworkCore;
using PlaceContext.Identity.Domain.Tenants;
using PlaceContext.Identity.Infrastructure.Persistence;

namespace PlaceContext.Identity.Infrastructure.Tenancy;

public sealed class EfIdentityTenantStore(IdentityDbContext db) : IIdentityTenantStore
{
    public async Task<IdentityTenantDetails?> FindByIdAsync(Guid tenantId, CancellationToken ct = default)
        => ToDetails(await db.Tenants.AsNoTracking().FirstOrDefaultAsync(row => row.Id == tenantId, ct));

    public async Task<IdentityTenantDetails?> FindBySlugAsync(string slug, CancellationToken ct = default)
        => ToDetails(await db.Tenants.AsNoTracking().FirstOrDefaultAsync(row => row.Slug == slug, ct));

    public async Task<IdentityTenantDetails?> FindByCustomerPortalDomainAsync(string domain, CancellationToken ct = default)
    {
        var normalized = NormalizeDomain(domain);
        return normalized is null ? null : ToDetails(await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(row => row.CustomerPortalDomain == normalized, ct));
    }

    public async Task<IdentityTenantDetails> GetOrCreateAsync(string slug, CancellationToken ct = default)
    {
        slug = string.IsNullOrWhiteSpace(slug) ? "default" : slug.Trim().ToLowerInvariant();
        var row = await db.Tenants.FirstOrDefaultAsync(item => item.Slug == slug, ct);
        if (row is not null) return ToDetails(row)!;
        row = new TenantRow
        {
            Id = Guid.NewGuid(), Slug = slug, Name = slug, TimeZoneId = "UTC",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return ToDetails(row)!;
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            var existing = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(item => item.Slug == slug, ct);
            if (existing is null) throw;
            return ToDetails(existing)!;
        }
    }

    public async Task<IReadOnlyList<IdentityTenantDetails>> ListAsync(int take = 1000, CancellationToken ct = default)
        => await db.Tenants.AsNoTracking().OrderBy(row => row.Name).Take(take)
            .Select(row => new IdentityTenantDetails(row.Id, row.Slug, row.Name, row.TimeZoneId,
                row.CustomerPortalDomain, row.CustomerPortalEnabled, row.GitHubLogin, row.GitHubToken))
            .ToListAsync(ct);

    public Task SaveGitHubAsync(Guid tenantId, string githubLogin, string accessToken, CancellationToken ct = default)
        => UpdateAsync(tenantId, row => { row.GitHubLogin = githubLogin; row.GitHubToken = accessToken; }, ct);

    public Task SetTimeZoneAsync(Guid tenantId, string timeZoneId, CancellationToken ct = default)
        => UpdateAsync(tenantId, row => row.TimeZoneId = timeZoneId, ct);

    public Task SetCustomerPortalDomainAsync(Guid tenantId, string? domain, CancellationToken ct = default)
        => UpdateAsync(tenantId, row => row.CustomerPortalDomain = NormalizeDomain(domain), ct);

    public Task SetCustomerPortalEnabledAsync(Guid tenantId, bool enabled, CancellationToken ct = default)
        => UpdateAsync(tenantId, row => row.CustomerPortalEnabled = enabled, ct);

    private async Task UpdateAsync(Guid tenantId, Action<TenantRow> update, CancellationToken ct)
    {
        var row = await db.Tenants.FirstOrDefaultAsync(item => item.Id == tenantId, ct);
        if (row is null) return;
        update(row);
        await db.SaveChangesAsync(ct);
    }

    private static string? NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return null;
        var value = domain.Trim().ToLowerInvariant();
        if (value.Contains('/') || value.Contains(':') || value.Length > 253 || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Use a hostname only, for example crm.example.com.", nameof(domain));
        return value.TrimEnd('.');
    }

    private static IdentityTenantDetails? ToDetails(TenantRow? row) => row is null ? null : new(
        row.Id, row.Slug, row.Name, row.TimeZoneId, row.CustomerPortalDomain,
        row.CustomerPortalEnabled, row.GitHubLogin, row.GitHubToken);
}

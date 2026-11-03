using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Communications;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Comms;

public sealed class EfCommunicationProviderStore(AppDbContext db) : ICommunicationProviderStore
{
    public async Task<IReadOnlyList<CommunicationProviderConfiguration>> ListAsync(CancellationToken ct = default)
        => (await db.CommunicationProviders.AsNoTracking().ToListAsync(ct)).Select(ToConfiguration).ToArray();

    public async Task<CommunicationProviderConfiguration?> GetAsync(Guid id, CancellationToken ct = default)
        => await db.CommunicationProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) is { } row
            ? ToConfiguration(row) : null;

    public async Task AddAsync(CommunicationProviderConfiguration provider, CancellationToken ct = default)
    {
        var row = new CommunicationProviderRow();
        db.Entry(row).CurrentValues.SetValues(provider);
        await db.CommunicationProviders.AddAsync(row, ct);
    }

    public async Task UpdateAsync(CommunicationProviderConfiguration provider, CancellationToken ct = default)
    {
        var row = await db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == provider.Id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");
        db.Entry(row).CurrentValues.SetValues(provider);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");
        db.CommunicationProviders.Remove(row);
    }

    private static CommunicationProviderConfiguration ToConfiguration(CommunicationProviderRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        Channel = row.Channel,
        Kind = row.Kind,
        Name = row.Name,
        Enabled = row.Enabled,
        IsDefault = row.IsDefault,
        UseForTwoFactor = row.UseForTwoFactor,
        AuthType = row.AuthType,
        AuthHeaderName = row.AuthHeaderName,
        VaultProjectId = row.VaultProjectId,
        ApiKeySecretName = row.ApiKeySecretName,
        SettingsJson = row.SettingsJson,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
    };
}

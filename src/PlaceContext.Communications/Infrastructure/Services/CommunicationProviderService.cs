using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Communications.Contracts;
using PlaceContext.Communications.Infrastructure.Persistence;

namespace PlaceContext.Communications.Infrastructure.Providers;

public sealed class CommunicationProviderService(
    CommunicationsDbContext db,
    ICommunicationVaultClient vault) : ICommunicationProviderService
{
    private static readonly string[] Channels = ["email", "sms"];
    private static readonly string[] Kinds = ["postmark", "sendgrid", "twilio"];
    private static readonly string[] AuthTypes = ["none", "bearer", "header", "basic"];

    public async Task<IReadOnlyList<CommunicationProviderView>> ListAsync(CancellationToken ct = default)
        => (await db.Providers.OrderBy(row => row.Channel).ThenBy(row => row.Name).ToListAsync(ct))
            .Select(Map).ToList();

    public async Task<CommunicationProviderView?> GetAsync(Guid id, CancellationToken ct = default)
        => await db.Providers.FirstOrDefaultAsync(row => row.Id == id, ct) is { } row ? Map(row) : null;

    public async Task<CommunicationProviderView> CreateAsync(
        CommunicationProviderInput input,
        CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var now = DateTimeOffset.UtcNow;
        var row = new CommunicationProviderRow { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        Apply(row, input);
        await db.Providers.AddAsync(row, ct);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<CommunicationProviderView> UpdateAsync(
        Guid id,
        CommunicationProviderInput input,
        CancellationToken ct = default)
    {
        var row = await FindAsync(id, ct);
        await ValidateAsync(input, ct);
        Apply(row, input);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        db.Providers.Remove(await FindAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    public async Task<CommunicationProviderView> SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var row = await FindAsync(id, ct);
        foreach (var sibling in await db.Providers
                     .Where(candidate => candidate.Channel == row.Channel && candidate.IsDefault)
                     .ToListAsync(ct))
            sibling.IsDefault = false;
        row.IsDefault = true;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<CommunicationProviderView> SetTwoFactorAsync(
        Guid id,
        bool enabled,
        CancellationToken ct = default)
    {
        var row = await FindAsync(id, ct);
        if (enabled)
            foreach (var sibling in await db.Providers
                         .Where(candidate => candidate.Channel == row.Channel && candidate.UseForTwoFactor)
                         .ToListAsync(ct))
                sibling.UseForTwoFactor = false;
        row.UseForTwoFactor = enabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<IReadOnlyList<string>> TwoFactorChannelsAsync(CancellationToken ct = default)
        => await db.Providers.Where(row => row.Enabled && row.UseForTwoFactor)
            .Select(row => row.Channel).Distinct().ToListAsync(ct);

    public Task<ResolvedCommunicationProvider?> ResolveForSendAsync(
        string channel,
        CancellationToken ct = default)
        => ResolveAsync(channel, twoFactor: false, ct);

    public Task<ResolvedCommunicationProvider?> ResolveForTwoFactorAsync(
        string channel,
        CancellationToken ct = default)
        => ResolveAsync(channel, twoFactor: true, ct);

    public async Task<ResolvedCommunicationProvider> ResolveByIdAsync(
        Guid id,
        CancellationToken ct = default)
        => await ResolveRowAsync(await FindAsync(id, ct), ct);

    private async Task<ResolvedCommunicationProvider?> ResolveAsync(
        string channel,
        bool twoFactor,
        CancellationToken ct)
    {
        var row = twoFactor
            ? await db.Providers.FirstOrDefaultAsync(
                candidate => candidate.Channel == channel && candidate.Enabled && candidate.UseForTwoFactor, ct)
            : null;
        row ??= await db.Providers.FirstOrDefaultAsync(
            candidate => candidate.Channel == channel && candidate.Enabled && candidate.IsDefault, ct);
        return row is null ? null : await ResolveRowAsync(row, ct);
    }

    private async Task<ResolvedCommunicationProvider> ResolveRowAsync(
        CommunicationProviderRow row,
        CancellationToken ct)
    {
        string? secret = null;
        if (row.VaultProjectId is { } projectId && !string.IsNullOrWhiteSpace(row.ApiKeySecretName))
            secret = await vault.ResolveAsync(projectId, row.ApiKeySecretName, ct);
        var requiresSecret = row.AuthType is "bearer" or "header" or "basic";
        return new ResolvedCommunicationProvider(
            row.Id, row.Channel, row.Kind, row.Name, row.AuthType, row.AuthHeaderName,
            secret, !requiresSecret || !string.IsNullOrWhiteSpace(secret), row.SettingsJson);
    }

    private async Task ValidateAsync(CommunicationProviderInput input, CancellationToken ct)
    {
        var channel = input.Channel.Trim().ToLowerInvariant();
        var kind = input.Kind.Trim().ToLowerInvariant();
        var authType = input.AuthType.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Enter a display name for the provider.");
        if (!Channels.Contains(channel)) throw new ArgumentException("Channel must be email or sms.");
        if (!Kinds.Contains(kind)) throw new ArgumentException("Provider kind must be postmark, sendgrid, or twilio.");
        if (kind == "twilio" && channel != "sms") throw new ArgumentException("Twilio providers must use SMS.");
        if (kind is "postmark" or "sendgrid" && channel != "email") throw new ArgumentException("Postmark and SendGrid providers must use email.");
        if (!AuthTypes.Contains(authType)) throw new ArgumentException("Unknown authentication type.");

        JsonElement settings;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(input.SettingsJson) ? "{}" : input.SettingsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Settings must be a JSON object.");
            settings = document.RootElement.Clone();
        }
        catch (JsonException) { throw new ArgumentException("Settings must be valid JSON."); }

        if (authType == "header" && string.IsNullOrWhiteSpace(input.AuthHeaderName))
            throw new ArgumentException("Enter the API-key header name.");
        if (authType == "basic"
            && (!settings.TryGetProperty("accountSid", out var sid)
                || string.IsNullOrWhiteSpace(sid.GetString())))
            throw new ArgumentException("Basic auth requires the Twilio Account SID in settings.");
        if (authType is "bearer" or "header" or "basic")
        {
            if (input.VaultProjectId is not { } projectId || projectId == Guid.Empty
                || string.IsNullOrWhiteSpace(input.ApiKeySecretName))
                throw new ArgumentException("Choose the Vault project and secret containing the provider credential.");
            if (!await vault.ExistsAsync(projectId, input.ApiKeySecretName, ct))
                throw new InvalidOperationException($"Vault secret '{input.ApiKeySecretName}' was not found.");
        }
        if (kind is "postmark" or "sendgrid"
            && (!settings.TryGetProperty("fromEmail", out var from)
                || from.GetString() is not { Length: > 0 } email || !email.Contains('@')))
            throw new ArgumentException("Enter a valid verified sender email address in settings.");
        if (kind == "twilio"
            && (!settings.TryGetProperty("fromNumber", out var number)
                || string.IsNullOrWhiteSpace(number.GetString())))
            throw new ArgumentException("Enter the Twilio sender number in settings.");
    }

    private async Task<CommunicationProviderRow> FindAsync(Guid id, CancellationToken ct)
        => await db.Providers.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");

    private static void Apply(CommunicationProviderRow row, CommunicationProviderInput input)
    {
        row.Channel = input.Channel.Trim().ToLowerInvariant();
        row.Kind = input.Kind.Trim().ToLowerInvariant();
        row.Name = input.Name.Trim();
        row.Enabled = input.Enabled;
        row.AuthType = input.AuthType.Trim().ToLowerInvariant();
        row.AuthHeaderName = string.IsNullOrWhiteSpace(input.AuthHeaderName) ? null : input.AuthHeaderName.Trim();
        row.VaultProjectId = input.VaultProjectId;
        row.ApiKeySecretName = string.IsNullOrWhiteSpace(input.ApiKeySecretName) ? null : input.ApiKeySecretName.Trim();
        row.SettingsJson = string.IsNullOrWhiteSpace(input.SettingsJson) ? "{}" : input.SettingsJson;
    }

    private static CommunicationProviderView Map(CommunicationProviderRow row) => new(
        row.Id, row.Channel, row.Kind, row.Name, row.Enabled, row.IsDefault, row.UseForTwoFactor,
        row.AuthType, row.AuthHeaderName, row.VaultProjectId, row.ApiKeySecretName,
        row.SettingsJson, row.CreatedAt, row.UpdatedAt);
}

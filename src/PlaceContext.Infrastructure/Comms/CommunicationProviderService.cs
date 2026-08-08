using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Infrastructure.Comms;

/// <summary>
/// Tenant-scoped CRUD + resolution for communication providers. Stores only a reference to a
/// project Vault secret; the secret value is decrypted at send time so it never appears in
/// settings storage and Vault rotation takes effect without reconfiguring anything.
/// </summary>
public sealed class CommunicationProviderService
{
    public static readonly string[] Channels = ["email", "sms"];
    public static readonly string[] Kinds = ["postmark", "sendgrid", "twilio"];
    public static readonly string[] AuthTypes = ["none", "bearer", "header", "basic"];

    private readonly AppDbContext _db;
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;

    public CommunicationProviderService(
        AppDbContext db,
        IProjectSecretRepository secrets,
        ISecretProtector protector)
        => (_db, _secrets, _protector) = (db, secrets, protector);

    public async Task<IReadOnlyList<CommunicationProviderView>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.CommunicationProviders
            .OrderBy(x => x.Channel).ThenBy(x => x.Name)
            .ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task<CommunicationProviderView?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToView(row);
    }

    public async Task<CommunicationProviderView> CreateAsync(
        CommunicationProviderInput input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var now = DateTimeOffset.UtcNow;
        var row = new CommunicationProviderRow
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        Apply(row, input);
        await _db.CommunicationProviders.AddAsync(row, ct);
        await _db.SaveChangesAsync(ct);
        return ToView(row);
    }

    public async Task<CommunicationProviderView> UpdateAsync(
        Guid id, CommunicationProviderInput input, CancellationToken ct = default)
    {
        var row = await _db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");
        await ValidateAsync(input, ct);
        Apply(row, input);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToView(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");
        _db.CommunicationProviders.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Per channel: clears <c>IsDefault</c> on siblings, sets it on the target.</summary>
    public async Task<CommunicationProviderView> SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");
        var siblings = await _db.CommunicationProviders
            .Where(x => x.Channel == row.Channel && x.IsDefault)
            .ToListAsync(ct);
        foreach (var sibling in siblings)
            sibling.IsDefault = false;
        row.IsDefault = true;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToView(row);
    }

    /// <summary>Per channel: enabling clears the 2FA flag on siblings (at most one per channel).</summary>
    public async Task<CommunicationProviderView> SetTwoFactorAsync(
        Guid id, bool enabled, CancellationToken ct = default)
    {
        var row = await _db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");
        if (enabled)
        {
            var siblings = await _db.CommunicationProviders
                .Where(x => x.Channel == row.Channel && x.UseForTwoFactor)
                .ToListAsync(ct);
            foreach (var sibling in siblings)
                sibling.UseForTwoFactor = false;
        }
        row.UseForTwoFactor = enabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToView(row);
    }

    /// <summary>The default enabled provider for a channel, or null when none is configured.</summary>
    public Task<ResolvedProvider?> ResolveForSendAsync(string channel, CancellationToken ct = default)
        => ResolveAsync(channel, twoFactor: false, ct);

    /// <summary>
    /// The channels ("email" / "sms") having an enabled provider flagged <c>UseForTwoFactor</c>.
    /// A non-empty result means organisation-wide 2FA is mandatory.
    /// </summary>
    public async Task<IReadOnlyList<string>> TwoFactorChannelsAsync(CancellationToken ct = default)
        => await _db.CommunicationProviders
            .Where(x => x.Enabled && x.UseForTwoFactor)
            .Select(x => x.Channel)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>The 2FA-flagged provider for a channel, falling back to the channel default.</summary>
    public Task<ResolvedProvider?> ResolveForTwoFactorAsync(string channel, CancellationToken ct = default)
        => ResolveAsync(channel, twoFactor: true, ct);

    /// <summary>A specific provider resolved for sending, regardless of default/2FA flags (test sends).</summary>
    public async Task<ResolvedProvider> ResolveByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.CommunicationProviders.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Communication provider not found.");
        return await ResolveRowAsync(row, ct);
    }

    private async Task<ResolvedProvider?> ResolveAsync(
        string channel, bool twoFactor, CancellationToken ct)
    {
        var row = twoFactor
            ? await _db.CommunicationProviders
                .FirstOrDefaultAsync(x => x.Channel == channel && x.Enabled && x.UseForTwoFactor, ct)
            : null;
        row ??= await _db.CommunicationProviders
            .FirstOrDefaultAsync(x => x.Channel == channel && x.Enabled && x.IsDefault, ct);
        if (row is null) return null;
        return await ResolveRowAsync(row, ct);
    }

    private async Task<ResolvedProvider> ResolveRowAsync(CommunicationProviderRow row, CancellationToken ct)
    {
        string? secret = null;
        var resolved = true;
        var requiresSecret = row.AuthType is "bearer" or "header" or "basic";
        if (requiresSecret)
        {
            if (row.VaultProjectId is { } projectId && !string.IsNullOrWhiteSpace(row.ApiKeySecretName))
            {
                var ciphers = await _secrets.GetCiphersAsync(projectId, ct);
                if (ciphers.TryGetValue(row.ApiKeySecretName, out var cipher))
                    secret = _protector.Unprotect(cipher);
            }
            resolved = !string.IsNullOrWhiteSpace(secret);
        }
        return new ResolvedProvider(
            row.Id, row.Channel, row.Kind, row.Name,
            row.AuthType, row.AuthHeaderName,
            secret, resolved, row.SettingsJson);
    }

    private async Task ValidateAsync(CommunicationProviderInput input, CancellationToken ct)
    {
        var channel = input.Channel.Trim().ToLowerInvariant();
        var kind = input.Kind.Trim().ToLowerInvariant();
        var authType = input.AuthType.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("Enter a display name for the provider.");
        if (!Channels.Contains(channel))
            throw new ArgumentException($"Channel must be one of: {string.Join(", ", Channels)}.");
        if (!Kinds.Contains(kind))
            throw new ArgumentException($"Kind must be one of: {string.Join(", ", Kinds)}.");
        if (kind == "twilio" && channel != "sms")
            throw new ArgumentException("Twilio providers must use the SMS channel.");
        if (kind is "postmark" or "sendgrid" && channel != "email")
            throw new ArgumentException("Postmark and SendGrid providers must use the email channel.");
        if (!AuthTypes.Contains(authType))
            throw new ArgumentException($"Auth type must be one of: {string.Join(", ", AuthTypes)}.");

        JsonElement settings;
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(input.SettingsJson) ? "{}" : input.SettingsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Settings must be a JSON object.");
            settings = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ArgumentException("Settings must be valid JSON.");
        }

        if (authType == "header" && string.IsNullOrWhiteSpace(input.AuthHeaderName))
            throw new ArgumentException("Enter the header carrying the API key (e.g. X-Postmark-Server-Token).");
        if (authType == "basic"
            && (!settings.TryGetProperty("accountSid", out var sid) || string.IsNullOrWhiteSpace(sid.GetString())))
            throw new ArgumentException("Basic auth requires the username (Twilio Account SID) in settings.");

        if (authType is "bearer" or "header" or "basic")
        {
            if (input.VaultProjectId is not { } vaultProjectId || vaultProjectId == Guid.Empty)
                throw new ArgumentException("Choose the Vault project containing the provider API key.");
            if (string.IsNullOrWhiteSpace(input.ApiKeySecretName))
                throw new ArgumentException("Choose the Vault secret containing the provider API key.");
            if (!(await _secrets.ListAsync(vaultProjectId, ct))
                .Any(secret => secret.Name == input.ApiKeySecretName))
                throw new InvalidOperationException(
                    $"Vault secret '{input.ApiKeySecretName}' was not found in the selected project.");
        }

        if (kind is "postmark" or "sendgrid")
        {
            if (!settings.TryGetProperty("fromEmail", out var from)
                || from.GetString() is not { Length: > 0 } fromEmail
                || !fromEmail.Contains('@'))
                throw new ArgumentException("Enter a valid verified sender email address in settings.");
        }
        if (kind == "twilio"
            && (!settings.TryGetProperty("fromNumber", out var number)
                || string.IsNullOrWhiteSpace(number.GetString())))
            throw new ArgumentException("Enter the Twilio sender number in settings.");
    }

    private static void Apply(CommunicationProviderRow row, CommunicationProviderInput input)
    {
        row.Channel = input.Channel.Trim().ToLowerInvariant();
        row.Kind = input.Kind.Trim().ToLowerInvariant();
        row.Name = input.Name.Trim();
        row.Enabled = input.Enabled;
        row.AuthType = input.AuthType.Trim().ToLowerInvariant();
        row.AuthHeaderName = string.IsNullOrWhiteSpace(input.AuthHeaderName)
            ? null
            : input.AuthHeaderName.Trim();
        row.VaultProjectId = input.VaultProjectId;
        row.ApiKeySecretName = string.IsNullOrWhiteSpace(input.ApiKeySecretName)
            ? null
            : input.ApiKeySecretName.Trim();
        row.SettingsJson = string.IsNullOrWhiteSpace(input.SettingsJson) ? "{}" : input.SettingsJson;
    }

    private static CommunicationProviderView ToView(CommunicationProviderRow row)
        => new(
            row.Id, row.Channel, row.Kind, row.Name, row.Enabled,
            row.IsDefault, row.UseForTwoFactor, row.AuthType, row.AuthHeaderName,
            row.VaultProjectId, row.ApiKeySecretName, row.SettingsJson,
            row.CreatedAt, row.UpdatedAt);
}

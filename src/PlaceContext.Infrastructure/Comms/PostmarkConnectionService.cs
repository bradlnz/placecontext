using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Comms;

public sealed record PostmarkConnectionStatus(
    bool Configured,
    bool Ready,
    Guid? VaultProjectId,
    string ServerTokenSecretName,
    string FromEmail,
    string FromName,
    string MessageStream,
    DateTimeOffset? ConfiguredAt);

public sealed record PostmarkSendingCredential(
    string ServerToken,
    string FromEmail,
    string FromName,
    string MessageStream);

/// <summary>
/// Stores only a reference to a project Vault secret. The Postmark server token is resolved and
/// decrypted at send time, so it never appears in settings storage and Vault rotation takes effect
/// without reconfiguring CRM.
/// </summary>
public sealed class PostmarkConnectionService
{
    private readonly AppDbContext _db;
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;

    public PostmarkConnectionService(
        AppDbContext db,
        IProjectSecretRepository secrets,
        ISecretProtector protector)
        => (_db, _secrets, _protector) = (db, secrets, protector);

    public async Task<PostmarkConnectionStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var row = await GetRowAsync(ct);
        var secretExists = row is not null
            && (await _secrets.ListAsync(row.VaultProjectId, ct))
                .Any(secret => secret.Name == row.ServerTokenSecretName);
        return new PostmarkConnectionStatus(
            row is not null,
            IsReady(row) && secretExists,
            row?.VaultProjectId,
            row?.ServerTokenSecretName ?? "",
            row?.FromEmail ?? "",
            row?.FromName ?? "PlaceContext",
            row?.MessageStream ?? "outbound",
            row?.ConfiguredAt);
    }

    public async Task SaveSettingsAsync(
        Guid vaultProjectId,
        string serverTokenSecretName,
        string fromEmail,
        string fromName,
        string messageStream,
        CancellationToken ct = default)
    {
        if (vaultProjectId == Guid.Empty)
            throw new ArgumentException("Choose the project containing the Postmark Vault secret.");
        if (string.IsNullOrWhiteSpace(serverTokenSecretName))
            throw new ArgumentException("Choose the Vault secret containing the Postmark Server API Token.");
        if (string.IsNullOrWhiteSpace(fromEmail) || !fromEmail.Contains('@'))
            throw new ArgumentException("Enter a valid verified sender email address.");
        if (!(await _secrets.ListAsync(vaultProjectId, ct))
            .Any(secret => secret.Name == serverTokenSecretName))
            throw new InvalidOperationException(
                $"Vault secret '{serverTokenSecretName}' was not found in the selected project.");

        var now = DateTimeOffset.UtcNow;
        var row = await GetRowAsync(ct);
        if (row is null)
        {
            row = new PostmarkConnectionRow { ConfiguredAt = now };
            await _db.PostmarkConnections.AddAsync(row, ct);
        }
        row.VaultProjectId = vaultProjectId;
        row.ServerTokenSecretName = serverTokenSecretName.Trim();
        row.FromEmail = fromEmail.Trim();
        row.FromName = string.IsNullOrWhiteSpace(fromName) ? "PlaceContext" : fromName.Trim();
        row.MessageStream = string.IsNullOrWhiteSpace(messageStream) ? "outbound" : messageStream.Trim();
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
        => _ = await _db.PostmarkConnections.ExecuteDeleteAsync(ct);

    public async Task<PostmarkSendingCredential?> GetSendingCredentialAsync(CancellationToken ct = default)
    {
        var row = await GetRowAsync(ct);
        if (!IsReady(row)) return null;
        var ciphers = await _secrets.GetCiphersAsync(row!.VaultProjectId, ct);
        if (!ciphers.TryGetValue(row.ServerTokenSecretName, out var cipher))
            throw new InvalidOperationException(
                $"Postmark Vault secret '{row.ServerTokenSecretName}' is no longer available.");
        var token = _protector.Unprotect(cipher);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The Postmark Server API Token Vault secret is empty.");
        return new PostmarkSendingCredential(
            token.Trim(),
            row.FromEmail,
            row.FromName,
            row.MessageStream);
    }

    private async Task<PostmarkConnectionRow?> GetRowAsync(CancellationToken ct)
        => await _db.PostmarkConnections.FirstOrDefaultAsync(ct);

    private static bool IsReady(PostmarkConnectionRow? row)
        => row is not null
            && row.VaultProjectId != Guid.Empty
            && !string.IsNullOrWhiteSpace(row.ServerTokenSecretName)
            && !string.IsNullOrWhiteSpace(row.FromEmail);
}

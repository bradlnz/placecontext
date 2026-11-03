namespace PlaceContext.Application.Communications;

/// <summary>
/// A provider row resolved for sending, with the Vault secret decrypted. <see cref="SecretResolved"/>
/// is false when the row references a secret that no longer exists — sends throw, capabilities
/// report the channel as not ready.
/// </summary>
public sealed record ResolvedProvider(
    Guid Id,
    string Channel,
    string Kind,
    string Name,
    string AuthType,
    string? AuthHeaderName,
    string? Secret,
    bool SecretResolved,
    string SettingsJson)
{
    public bool RequiresSecret => AuthType is "bearer" or "header" or "basic";
}

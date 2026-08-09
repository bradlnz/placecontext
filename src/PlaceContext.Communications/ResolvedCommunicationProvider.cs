namespace PlaceContext.Communications;

public sealed record ResolvedCommunicationProvider(
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

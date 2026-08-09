namespace PlaceContext.Communications.Contracts;

public sealed record CommunicationProviderInput(
    string Channel,
    string Kind,
    string Name,
    bool Enabled,
    string AuthType,
    string? AuthHeaderName,
    Guid? VaultProjectId,
    string? ApiKeySecretName,
    string SettingsJson);

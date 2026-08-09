namespace PlaceContext.Vault.Contracts.Api;

public sealed record ProjectSecretResponse(
    string Name,
    DateTimeOffset CreatedAt,
    string CreatedAtDisplay);

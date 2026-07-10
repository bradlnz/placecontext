namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a vault secret — name + creation time only; the value is never exposed.</summary>
public sealed record ProjectSecretView(string Name, DateTimeOffset CreatedAt);

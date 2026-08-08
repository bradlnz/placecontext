namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectSecretResponse(
    string Name,
    DateTimeOffset CreatedAt,
    string CreatedAtDisplay);

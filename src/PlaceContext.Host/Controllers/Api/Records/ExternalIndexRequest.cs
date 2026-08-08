namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ExternalIndexRequest(
    string Endpoint,
    string Username,
    string Password,
    string Index);

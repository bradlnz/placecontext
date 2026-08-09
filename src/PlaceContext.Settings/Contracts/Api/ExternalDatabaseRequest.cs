namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ExternalDatabaseRequest(
    string Host,
    string Port,
    string Database,
    string Username,
    string Password,
    string SslMode);

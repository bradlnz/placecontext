namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record CreateApiTokenRequest(string Name, int LifetimeDays = 90);

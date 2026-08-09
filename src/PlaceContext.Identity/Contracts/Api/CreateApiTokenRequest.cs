namespace PlaceContext.Identity.Contracts.Api;

public sealed record CreateApiTokenRequest(string Name, int LifetimeDays = 90);

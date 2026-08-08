namespace PlaceContext.Host.Controllers;

public sealed record SaveCrmIngestionSettingsRequest(Guid ProjectId, string AllowedOrigin);

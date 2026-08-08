namespace PlaceContext.Crm.Contracts.Ingestion;

public sealed record SaveCrmIngestionSettingsRequest(Guid ProjectId, string AllowedOrigin);

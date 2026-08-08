namespace PlaceContext.Crm.Contracts.Ingestion;

public sealed record CrmIngestionSettingsView(
    Guid ProjectId,
    string AllowedOrigin,
    bool Enabled,
    string? TokenPrefix,
    DateTimeOffset? UpdatedAt);

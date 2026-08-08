namespace PlaceContext.Crm.Infrastructure.Crm;

public sealed record CrmIngestionSettingsView(
    Guid ProjectId,
    string AllowedOrigin,
    bool Enabled,
    string? TokenPrefix,
    DateTimeOffset? UpdatedAt);

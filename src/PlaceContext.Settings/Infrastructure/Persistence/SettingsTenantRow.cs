namespace PlaceContext.Settings.Infrastructure.Persistence;

public sealed class SettingsTenantRow
{
    public Guid Id { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public string? BrandingJson { get; set; }
    public string? MenuJson { get; set; }
    public string? ArtifactViewJson { get; set; }
}

namespace PlaceContext.Settings.Infrastructure.Persistence;

public sealed class SettingsUserRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public bool IsDefaultAdmin { get; set; }
}

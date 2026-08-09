namespace PlaceContext.Settings.Context;

public interface ISettingsRequestContext
{
    Guid TenantId { get; }
    string TimeZoneId { get; }
    bool IsResolved { get; }
}

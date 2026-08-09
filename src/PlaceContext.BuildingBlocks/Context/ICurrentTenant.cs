namespace PlaceContext.Application.Ports;

public interface ICurrentTenant
{
    Guid TenantId { get; }
    string Slug { get; }
    string TimeZoneId { get; }
    bool IsResolved { get; }
}

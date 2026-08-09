namespace PlaceContext.Application.Ports;

public interface IRequestTenantResolver
{
    Task<TenantContext?> ResolveAsync(string host, CancellationToken ct = default);
}

using PlaceContext.Application.Ports;

namespace PlaceContext.ServiceDefaults;

internal sealed class NullRequestTenantResolver : IRequestTenantResolver
{
    public Task<TenantContext?> ResolveAsync(string host, CancellationToken ct = default)
        => Task.FromResult<TenantContext?>(null);
}

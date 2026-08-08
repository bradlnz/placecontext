using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Runtime;

internal sealed class NullRequestTenantResolver : IRequestTenantResolver
{
    public Task<TenantContext?> ResolveAsync(string host, CancellationToken ct = default)
        => Task.FromResult<TenantContext?>(null);
}

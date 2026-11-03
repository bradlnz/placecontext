using PlaceContext.Application.Jobs;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Host.Auth;

public sealed class HttpJobDataAccess(
    IHttpContextAccessor accessor, ICurrentTenant tenant, IClock clock) : IJobDataAccess
{
    public Guid RequireProjectId()
    {
        var context = accessor.HttpContext;
        var grant = context?.Features.Get<JobDataGrant>();
        if (grant is null || !tenant.IsResolved || grant.TenantId != tenant.TenantId
            || grant.ExpiresAt <= clock.UtcNow
            || context!.User.Identities.All(identity =>
                !identity.IsAuthenticated || identity.AuthenticationType != JobDataAuthenticationHandler.SchemeName))
        {
            throw new UnauthorizedAccessException("An authenticated job data credential is required.");
        }
        return grant.ProjectId;
    }
}

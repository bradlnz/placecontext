using Microsoft.AspNetCore.Authorization;

namespace PlaceContext.Host.Auth;

/// <summary>Authorization requirement restricting an endpoint/page to the tenant's default admin (the
/// bootstrap Owner flagged <c>IsDefaultAdmin</c> on their user row); registered under
/// <see cref="Policies.DefaultAdmin"/> in Program.cs so gating is simply
/// <c>[Authorize(Policy = Policies.DefaultAdmin)]</c>.</summary>
public sealed class DefaultAdminRequirement : IAuthorizationRequirement
{
}

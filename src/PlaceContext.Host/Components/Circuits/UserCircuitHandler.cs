using Microsoft.AspNetCore.Components.Server.Circuits;
using PlaceContext.Application.Tenancy;
using Microsoft.AspNetCore.Components.Authorization;
using PlaceContext.Application.Membership;
using PlaceContext.Host.Tenancy;
using System.Security.Claims;

namespace PlaceContext.Host.Auth;

/// <summary>
/// Re-applies the circuit's resolved user to the ambient <see cref="CurrentUser"/> before each inbound
/// Blazor activity (render/event) — the AsyncLocal set by <see cref="UserResolutionMiddleware"/> during
/// the request that established the circuit does not flow into later circuit callbacks on its own. This
/// mirrors <c>TenantCircuitHandler</c>, which solves the identical problem for the ambient tenant.
/// </summary>
public sealed class UserCircuitHandler : CircuitHandler
{
    private readonly UserHolder _holder;
    private readonly TenantHolder _tenant;
    private readonly IServiceScopeFactory _scopes;
    private readonly AuthenticationStateProvider _authentication;
    public UserCircuitHandler(UserHolder holder, TenantHolder tenant, IServiceScopeFactory scopes,
        AuthenticationStateProvider authentication)
        => (_holder, _tenant, _scopes, _authentication) = (holder, tenant, scopes, authentication);

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
        => async context =>
        {
            CurrentUser.Clear();
            _holder.User = null;
            if (_tenant.Tenant is { } tenant) CurrentTenant.Set(tenant);
            else CurrentTenant.Clear();
            var state = await _authentication.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated == true)
            {
                await using var scope = _scopes.CreateAsyncScope();
                var oldRole = state.User.FindFirstValue(ClaimTypes.Role);
                if (_tenant.Tenant is null || !await PortalSession.ValidateAsync(state.User, _tenant.Tenant,
                    scope.ServiceProvider.GetRequiredService<IMembershipService>()))
                {
                    _holder.User = null;
                    if (_authentication is IHostEnvironmentAuthenticationStateProvider host)
                        host.SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
                    throw new UnauthorizedAccessException("Workspace membership has expired. Sign in again.");
                }
                _holder.User = new UserIdentity(Guid.Parse(state.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
                    state.User.FindFirstValue(ClaimTypes.Role)!);
                CurrentUser.Set(_holder.User);
                if (oldRole != _holder.User.Role && _authentication is IHostEnvironmentAuthenticationStateProvider updated)
                    updated.SetAuthenticationState(Task.FromResult(state));
            }
            await next(context);
        };
}

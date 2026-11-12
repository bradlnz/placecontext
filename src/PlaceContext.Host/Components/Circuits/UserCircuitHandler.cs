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
    private readonly object _dispatchLock = new();
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
                // Inbound activities overlap; refresh a private identity so shared claims remain readable.
                var principal = new ClaimsPrincipal(state.User.Identities.Select(identity => identity.Clone()));
                if (_tenant.Tenant is null || !await PortalSession.ValidateAsync(principal, _tenant.Tenant,
                    scope.ServiceProvider.GetRequiredService<IMembershipService>()))
                {
                    _holder.User = null;
                    if (_authentication is IHostEnvironmentAuthenticationStateProvider host)
                        host.SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
                    throw new UnauthorizedAccessException("Workspace membership has expired. Sign in again.");
                }
                var user = new UserIdentity(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
                    principal.FindFirstValue(ClaimTypes.Role)!);
                _holder.User = user;
                CurrentUser.Set(user);
                if (oldRole != user.Role && _authentication is IHostEnvironmentAuthenticationStateProvider updated)
                {
                    updated.SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));
                }
            }
            Task activity;
            // Async session checks break SignalR's serialized render ACK dispatch. Lock only the
            // synchronous callback: awaiting it under the lock would block JS completions.
            lock (_dispatchLock) activity = next(context);
            await activity;
        };
}

using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Identity.Access;
using PlaceContext.Identity.Auth;
using PlaceContext.Identity.Contracts.Api;
using PlaceContext.Identity.Domain.Tenants;

namespace PlaceContext.Identity.Controllers;

[ApiController]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = IdentityPolicies.DefaultAdmin)]
[Route("api/v1/settings/access")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccessSettingsController(
    IMembershipService membership,
    IIdentityAccessService access,
    IIdentityTenantStore tenantStore,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ControllerBase
{
    private const string ProvisioningKey = "PlaceContext:CustomerPortal:ProvisioningKey";
    private const string ProvisionUsersRoute = "/api/provision/users";

    [HttpGet("context")]
    public async Task<ActionResult<AccessSettingsResponse>> Context(CancellationToken ct)
        => Ok(await BuildContextAsync(ct));

    [HttpPut("customer-portal")]
    public async Task<ActionResult<AccessMessageResponse>> SetCustomerPortalEnabled(
        [FromBody] SetCustomerPortalEnabledRequest request,
        CancellationToken ct)
    {
        await tenantStore.SetCustomerPortalEnabledAsync(tenant.TenantId, request.Enabled, ct);
        return Ok(new AccessMessageResponse(
            request.Enabled
                ? "Customer portal accounts enabled. You can now provision invitations for this tenant."
                : "Customer portal accounts disabled. Existing portal users cannot sign in."));
    }

    [HttpPost("customer-portal/invitations")]
    public async Task<ActionResult<AccessMessageResponse>> InviteCustomerPortalUser(
        [FromBody] CustomerPortalInviteRequest request,
        CancellationToken ct)
    {
        if (!IsValidEmail(request.Email))
            return BadRequest(new { error = "Enter a valid customer email." });

        var tenantRow = await tenantStore.FindByIdAsync(tenant.TenantId, ct);
        var key = configuration[ProvisioningKey];
        if (tenantRow is null || !tenantRow.CustomerPortalEnabled)
            return BadRequest(new { error = "Enable the customer portal before inviting users." });
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "Customer portal provisioning is not configured." });

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(BuildPortalHost(tenantRow.CustomerPortalDomain).TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("X-PlaceContext-Provisioning-Key", key);
        client.DefaultRequestHeaders.Add("X-PlaceContext-Tenant-Id", tenant.TenantId.ToString());
        using var response = await client.PostAsJsonAsync(
            ProvisionUsersRoute,
            new { email = request.Email.Trim(), role = request.Role },
            ct);
        if (!response.IsSuccessStatusCode)
            return BadRequest(new { error = $"Portal invitation failed ({(int)response.StatusCode})." });

        return Ok(new AccessMessageResponse($"Invitation sent to {request.Email.Trim()}."));
    }

    [HttpPost("invitations")]
    public async Task<ActionResult<MemberInviteResponse>> InviteMember(
        [FromBody] MemberInviteRequest request,
        CancellationToken ct)
    {
        if (!IsValidEmail(request.Email))
            return BadRequest(new { error = "Enter a valid email." });

        try
        {
            var invite = await membership.CreateInviteAsync(request.Email.Trim(), request.Role, ct);
            var origin = $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
            return Ok(new MemberInviteResponse(
                invite.Email,
                $"{origin}/join?token={Uri.EscapeDataString(invite.Token)}"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("members/{userId:guid}/role")]
    public async Task<IActionResult> SetMemberRole(
        Guid userId,
        [FromBody] SetMemberRoleRequest request,
        CancellationToken ct)
    {
        try
        {
            await membership.SetRoleAsync(userId, request.Role, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("members/{userId:guid}")]
    public async Task<IActionResult> DeleteMember(Guid userId, CancellationToken ct)
    {
        try
        {
            await membership.DeleteMemberAsync(userId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("members/{userId:guid}/permissions")]
    public async Task<ActionResult<UserPermissionsView>> GetMemberPermissions(
        Guid userId,
        CancellationToken ct)
        => Ok(await access.GetUserPermissionsAsync(userId, ct));

    [HttpPut("members/{userId:guid}/permission")]
    public async Task<ActionResult<UserPermissionsView>> SetMemberPermission(
        Guid userId,
        [FromBody] SetPermissionOverrideRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await access.SetUserPermissionOverrideAsync(
                userId,
                request.Permission,
                request.Allowed,
                ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("roles")]
    public async Task<ActionResult<RoleView>> CreateRole(
        [FromBody] CreateRoleRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await access.CreateRoleAsync(request.Name.Trim(), request.Permissions, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("roles/{roleId:guid}/permissions")]
    public async Task<ActionResult<RoleView>> UpdateRolePermissions(
        Guid roleId,
        [FromBody] UpdateRolePermissionsRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await access.UpdateRolePermissionsAsync(roleId, request.Permissions, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("roles/{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid roleId, CancellationToken ct)
        => await access.DeleteRoleAsync(roleId, ct)
            ? NoContent()
            : NotFound(new { error = "Role not found." });

    private async Task<AccessSettingsResponse> BuildContextAsync(CancellationToken ct)
    {
        var members = await membership.ListMembersAsync(ct);
        var roles = await access.ListRolesAsync(ct);
        var portalEnabled = (await tenantStore.FindByIdAsync(tenant.TenantId, ct))?.CustomerPortalEnabled == true;
        return new AccessSettingsResponse(
            members.Select(member => new AccessMemberResponse(
                member.Id,
                member.Email,
                member.DisplayName,
                member.Role,
                member.IsDefaultAdmin,
                member.CreatedAt)).ToList(),
            roles.Select(role => new AccessRoleResponse(
                role.Id,
                role.Name,
                role.IsSystem,
                role.Permissions,
                role.MemberCount)).ToList(),
            Permission.All,
            portalEnabled,
            currentUser.UserId);
    }

    private string BuildPortalHost(string? domain)
    {
        if (!string.IsNullOrWhiteSpace(domain))
            return $"https://{domain.Trim()}";

        var slug = string.IsNullOrWhiteSpace(tenant.Slug) ? "tenant" : tenant.Slug.Trim();
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}/p/{slug}";
    }

    private static bool IsValidEmail(string email)
        => !string.IsNullOrWhiteSpace(email) && email.Contains('@');
}

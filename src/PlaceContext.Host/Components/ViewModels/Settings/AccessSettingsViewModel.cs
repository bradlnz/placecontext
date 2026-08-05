using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class AccessSettingsViewModel(
    IServiceScopeFactory ScopeFactory,
    ICurrentUser CurrentUser,
    PortalUiState Ui,
    NavigationManager Nav,
    IJSRuntime JS,
    ITenantStore TenantStore,
    ICurrentTenant Tenant,
    IHttpClientFactory HttpClientFactory,
    IConfiguration Configuration
) : PageViewModel
{
    public const string PageRoute = "/settings/access";
    public const string ProvisioningKey = "PlaceContext:CustomerPortal:ProvisioningKey";
    public const string ProvisionUsersRoute = "/api/provision/users";
    public const string ScrollFunction = "placecontext.scrollToElement";
    public IReadOnlyList<string> Permissions => Permission.All;

    // Owner is not assignable via invite/role dropdown — ownership transfer is a separate path.
    public IEnumerable<RoleView> AssignableRoles => Roles.Where(r => !IsOwnerRole(r));

    public bool IsOwnerRole(RoleView role) =>
        string.Equals(role.Name, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<MemberView> Members = Array.Empty<MemberView>();
    public IReadOnlyList<RoleView> Roles = Array.Empty<RoleView>();
    public bool Loading = true;
    public bool CustomerPortalEnabled;
    public bool PortalBusy;
    public string? PortalMessage;
    public string PortalInviteEmail = "";
    public string PortalInviteRole = "member";
    public bool PortalInviting;
    public bool Busy;
    public string? Message;

    public string MemberSince(DateTimeOffset value) => Presentation.Date(value);

    public string InviteEmail = "";
    public string InviteRole = nameof(UserRole.Member);
    public bool Inviting;
    public string? InviteLink;
    public string? InviteError;

    public Guid? Expanded;
    public UserPermissionsView? Matrix;
    public Guid? ConfirmRemoveId;
    public string? PendingScrollId;

    public Guid? EditRoleId;
    public HashSet<string> EditRolePerms = new();
    public Guid? ConfirmDeleteRoleId;
    public string NewRoleName = "";
    public readonly HashSet<string> NewRolePerms = new();

    public async Task LoadAsync()
    {
        Ui.Set("Settings", "Access");
        await LoadDataAsync();
    }

    public async Task AfterRenderAsync(bool firstRender)
    {
        // Scroll after the render that expanded the member's card, so the card is in the DOM.
        if (PendingScrollId is { } elementId)
        {
            PendingScrollId = null;
            try
            {
                await JS.InvokeVoidAsync(ScrollFunction, elementId);
            }
            catch (InvalidOperationException) { } // JS interop unavailable during prerender
            catch (JSException) { }
        }
    }

    public async Task LoadDataAsync()
    {
        Loading = true;
        try
        {
            Members = await InScopeAsync<IMembershipService, IReadOnlyList<MemberView>>(service =>
                service.ListMembersAsync()
            );
            Roles = await InScopeAsync<IPlaceContextService, IReadOnlyList<RoleView>>(service =>
                service.ListRolesAsync()
            );
            CustomerPortalEnabled =
                (await TenantStore.GetRowAsync(Tenant.TenantId))?.CustomerPortalEnabled == true;
        }
        finally
        {
            Loading = false;
        }
    }

    public async Task SetCustomerPortalEnabledAsync(ChangeEventArgs args)
    {
        PortalBusy = true;
        PortalMessage = null;
        try
        {
            CustomerPortalEnabled = args.Value is bool value && value;
            await TenantStore.SetCustomerPortalEnabledAsync(Tenant.TenantId, CustomerPortalEnabled);
            PortalMessage = CustomerPortalEnabled
                ? "Customer portal accounts enabled. You can now provision invitations for this tenant."
                : "Customer portal accounts disabled. Existing portal users cannot sign in.";
        }
        catch (Exception ex)
        {
            PortalMessage = ex.Message;
        }
        finally
        {
            PortalBusy = false;
        }
    }

    public async Task InviteCustomerPortalUserAsync()
    {
        if (string.IsNullOrWhiteSpace(PortalInviteEmail) || !PortalInviteEmail.Contains('@'))
        {
            PortalMessage = "Enter a valid customer email.";
            return;
        }

        PortalInviting = true;
        try
        {
            var tenant = await TenantStore.GetRowAsync(Tenant.TenantId);
            var key = Configuration[ProvisioningKey];
            if (tenant is null || string.IsNullOrWhiteSpace(tenant.CustomerPortalDomain))
                throw new InvalidOperationException(
                    "Configure the customer portal domain before inviting users."
                );
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException(
                    "Customer portal provisioning is not configured."
                );

            var client = HttpClientFactory.CreateClient();
            client.BaseAddress = new Uri($"https://{tenant.CustomerPortalDomain}");
            client.DefaultRequestHeaders.Add("X-PlaceContext-Provisioning-Key", key);
            client.DefaultRequestHeaders.Add(
                "X-PlaceContext-Tenant-Id",
                Tenant.TenantId.ToString()
            );
            using var response = await client.PostAsJsonAsync(
                ProvisionUsersRoute,
                new { email = PortalInviteEmail.Trim(), role = PortalInviteRole }
            );
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Portal invitation failed ({(int)response.StatusCode})."
                );

            PortalMessage = $"Invitation sent to {PortalInviteEmail.Trim()}.";
            PortalInviteEmail = "";
        }
        catch (Exception ex)
        {
            PortalMessage = ex.Message;
        }
        finally
        {
            PortalInviting = false;
        }
    }

    public async Task LoadMembersAsync() =>
        Members = await InScopeAsync<IMembershipService, IReadOnlyList<MemberView>>(service =>
            service.ListMembersAsync()
        );

    public async Task LoadRolesAsync() =>
        Roles = await InScopeAsync<IPlaceContextService, IReadOnlyList<RoleView>>(service =>
            service.ListRolesAsync()
        );

    public async Task InviteAsync()
    {
        InviteError = null;
        InviteLink = null;
        if (string.IsNullOrWhiteSpace(InviteEmail) || !InviteEmail.Contains('@'))
        {
            InviteError = "Enter a valid email.";
            return;
        }
        Inviting = true;
        try
        {
            var invite = await InScopeAsync<IMembershipService, InviteView>(service =>
                service.CreateInviteAsync(InviteEmail.Trim(), InviteRole)
            );
            InviteLink = $"{Nav.BaseUri.TrimEnd('/')}/join?token={invite.Token}";
            InviteEmail = "";
            Message = $"Invite sent for {invite.Email}.";
        }
        catch (Exception ex)
        {
            InviteError = ex.Message;
        }
        finally
        {
            Inviting = false;
        }
    }

    public async Task SetRoleAsync(Guid userId, string role)
    {
        Busy = true;
        try
        {
            await InScopeAsync<IMembershipService>(service => service.SetRoleAsync(userId, role));
            await LoadMembersAsync();
            await LoadRolesAsync(); // member counts changed
            if (Expanded == userId)
                await LoadMatrixAsync(userId);
            Message = "Role updated.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    public Task SetRoleAsync(Guid userId, ChangeEventArgs args) =>
        SetRoleAsync(userId, args.Value as string ?? string.Empty);

    public bool CanRemoveMember(MemberView m) =>
        !m.IsDefaultAdmin
        && !string.Equals(m.Role, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase)
        && m.Id != CurrentUser.UserId;

    public async Task RemoveMemberAsync(Guid userId)
    {
        Busy = true;
        try
        {
            await InScopeAsync<IMembershipService>(service => service.DeleteMemberAsync(userId));
            ConfirmRemoveId = null;
            if (Expanded == userId)
            {
                Expanded = null;
                Matrix = null;
            }
            await LoadMembersAsync();
            await LoadRolesAsync(); // member counts changed
            Message = "Member removed.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task ManageMemberAsync(ChangeEventArgs e)
    {
        if (!Guid.TryParse(e.Value?.ToString(), out var userId))
            return;
        if (Expanded != userId)
        {
            Expanded = userId;
            await LoadMatrixAsync(userId);
        }
        PendingScrollId = $"member-{userId}";
    }

    public async Task ToggleMatrixAsync(Guid userId)
    {
        if (Expanded == userId)
        {
            Expanded = null;
            Matrix = null;
            return;
        }
        Expanded = userId;
        await LoadMatrixAsync(userId);
    }

    public async Task LoadMatrixAsync(Guid userId)
    {
        Matrix = null;
        try
        {
            Matrix = await InScopeAsync<IPlaceContextService, UserPermissionsView>(service =>
                service.GetUserPermissionsAsync(userId)
            );
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    public async Task SetOverrideAsync(Guid userId, string permission, bool? allowed)
    {
        Busy = true;
        try
        {
            Matrix = await InScopeAsync<IPlaceContextService, UserPermissionsView>(service =>
                service.SetUserPermissionOverrideAsync(userId, permission, allowed)
            );
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    // ── Roles & permissions ─────────────────────────────────────────────────────

    public string PermissionSummary(RoleView role) =>
        role.Permissions.Count == 0 ? "No permissions granted"
        : role.Permissions.Count == Permission.All.Count
            ? $"All permissions ({Permission.All.Count})"
        : string.Join(", ", role.Permissions);

    public void TogglePerm(HashSet<string> set, string permission, object? value)
    {
        if (value is true)
            set.Add(permission);
        else
            set.Remove(permission);
    }

    public void ToggleRoleEdit(RoleView role)
    {
        if (EditRoleId == role.Id)
        {
            EditRoleId = null;
            return;
        }
        EditRoleId = role.Id;
        EditRolePerms = new HashSet<string>(role.Permissions);
        ConfirmDeleteRoleId = null;
    }

    public async Task SaveRolePermissionsAsync(Guid roleId)
    {
        Busy = true;
        try
        {
            await InScopeAsync<IPlaceContextService>(service =>
                service.UpdateRolePermissionsAsync(roleId, EditRolePerms.ToList())
            );
            EditRoleId = null;
            await LoadRolesAsync();
            Message = "Role permissions updated.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task DeleteRoleAsync(Guid roleId)
    {
        Busy = true;
        try
        {
            await InScopeAsync<IPlaceContextService>(service => service.DeleteRoleAsync(roleId));
            ConfirmDeleteRoleId = null;
            await LoadRolesAsync();
            Message = "Role deleted.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task CreateRoleAsync()
    {
        Busy = true;
        try
        {
            await InScopeAsync<IPlaceContextService>(service =>
                service.CreateRoleAsync(NewRoleName.Trim(), NewRolePerms.ToList())
            );
            NewRoleName = "";
            NewRolePerms.Clear();
            await LoadRolesAsync();
            Message = "Role created.";
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task<TResult> InScopeAsync<TService, TResult>(
        Func<TService, Task<TResult>> operation
    )
        where TService : notnull
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task InScopeAsync<TService>(Func<TService, Task> operation)
        where TService : notnull
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@');

    public static bool CanRemove(MemberView member, Guid currentUserId) =>
        !member.IsDefaultAdmin
        && !string.Equals(member.Role, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase)
        && member.Id != currentUserId;
}

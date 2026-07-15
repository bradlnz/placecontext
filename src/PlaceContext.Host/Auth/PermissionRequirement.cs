using Microsoft.AspNetCore.Authorization;

namespace PlaceContext.Host.Auth;

/// <summary>Authorization requirement for one fine-grained permission (see
/// <see cref="PlaceContext.Application.Ports.Permission"/>); registered under a same-named policy in
/// Program.cs so the pattern to gate a new sensitive tool/endpoint/page is simply
/// <c>[Authorize(Policy = Permission.X)]</c>.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

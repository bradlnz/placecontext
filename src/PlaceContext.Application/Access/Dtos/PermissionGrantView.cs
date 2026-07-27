namespace PlaceContext.Application.Dtos;

/// <summary>One permission's state for a member: whether their role grants it by default, any explicit
/// override (true = allow, false = revoke, null = inherit the role default), and the resulting
/// effective grant. Drives one row of the Access settings admin UI's permission matrix.</summary>
public sealed record PermissionGrantView(string Permission, bool DefaultAllowed, bool? Override, bool Effective);

/// <summary>A member's full permission matrix (the whole catalog) — role plus every permission's state.</summary>
public sealed record UserPermissionsView(Guid UserId, string Role, IReadOnlyList<PermissionGrantView> Permissions);

namespace PlaceContext.Application.Dtos;

/// <summary>A member's full permission matrix (the whole catalog) — role plus every permission's state.</summary>
public sealed record UserPermissionsView(Guid UserId, string Role, IReadOnlyList<PermissionGrantView> Permissions);

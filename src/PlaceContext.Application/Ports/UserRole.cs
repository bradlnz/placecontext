namespace PlaceContext.Application.Ports;

/// <summary>
/// A member's role within an organisation. The coarse ladder used by legacy policies is
/// Viewer's role and above, while <see cref="CrmUser"/> is a separate constrained role that does not
/// participate in that ladder.
/// </summary>
public enum UserRole
{
    CrmUser = -1,
    Viewer = 0,
    Member = 1,
    Admin = 2,
    Owner = 3,
}

namespace PlaceContext.Application.Ports;

/// <summary>
/// A member's role within an organisation, ordered by privilege so "at least Member" is a simple
/// comparison: Viewer (read-only) &lt; Member (does the work) &lt; Admin (manages members + settings) &lt;
/// Owner (everything, incl. the org itself).
/// </summary>
public enum UserRole
{
    Viewer = 0,
    Member = 1,
    Admin = 2,
    Owner = 3,
}

namespace PlaceContext.Application.Ports;

/// <summary>
/// An authenticated portal user (a member of one organisation), with their role. The role is carried
/// as its name (a <c>role_definitions</c> row or a <see cref="UserRole"/> enum name) rather than the
/// enum itself, so members holding custom roles round-trip through sign-in and claims unchanged.
/// </summary>
public sealed record AuthUser(Guid Id, Guid TenantId, string Email, string DisplayName, string Role);

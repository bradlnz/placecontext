namespace PlaceContext.Identity.Contracts.Api;

public sealed record AccessMemberResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsDefaultAdmin,
    DateTimeOffset CreatedAt);

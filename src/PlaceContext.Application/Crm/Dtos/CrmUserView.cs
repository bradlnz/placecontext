namespace PlaceContext.Application.Features;

public sealed record CrmUserView(
    Guid Id,
    Guid ProjectId,
    string? Name,
    string Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CrmUserCreationResult(
    CrmUserView User,
    string JoinCode,
    Guid? AssignedClientId,
    bool EmailSent,
    bool EmailProviderAvailable,
    string? EmailError = null);

public sealed record CrmOnboardingResult(
    Guid ProjectId,
    string Email);

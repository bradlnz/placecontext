namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one member of the organisation.</summary>
public sealed record MemberView(Guid Id, string Email, string DisplayName, string Role, bool IsDefaultAdmin, DateTimeOffset CreatedAt);

namespace PlaceContext.Agents.Contracts.Dtos;

public sealed record StaffMemberView(
    Guid Id, Guid ProfileId, string Name, IReadOnlyList<Guid> ProjectIds,
    string InstructionsOverride, string? ModelOverride, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

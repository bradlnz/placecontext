namespace PlaceContext.Application.Dtos;

public sealed record ChatCommandView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string ToolName,
    string? Args,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record CreateChatCommandCommand(
    Guid ProjectId,
    string Name,
    string? Description,
    string ToolName,
    string? Args) : ICommand<ChatCommandView>;

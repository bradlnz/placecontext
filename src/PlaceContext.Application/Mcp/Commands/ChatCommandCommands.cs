using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record CreateChatCommandCommand(
    Guid ProjectId,
    string Name,
    string? Description,
    string ToolName,
    string? Args) : ICommand<ChatCommandView>;

public sealed record UpdateChatCommandCommand(
    Guid Id,
    string Name,
    string? Description,
    string ToolName,
    string? Args) : ICommand<ChatCommandView>;

public sealed record DeleteChatCommandCommand(Guid Id) : ICommand<bool>;

public sealed record ListChatCommandsQuery(Guid ProjectId) : IQuery<IReadOnlyList<ChatCommandView>>;

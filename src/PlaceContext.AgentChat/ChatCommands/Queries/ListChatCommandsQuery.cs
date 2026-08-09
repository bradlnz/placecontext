using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record ListChatCommandsQuery(Guid ProjectId) : IQuery<IReadOnlyList<ChatCommandView>>;

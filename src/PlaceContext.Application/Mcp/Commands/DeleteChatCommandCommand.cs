using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record DeleteChatCommandCommand(Guid Id) : ICommand<bool>;

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record DeleteMcpConnectionCommand(Guid Id) : ICommand<bool>;

using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record DeleteJobChainCommand(Guid ChainId) : ICommand<bool>;

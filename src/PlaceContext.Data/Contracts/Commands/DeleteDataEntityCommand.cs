using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record DeleteDataEntityCommand(Guid EntityId) : ICommand<bool>;

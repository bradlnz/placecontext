using PlaceContext.Application.Cqrs;

namespace PlaceContext.Search.Contracts.Commands;

public sealed record IndexRunOutputCommand(
    Guid RunId,
    Guid JobId,
    Guid ProjectId,
    string Text) : ICommand<bool>;

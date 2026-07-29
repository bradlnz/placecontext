using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record UpdateTriggerCommand(
    Guid TriggerId,
    string? Name,
    string? CronExpression,
    string? EventName,
    bool? Enabled) : ICommand<TriggerView>;

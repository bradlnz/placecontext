using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates a trigger on a job. For <c>Kind == "Schedule"</c> supply a cron <see cref="CronExpression"/>;
/// for <c>Kind == "Event"</c> supply the <see cref="EventName"/> to subscribe to. The project is
/// inferred from the job.
/// </summary>
public sealed record CreateTriggerCommand(
    Guid JobId,
    string Name,
    string Kind,
    string? CronExpression,
    string? EventName) : ICommand<TriggerView>;

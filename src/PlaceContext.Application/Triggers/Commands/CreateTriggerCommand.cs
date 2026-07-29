using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates a trigger. For <c>Kind == "Schedule"</c> supply a cron <see cref="CronExpression"/> and a
/// <see cref="JobId"/>; for <c>Kind == "Event"</c> supply the <see cref="EventName"/> to subscribe to
/// and a <see cref="JobId"/>; for <c>Kind == "Launchpad"</c> supply a cron <see cref="CronExpression"/>,
/// the target <see cref="ChainId"/>, and the <see cref="Prompt"/> the agent session runs (JobId is
/// unused). For <c>Kind == "Command"</c> supply a cron <see cref="CronExpression"/> and a
/// <see cref="CommandId"/>. The project is inferred from the job, chain, or command.
/// </summary>
public sealed record CreateTriggerCommand(
    Guid? JobId,
    string Name,
    string Kind,
    string? CronExpression,
    string? EventName,
    Guid? ChainId = null,
    string? SourceTable = null,
    string? Prompt = null,
    Guid? CommandId = null) : ICommand<TriggerView>;

namespace PlaceContext.Domain.ValueObjects;

public sealed record AgentActivity(DateTimeOffset At, string Kind, string Message,
    int? Turn = null, string? Tool = null, Guid? JobId = null, Guid? RunId = null,
    double? DurationSeconds = null)
{
    public const int RetainedEvents = 400;
}

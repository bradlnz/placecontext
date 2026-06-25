namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// The system-raised domain events a <see cref="PlaceContext.Domain.Entities.JobTrigger"/> can subscribe
/// to by name, alongside any user-defined event types. These names are emitted from the relevant
/// Application handlers whenever the corresponding happening occurs.
/// </summary>
public static class BuiltInEvents
{
    /// <summary>Raised after a change is recorded into a project's activity log.</summary>
    public const string ActivityRecorded = "activity.recorded";

    /// <summary>Raised after a project's risk is recomputed.</summary>
    public const string RiskRecomputed = "risk.recomputed";

    /// <summary>Raised after a job run finishes (any status).</summary>
    public const string JobCompleted = "job.completed";

    /// <summary>One built-in event name paired with a human description, for listings.</summary>
    public sealed record Definition(string Name, string Description);

    /// <summary>All built-in event names, in display order.</summary>
    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(ActivityRecorded, "A change was recorded into a project's activity log."),
        new Definition(RiskRecomputed, "A project's risk was recomputed."),
        new Definition(JobCompleted, "A job run finished (any status)."),
    };

    /// <summary>True when <paramref name="name"/> is a reserved built-in event name.</summary>
    public static bool IsBuiltIn(string name) =>
        All.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
}

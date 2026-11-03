namespace PlaceContext.Domain.ValueObjects;

/// <summary>Lifecycle of one stage within a chain run.</summary>
public enum ChainStepStatus
{
    Pending,
    Running,
    Succeeded,
    Partial,
    Failed,
    /// <summary>Never started — an earlier stage failed.</summary>
    Skipped,
    /// <summary>Cancelled by a user.</summary>
    Cancelled,
}

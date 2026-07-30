namespace PlaceContext.Domain.ValueObjects;

/// <summary>Lifecycle of a whole chain run.</summary>
public enum ChainRunStatus
{
    Running,
    Succeeded,
    /// <summary>Every step ran, but at least one was Partial.</summary>
    Partial,
    Failed,
    /// <summary>The chain run was cancelled by a user.</summary>
    Cancelled,
}

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

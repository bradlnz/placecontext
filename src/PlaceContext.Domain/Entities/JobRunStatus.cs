namespace PlaceContext.Domain.Entities;

/// <summary>Lifecycle status of a <see cref="JobRun"/>.</summary>
public enum JobRunStatus
{
    /// <summary>Created but not yet started.</summary>
    Queued,
    /// <summary>Map or reduce containers are executing.</summary>
    Running,
    /// <summary>All shards and (if present) the reduce step completed with success exit codes.</summary>
    Succeeded,
    /// <summary>One or more shards returned a partial exit code; no shards failed outright.</summary>
    Partial,
    /// <summary>One or more shards returned a failed exit code, or the reduce step failed.</summary>
    Failed,
}

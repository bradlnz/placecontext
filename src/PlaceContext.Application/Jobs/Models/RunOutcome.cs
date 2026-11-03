namespace PlaceContext.Application.Jobs;

/// <summary>Where a run is in its lifecycle, as surfaced to notification consumers.</summary>
public enum RunOutcome
{
    Running, NeedsInput, Succeeded, Partial, Failed, Cancelled
}

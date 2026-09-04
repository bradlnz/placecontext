using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Per-tenant watcher memory, owned by the background loop and threaded through every sweep.
/// In-memory by design — it mirrors the per-replica notification ledger it feeds, and after a
/// restart the first sweep rebuilds it from the persisted runs.
/// </summary>
public sealed class RunWatchState
{
    /// <summary>Terminal-status watermark for the reader query; default triggers the startup lookback.</summary>
    public DateTimeOffset Cursor { get; set; }

    /// <summary>Job runs already notified as Running.</summary>
    public HashSet<Guid> JobRuns { get; } = new();

    /// <summary>Chain runs already notified as Running → their last progress fingerprint.</summary>
    public Dictionary<Guid, string> Chains { get; } = new();

    /// <summary>Runs whose terminal status was already notified (the cursor overlap re-reads them).</summary>
    public Dictionary<Guid, DateTimeOffset> NotifiedTerminal { get; } = new();

    /// <summary>Job-run ids owned by a chain step → when last seen; suppressed from standalone notifications.</summary>
    public Dictionary<Guid, DateTimeOffset> ChainStepRuns { get; } = new();
}

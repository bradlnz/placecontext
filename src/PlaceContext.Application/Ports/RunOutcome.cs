namespace PlaceContext.Application.Ports;

/// <summary>Where a run is in its lifecycle, as surfaced to notification consumers.</summary>
public enum RunOutcome { Running, Succeeded, Partial, Failed, Cancelled }

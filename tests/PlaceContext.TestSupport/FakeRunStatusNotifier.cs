using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>Records every synced run-status update so tests can assert transitions and dedupe.</summary>
public sealed class FakeRunStatusNotifier : IRunStatusNotifier
{
    public List<RunStatusUpdate> Synced { get; } = new();

    public void Sync(RunStatusUpdate update) => Synced.Add(update);
}

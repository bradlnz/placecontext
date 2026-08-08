namespace PlaceContext.Application.Ports;

/// <summary>
/// Push-side port for the run-status watcher: upserts a run's current status into whatever
/// surfaces notifications (the portal's operation ledger). Updates reflect persisted state, so
/// implementations treat them as authoritative over caller-side progress marks.
/// </summary>
public interface IRunStatusNotifier
{
    void Sync(RunStatusUpdate update);
}

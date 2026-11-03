namespace PlaceContext.Application.DataSources;

public sealed record SourceSyncResult(Guid SourceId, int Total, int Changed, int Deleted, DateTimeOffset SyncedAt, int Held = 0, int Queued = 0);

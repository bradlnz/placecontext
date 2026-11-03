using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record WriteJobSourceRecordsCommand(IReadOnlyList<SourceRecord> Records, IReadOnlyList<string> DeletedKeys) : ICommand<SourceSyncResult>;

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

// Feed authentication is source-scoped and checked by the handler before any data is processed.
public sealed record ReceiveDataFeedCommand(Guid ProjectId, Guid SourceId, string Token, IReadOnlyList<SourceRecord> Records,
    IReadOnlyList<string> DeletedKeys) : ICommand<SourceSyncResult>;

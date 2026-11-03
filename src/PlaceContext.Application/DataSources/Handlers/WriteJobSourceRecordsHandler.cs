using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Jobs;

namespace PlaceContext.Application.Features;

public sealed class WriteJobSourceRecordsHandler(DataSourcePipeline pipeline, IJobDataAccess access)
    : ICommandHandler<WriteJobSourceRecordsCommand, SourceSyncResult>
{
    public Task<SourceSyncResult> HandleAsync(WriteJobSourceRecordsCommand request, CancellationToken ct = default)
        => pipeline.IngestAsync(access.RequireProjectId(), access.RequireSourceId(), request.Records, request.DeletedKeys, ct);
}

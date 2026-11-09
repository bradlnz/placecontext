using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Jobs;

namespace PlaceContext.Application.Features;

public sealed class SyncJobDataSourceHandler(DataSourcePipeline pipeline, IJobDataAccess access) : ICommandHandler<SyncJobDataSourceCommand, SourceSyncResult>
{
    public async Task<SourceSyncResult> HandleAsync(SyncJobDataSourceCommand request, CancellationToken ct = default)
    {
        return await pipeline.SyncAsync(access.RequireProjectId(), access.RequireSourceId(), ct);
    }
}

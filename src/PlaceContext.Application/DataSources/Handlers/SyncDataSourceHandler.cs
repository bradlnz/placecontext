using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed class SyncDataSourceHandler(DataSourcePipeline pipeline) : ICommandHandler<SyncDataSourceCommand, SourceSyncResult>
{
    public async Task<SourceSyncResult> HandleAsync(SyncDataSourceCommand request, CancellationToken ct = default)
    {
        return await pipeline.SyncAsync(request.ProjectId, request.SourceId, ct, request.Rebuild);
    }
}

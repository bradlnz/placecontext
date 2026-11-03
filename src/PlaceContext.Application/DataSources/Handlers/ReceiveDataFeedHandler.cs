using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed class ReceiveDataFeedHandler(IDataFeedAccess access, DataSourcePipeline pipeline)
    : ICommandHandler<ReceiveDataFeedCommand, SourceSyncResult>
{
    public async Task<SourceSyncResult> HandleAsync(ReceiveDataFeedCommand request, CancellationToken ct = default)
    {
        if (!await access.AuthorizeAsync(request.ProjectId, request.SourceId, request.Token, ct))
        {
            throw new UnauthorizedAccessException("Invalid feed token.");
        }
        return await pipeline.IngestAsync(request.ProjectId, request.SourceId, request.Records, request.DeletedKeys, ct);
    }
}

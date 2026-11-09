using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed class DeleteDataSourceHandler(IDataSourceStore store) : ICommandHandler<DeleteDataSourceCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteDataSourceCommand request, CancellationToken ct = default)
    {
        await store.DeleteAsync(request.ProjectId, request.SourceId, ct);
        return true;
    }
}

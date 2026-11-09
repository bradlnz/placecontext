using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record SyncDataSourceCommand(Guid ProjectId, Guid SourceId, bool Rebuild = false) : ICommand<SourceSyncResult>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

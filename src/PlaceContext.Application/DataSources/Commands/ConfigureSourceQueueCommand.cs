using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record ConfigureSourceQueueCommand(Guid ProjectId, Guid SourceId, SourceQueueConfiguration Queue)
    : ICommand<DataSourceDefinition>, IRequiresPermission
{
    public string RequiredPermission => Permission.SecretsManage;
}

using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record CreateSourceWorkflowCommand(Guid ProjectId, Guid SourceId, string HostUrl) : ICommand<Guid>, IRequiresPermission
{
    public string RequiredPermission => Permission.ChainsManage;
}

using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record CreateSourceWorkflowCommand(Guid ProjectId, Guid SourceId, string HostUrl) : ICommand<Guid>, IRequiresPermission
{
    public string RequiredPermission => Permission.ChainsManage;
}

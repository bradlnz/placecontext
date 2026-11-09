using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record DeleteDataSourceCommand(Guid ProjectId, Guid SourceId) : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.SecretsManage;
}

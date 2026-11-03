using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record DeleteDataSourceCommand(Guid ProjectId, Guid SourceId) : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.SecretsManage;
}

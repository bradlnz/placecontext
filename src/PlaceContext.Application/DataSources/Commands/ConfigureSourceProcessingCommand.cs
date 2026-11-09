using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record ConfigureSourceProcessingCommand(Guid ProjectId, Guid SourceId, SourceProcessingPolicy? Processing,
    bool ProcessingApproved) : ICommand<DataSourceDefinition>, IRequiresPermission
{
    public string RequiredPermission => Permission.SecretsManage;
}

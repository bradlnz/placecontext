using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Features;

public sealed record CreateDataSourceCommand(Guid ProjectId, string Name, string ConnectionString, string Schema, string Table, string KeyColumn, IReadOnlyList<string> Columns, IReadOnlyList<string> Tags, bool EnableEmbeddings = false, string Provider = "postgres", SourceProcessingPolicy? Processing = null, bool ProcessingApproved = false, SourceQueueConfiguration? Queue = null) : ICommand<DataSourceDefinition>, IRequiresPermission
{
    public string RequiredPermission => Permission.SecretsManage;
}

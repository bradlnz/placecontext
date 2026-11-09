using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateDataSourceHandler(IDataSourceStore store, IProjectRepository projects, ICurrentUser user, IClock clock, SourceQueueApproval queueApproval) : ICommandHandler<CreateDataSourceCommand, DataSourceDefinition>
{
    public async Task<DataSourceDefinition> HandleAsync(CreateDataSourceCommand request, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, request.ProjectId, ct);
        if (request.Processing is not null && !request.ProcessingApproved)
        {
            throw new ArgumentException("Approve sending the selected fields to Jev during execution.");
        }
        var source = new DataSourceDefinition(Guid.NewGuid(), request.ProjectId, (request.Name ?? "").Trim(), request.Schema,
            request.Table, request.KeyColumn, request.Columns, (request.Tags ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), request.EnableEmbeddings, Guid.NewGuid(), CreatedBy: user.IsAuthenticated ? user.UserId : null, CreatedAt: clock.UtcNow, Provider: request.Provider,
            Processing: request.Processing is null ? null : request.Processing with
            {
                ApprovedBy = user.IsAuthenticated ? user.UserId : null, ApprovedAt = clock.UtcNow
            }, Queue: await queueApproval.ApproveAsync(request.ProjectId, request.Queue, ct));
        DataSourceRules.Validate(source);
        return await store.CreateAsync(source, request.ConnectionString, ct);
    }
}

using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Shared;

namespace PlaceContext.Application.Features;

public sealed class ConfigureSourceQueueHandler(IDataSourceStore store, SourceQueueApproval approval, IPermissionService permissions, IClock clock)
    : ICommandHandler<ConfigureSourceQueueCommand, DataSourceDefinition>
{
    public async Task<DataSourceDefinition> HandleAsync(ConfigureSourceQueueCommand request, CancellationToken ct = default)
    {
        if (!await permissions.HasAsync(Permission.DataWrite, ct))
        {
            throw new UnauthorizedAccessException("Changing source preparation requires data.write.");
        }
        var source = await store.GetAsync(request.ProjectId, request.SourceId, ct);
        if (source.Execution is { Status: "running" } run && run.StartedAt > clock.UtcNow.AddMinutes(-10))
        {
            throw new InvalidOperationException("Wait for the current source run to finish.");
        }
        var updated = source with
        {
            Queue = await approval.ApproveAsync(request.ProjectId, request.Queue, ct),
            Processing = null,
            EnableEmbeddings = false,
            Execution = new(Guid.NewGuid(), "pending", "Queue configured — sync to prepare and deliver data", clock.UtcNow)
        };
        DataSourceRules.Validate(updated);
        return await store.UpdateAsync(updated, ct);
    }
}

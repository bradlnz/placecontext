using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Application.Features;

public sealed class ConfigureSourceProcessingHandler(IDataSourceStore store, IPermissionService permissions, ICurrentUser user, IClock clock)
    : ICommandHandler<ConfigureSourceProcessingCommand, DataSourceDefinition>
{
    public async Task<DataSourceDefinition> HandleAsync(ConfigureSourceProcessingCommand request, CancellationToken ct = default)
    {
        if (!await permissions.HasAsync(Permission.DataWrite, ct))
        {
            throw new UnauthorizedAccessException("Changing inbound processing requires data.write.");
        }
        if (request.Processing is not null && !request.ProcessingApproved)
        {
            throw new ArgumentException("Approve sending the selected fields to Jev during execution.");
        }
        var source = await store.GetAsync(request.ProjectId, request.SourceId, ct);
        if (source.Execution is { Status: "running" } run && run.StartedAt > clock.UtcNow.AddMinutes(-10))
        {
            throw new InvalidOperationException("Wait for the current source run to finish before changing its rules.");
        }
        var updated = source with
        {
            Processing = request.Processing is null ? null : request.Processing with
            {
                ApprovedBy = user.IsAuthenticated ? user.UserId : null, ApprovedAt = clock.UtcNow
            },
            Execution = new(Guid.NewGuid(), "pending", "Rules changed — run to evaluate retained data", clock.UtcNow)
        };
        DataSourceRules.Validate(updated);
        return await store.UpdateAsync(updated, ct);
    }
}

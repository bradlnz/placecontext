using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access;
using PlaceContext.Application.Auth;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Scheduling;

namespace PlaceContext.Infrastructure.DataSources;

public sealed class SourceQueueDelivery(AppDbContext db, DbChainQueueService queues, IPermissionService permissions) : ISourceQueueDelivery
{
    public async Task PublishAsync(DataSourceDefinition source, IReadOnlyList<SourceDocument> changed,
        IReadOnlyList<string> deletedKeys, CancellationToken ct)
    {
        var configuration = source.Queue ?? throw new InvalidOperationException("Source has no destination queue.");
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Source delivery requires an atomic transaction.");
        }
        var queue = await queues.GetQueueAsync(configuration.QueueId, ct);
        if (queue?.ProjectId != source.ProjectId || !queue.Enabled)
        {
            throw new InvalidOperationException("Destination queue is unavailable or paused. Previous data was retained.");
        }
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == configuration.ApprovedBy, ct);
        var identity = user is null ? null : new UserIdentity(user.Id, user.Role);
        if (configuration.ApprovedBy == TrustedServicePrincipals.ManagementApiUserId)
        {
            identity = new UserIdentity(TrustedServicePrincipals.ManagementApiUserId, nameof(UserRole.Owner));
        }
        if (identity is null)
        {
            throw new UnauthorizedAccessException("The source queue approval is no longer valid.");
        }
        var caller = CurrentUser.Current;
        CurrentUser.Set(identity);
        try
        {
            if (!await permissions.HasAsync(Permission.JobsRun, ct) || !await permissions.HasAsync(Permission.DataRead, ct))
            {
                throw new UnauthorizedAccessException("The source approver no longer has data and workflow access.");
            }
            var batch = 0;
            foreach (var documents in changed.Chunk(10))
            {
                var payload = JsonSerializer.SerializeToElement(new
                {
                    sourceId = source.Id, sourceName = source.Name, projectId = source.ProjectId, schema = source.Schema, table = source.Table,
                    tags = source.Tags, records = documents.Select(document => JsonSerializer.Deserialize<JsonElement>(document.Json)).ToArray(),
                    deletedKeys = Array.Empty<string>()
                });
                await queues.SubmitAuthorizedAsync(queue.Id, payload, $"source:{source.Id:N}:{source.Version:N}:{batch++}", null, ct);
            }
            foreach (var keys in deletedKeys.Chunk(100))
            {
                var payload = JsonSerializer.SerializeToElement(new
                {
                    sourceId = source.Id, sourceName = source.Name, projectId = source.ProjectId, schema = source.Schema, table = source.Table,
                    tags = source.Tags, records = Array.Empty<object>(), deletedKeys = keys
                });
                await queues.SubmitAuthorizedAsync(queue.Id, payload, $"source:{source.Id:N}:{source.Version:N}:{batch++}", null, ct);
            }
        }
        finally
        {
            CurrentUser.Clear();
            if (caller is not null)
            {
                CurrentUser.Set(caller);
            }
        }
    }
}

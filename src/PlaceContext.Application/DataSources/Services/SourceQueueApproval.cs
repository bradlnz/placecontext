using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Application.DataSources;

public sealed class SourceQueueApproval(IChainQueueService queues, IPermissionService permissions, ICurrentUser user)
{
    public async Task<SourceQueueConfiguration?> ApproveAsync(Guid projectId, SourceQueueConfiguration? configuration, CancellationToken ct)
    {
        if (configuration is null)
        {
            return null;
        }
        if (!user.IsAuthenticated || !await permissions.HasAsync(Permission.JobsRun, ct)
            || !await permissions.HasAsync(Permission.ChainsManage, ct))
        {
            throw new UnauthorizedAccessException("Connecting a source to a queue requires workflow management and run permissions.");
        }
        var queue = (await queues.ListAsync(projectId, ct)).FirstOrDefault(queue => queue.Id == configuration.QueueId && queue.ProjectId == projectId);
        if (queue is null || !queue.Enabled)
        {
            throw new ArgumentException("Choose an active queue in this project.");
        }
        return configuration with { ApprovedBy = user.UserId };
    }
}

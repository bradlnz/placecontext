using System.Text.Json;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Tenancy;

namespace PlaceContext.Application.Tests;

public sealed class SourceQueueApprovalTests
{
    [Fact]
    public async Task Approval_checks_project_queue_state_and_permissions_and_replaces_caller_supplied_grants()
    {
        var project = Guid.NewGuid();
        var queueId = Guid.NewGuid();
        var queues = new Queues { Queue = new(queueId, project, Guid.NewGuid(), "Prepared", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) };
        var permissions = new Permissions();
        var user = new User();
        var service = new SourceQueueApproval(queues, permissions, user);
        var configuration = new SourceQueueConfiguration(queueId, [], [], ApprovedBy: Guid.NewGuid());
        var approved = await service.ApproveAsync(project, configuration, default);
        Assert.Equal(user.UserId, approved!.ApprovedBy);
        Assert.Null(await service.ApproveAsync(project, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApproveAsync(Guid.NewGuid(), configuration, default));
        queues.Queue = queues.Queue with { Enabled = false };
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApproveAsync(project, configuration, default));
        queues.Queue = queues.Queue with { Enabled = true };
        permissions.Allowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveAsync(project, configuration, default));
    }

    private sealed class User : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => "Owner";
        public bool IsAuthenticated => true;
    }
    private sealed class Permissions : IPermissionService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(Allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Queues : IChainQueueService
    {
        public ChainQueueDefinition Queue { get; set; } = null!;
        public Task<IReadOnlyList<ChainQueueDefinition>> ListAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChainQueueDefinition>>([Queue]);
        public Task<ChainQueueDefinition> SaveAsync(Guid? id, Guid projectId, Guid chainId, string name, bool enabled, int messageTtlSeconds = 86400, int receiptRetentionDays = 30, int maxAttempts = 3, int retryDelaySeconds = 3, int deliveryDelaySeconds = 0, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RetryAsync(Guid queueId, Guid trackingId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid queueId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JobChainSubmission> SubmitAsync(Guid queueId, JsonElement payload, string? idempotencyKey, DateTimeOffset? executeAt = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobChainSubmission>> ListMessagesAsync(Guid queueId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JobChainSubmission?> GetMessageAsync(Guid queueId, Guid trackingId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class CrmIngestionAutomationTests
{
    [Fact]
    public async Task Ingestion_dispatcher_queues_raw_payload_for_each_matching_rule()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var chainId = Guid.NewGuid();
        var rules = new RuleRepository();
        await rules.AddAsync(CrmAutomationRule.Create(
            projectId, "Run feasibility", CrmAutomationEventType.IngestionReceived,
            null, chainId, true, DateTimeOffset.UtcNow));
        var queue = new RecordingQueue();
        var dispatcher = new CrmAutomationDispatcher(
            rules, queue, new FakeCurrentTenant(tenantId));
        const string payload = """{"address":"123 Example Street"}""";

        var count = await dispatcher.EnqueueIngestionAsync(projectId, payload);

        Assert.Equal(1, count);
        var queued = Assert.Single(queue.Values);
        Assert.Equal(tenantId, queued.TenantId);
        Assert.Equal(chainId, queued.ChainId);
        Assert.Null(queued.ClientId);
        Assert.Null(queued.LifecycleStage);
        Assert.Equal(CrmAutomationEventType.IngestionReceived, queued.EventType);
        Assert.Equal(payload, queued.InputPayload);
    }

    private sealed class RecordingQueue : ICrmAutomationQueue
    {
        public List<QueuedCrmAutomation> Values { get; } = new();

        public Task EnqueueAsync(QueuedCrmAutomation value, CancellationToken ct = default)
        {
            Values.Add(value);
            return Task.CompletedTask;
        }
    }

    private sealed class RuleRepository : ICrmAutomationRuleRepository
    {
        private readonly List<CrmAutomationRule> _rules = new();

        public Task AddAsync(CrmAutomationRule rule, CancellationToken ct = default)
        {
            _rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CrmAutomationRule rule, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            _rules.RemoveAll(rule => rule.Id == id);
            return Task.CompletedTask;
        }

        public Task<CrmAutomationRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_rules.FirstOrDefault(rule => rule.Id == id));

        public Task<IReadOnlyList<CrmAutomationRule>> ListForProjectAsync(
            Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmAutomationRule>>(
                _rules.Where(rule => rule.ProjectId == projectId).ToList());

        public Task<IReadOnlyList<CrmAutomationRule>> ListMatchingAsync(
            Guid projectId, CrmAutomationEventType eventType, CustomerLifecycleStage? stage,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmAutomationRule>>(_rules
                .Where(rule => rule.ProjectId == projectId && rule.Matches(
                    eventType, stage ?? CustomerLifecycleStage.Lead))
                .Where(rule => stage is not null || rule.LifecycleStage is null)
                .ToList());
    }
}

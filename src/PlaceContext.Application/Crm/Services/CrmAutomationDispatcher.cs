using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Matches a CRM event to enabled rules and adds durable chain runs to the current transaction.</summary>
public sealed class CrmAutomationDispatcher
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly ICrmAutomationQueue _queue;
    private readonly ICurrentTenant _tenant;

    public CrmAutomationDispatcher(
        ICrmAutomationRuleRepository rules,
        ICrmAutomationQueue queue,
        ICurrentTenant tenant)
        => (_rules, _queue, _tenant) = (rules, queue, tenant);

    public async Task<int> EnqueueAsync(
        CrmClient client,
        CrmAutomationEventType eventType,
        CancellationToken ct = default)
    {
        var matching = await _rules.ListMatchingAsync(
            client.ProjectId, eventType, client.LifecycleStage, ct);
        foreach (var rule in matching)
            await _queue.EnqueueAsync(new QueuedCrmAutomation(
                _tenant.TenantId, rule.Id, client.Id, rule.ChainId, eventType,
                client.LifecycleStage, rule.Name), ct);
        return matching.Count;
    }

    public async Task<int> EnqueueIngestionAsync(
        Guid projectId,
        string inputPayload,
        CancellationToken ct = default)
    {
        var matching = await _rules.ListMatchingAsync(
            projectId, CrmAutomationEventType.IngestionReceived, null, ct);
        foreach (var rule in matching)
            await _queue.EnqueueAsync(new QueuedCrmAutomation(
                _tenant.TenantId, rule.Id, null, rule.ChainId,
                CrmAutomationEventType.IngestionReceived, null, rule.Name, inputPayload), ct);
        return matching.Count;
    }
}

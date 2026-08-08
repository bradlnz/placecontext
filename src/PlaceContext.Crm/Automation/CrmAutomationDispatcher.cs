using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Automation;

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
                _tenant.TenantId, client.ProjectId, rule.Id, client.Id, rule.ChainId, eventType,
                client.LifecycleStage, rule.Name), ct);
        return matching.Count;
    }

    public async Task<IReadOnlyList<CrmAutomationReceipt>> EnqueueIngestionAsync(
        Guid projectId,
        string inputPayload,
        Guid? clientId = null,
        CancellationToken ct = default)
    {
        var matching = await _rules.ListMatchingAsync(
            projectId, CrmAutomationEventType.IngestionReceived, null, ct);
        var receipts = new List<CrmAutomationReceipt>(matching.Count);
        foreach (var rule in matching)
        {
            var trackingId = await _queue.EnqueueAsync(new QueuedCrmAutomation(
                _tenant.TenantId, projectId, rule.Id, clientId, rule.ChainId,
                CrmAutomationEventType.IngestionReceived, null, rule.Name, inputPayload), ct);
            receipts.Add(new CrmAutomationReceipt(trackingId, rule.Id, rule.ChainId, rule.Name));
        }
        return receipts;
    }
}

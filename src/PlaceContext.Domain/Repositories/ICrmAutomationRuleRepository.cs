using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

public interface ICrmAutomationRuleRepository
{
    Task AddAsync(CrmAutomationRule rule, CancellationToken ct = default);
    Task UpdateAsync(CrmAutomationRule rule, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
    Task<CrmAutomationRule?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CrmAutomationRule>> ListForProjectAsync(
        Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<CrmAutomationRule>> ListMatchingAsync(
        Guid projectId, CrmAutomationEventType eventType, CustomerLifecycleStage stage,
        CancellationToken ct = default);
}

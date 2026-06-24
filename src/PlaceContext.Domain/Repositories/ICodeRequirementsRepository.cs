using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of code-requirements documents: one global, plus one per project.</summary>
public interface ICodeRequirementsRepository
{
    Task<CodeRequirements?> GetGlobalAsync(CancellationToken ct = default);
    Task<CodeRequirements?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default);
    Task SaveAsync(CodeRequirements requirements, CancellationToken ct = default);
}

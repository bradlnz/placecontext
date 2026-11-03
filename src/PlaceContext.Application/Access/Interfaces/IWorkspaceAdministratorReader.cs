namespace PlaceContext.Application.Access;

public interface IWorkspaceAdministratorReader
{
    Task<bool> IsOwnerOrAdministratorAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task<bool> IsAdministratorAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteProjectSecretHandler : ICommandHandler<DeleteProjectSecretCommand, bool>
{
    private readonly IProjectSecretRepository _secrets;
    private readonly IVaultUnitOfWork _unitOfWork;

    public DeleteProjectSecretHandler(IProjectSecretRepository secrets, IVaultUnitOfWork unitOfWork)
        => (_secrets, _unitOfWork) = (secrets, unitOfWork);

    public async Task<bool> HandleAsync(DeleteProjectSecretCommand c, CancellationToken ct = default)
    {
        await _secrets.DeleteAsync(c.ProjectId, c.Name, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}

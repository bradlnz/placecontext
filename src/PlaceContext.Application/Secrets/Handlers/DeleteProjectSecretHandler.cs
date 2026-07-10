using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteProjectSecretHandler : ICommandHandler<DeleteProjectSecretCommand, bool>
{
    private readonly IProjectSecretRepository _secrets;
    private readonly IUnitOfWork _uow;
    public DeleteProjectSecretHandler(IProjectSecretRepository secrets, IUnitOfWork uow) => (_secrets, _uow) = (secrets, uow);

    public async Task<bool> HandleAsync(DeleteProjectSecretCommand c, CancellationToken ct = default)
    {
        await _secrets.DeleteAsync(c.ProjectId, c.Name, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

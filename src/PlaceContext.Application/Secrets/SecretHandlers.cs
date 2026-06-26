using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class AddProjectSecretHandler : ICommandHandler<AddProjectSecretCommand, ProjectSecretView>
{
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public AddProjectSecretHandler(IProjectSecretRepository secrets, ISecretProtector protector, IUnitOfWork uow, IClock clock)
        => (_secrets, _protector, _uow, _clock) = (secrets, protector, uow, clock);

    public async Task<ProjectSecretView> HandleAsync(AddProjectSecretCommand c, CancellationToken ct = default)
    {
        var name = (c.Name ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("Secret name is required.");
        var now = _clock.UtcNow;
        await _secrets.AddAsync(c.ProjectId, name, _protector.Protect(c.Value ?? ""), now, ct);
        await _uow.SaveChangesAsync(ct);
        return new ProjectSecretView(name, now);
    }
}

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

public sealed class ListProjectSecretsHandler : IQueryHandler<ListProjectSecretsQuery, IReadOnlyList<ProjectSecretView>>
{
    private readonly IProjectSecretRepository _secrets;
    public ListProjectSecretsHandler(IProjectSecretRepository secrets) => _secrets = secrets;

    public async Task<IReadOnlyList<ProjectSecretView>> HandleAsync(ListProjectSecretsQuery q, CancellationToken ct = default)
    {
        var rows = await _secrets.ListAsync(q.ProjectId, ct);
        return rows.Select(r => new ProjectSecretView(r.Name, r.CreatedAt)).ToList();
    }
}

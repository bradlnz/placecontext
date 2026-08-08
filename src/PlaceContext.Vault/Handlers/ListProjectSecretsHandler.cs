using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Application.Features;

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

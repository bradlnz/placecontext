using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Search.Contracts.Commands;

namespace PlaceContext.Search.Handlers.Workspace;

public sealed class IndexRunOutputHandler(
    IEmbeddingGateway embeddings,
    IRunEmbeddingRepository repository,
    IClock clock) : ICommandHandler<IndexRunOutputCommand, bool>
{
    public async Task<bool> HandleAsync(
        IndexRunOutputCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!embeddings.IsEnabled || string.IsNullOrWhiteSpace(command.Text))
            return false;

        var vectors = await embeddings.EmbedAsync([command.Text], cancellationToken);
        if (vectors.Count != 1 || vectors[0].Length == 0)
            return false;

        await repository.AddAsync(
            RunEmbedding.Create(
                command.RunId,
                command.JobId,
                command.ProjectId,
                command.Text,
                vectors[0],
                clock.UtcNow),
            cancellationToken);
        return true;
    }
}

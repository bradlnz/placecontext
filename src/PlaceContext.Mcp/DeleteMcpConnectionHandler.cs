using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp;

public sealed class DeleteMcpConnectionHandler(
    IMcpConnectionRepository repository,
    IMcpUnitOfWork unitOfWork) : ICommandHandler<DeleteMcpConnectionCommand, bool>
{
    public async Task<bool> HandleAsync(
        DeleteMcpConnectionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await repository.GetByIdAsync(command.Id, cancellationToken) is null)
            return false;

        await repository.DeleteAsync(command.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

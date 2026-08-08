using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteChatCommandHandler : ICommandHandler<DeleteChatCommandCommand, bool>
{
    private readonly IChatCommandRepository _repo;
    private readonly IAgentChatUnitOfWork _uow;

    public DeleteChatCommandHandler(IChatCommandRepository repo, IAgentChatUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteChatCommandCommand command, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(command.Id, ct);
        if (entity is null) return false;
        await _repo.RemoveAsync(command.Id, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

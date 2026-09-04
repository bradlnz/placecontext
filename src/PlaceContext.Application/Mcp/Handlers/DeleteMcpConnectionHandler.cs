using System.Net.Http;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteMcpConnectionHandler : ICommandHandler<DeleteMcpConnectionCommand, bool>
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IUnitOfWork _uow;

    public DeleteMcpConnectionHandler(IMcpConnectionRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteMcpConnectionCommand command, CancellationToken ct = default)
    {
        var conn = await _repo.GetByIdAsync(command.Id, ct);
        if (conn is null) return false;
        await _repo.DeleteAsync(command.Id, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

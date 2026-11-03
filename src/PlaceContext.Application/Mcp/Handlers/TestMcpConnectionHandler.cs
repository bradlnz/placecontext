using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class TestMcpConnectionHandler : ICommandHandler<TestMcpConnectionCommand, McpConnectionView>
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IMcpConnectionProbe _probe;

    public TestMcpConnectionHandler(IMcpConnectionRepository repo, IUnitOfWork uow, IClock clock, IMcpConnectionProbe probe)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
        _probe = probe;
    }

    public async Task<McpConnectionView> HandleAsync(TestMcpConnectionCommand command, CancellationToken ct = default)
    {
        var conn = await _repo.GetByIdAsync(command.Id, ct) ?? throw new InvalidOperationException($"McpConnection {command.Id} not found.");

        var status = await _probe.ProbeAsync(conn, ct);

        conn.RecordConnection(status, _clock.UtcNow);
        await _repo.UpdateAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return McpConnectionMapper.ToView(conn);
    }
}

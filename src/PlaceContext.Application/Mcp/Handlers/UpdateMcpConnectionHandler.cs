using System.Net.Http;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class UpdateMcpConnectionHandler : ICommandHandler<UpdateMcpConnectionCommand, McpConnectionView>
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IAgentChatUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateMcpConnectionHandler(IMcpConnectionRepository repo, IAgentChatUnitOfWork uow, IClock clock)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
    }

    public async Task<McpConnectionView> HandleAsync(UpdateMcpConnectionCommand command, CancellationToken ct = default)
    {
        var conn = await _repo.GetByIdAsync(command.Id, ct) ?? throw new InvalidOperationException($"McpConnection {command.Id} not found.");
        conn.Update(command.Name, command.Transport, command.EndpointUrl, command.Command, command.Args,
            command.AuthType, command.AuthToken, command.AuthHeader, _clock.UtcNow);
        if (command.AuthType == McpAuthType.OAuth)
            conn.SetOAuthCredentials(command.OAuthClientId, command.OAuthScopes, _clock.UtcNow);
        await _repo.UpdateAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return McpConnectionMapper.ToView(conn);
    }
}

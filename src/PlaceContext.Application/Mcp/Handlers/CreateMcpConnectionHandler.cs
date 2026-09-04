using System.Net.Http;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateMcpConnectionHandler : ICommandHandler<CreateMcpConnectionCommand, McpConnectionView>
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateMcpConnectionHandler(IMcpConnectionRepository repo, IUnitOfWork uow, IClock clock)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
    }

    public async Task<McpConnectionView> HandleAsync(CreateMcpConnectionCommand command, CancellationToken ct = default)
    {
        var conn = McpConnection.Create(command.ProjectId, command.Name, command.Transport,
            command.EndpointUrl, command.Command, command.Args,
            command.AuthType, command.AuthToken, command.AuthHeader, _clock.UtcNow);
        if (command.AuthType == McpAuthType.OAuth)
            conn.SetOAuthCredentials(command.OAuthClientId, command.OAuthScopes, _clock.UtcNow);
        await _repo.AddAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return McpConnectionMapper.ToView(conn);
    }
}

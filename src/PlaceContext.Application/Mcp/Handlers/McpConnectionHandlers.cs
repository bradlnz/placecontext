using System.Net.Http;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
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
        if (command.AuthType == "oauth")
            conn.SetOAuthCredentials(command.OAuthClientId, command.OAuthScopes, _clock.UtcNow);
        await _repo.AddAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return McpConnectionMapper.ToView(conn);
    }
}

public sealed class UpdateMcpConnectionHandler : ICommandHandler<UpdateMcpConnectionCommand, McpConnectionView>
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateMcpConnectionHandler(IMcpConnectionRepository repo, IUnitOfWork uow, IClock clock)
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
        if (command.AuthType == "oauth")
            conn.SetOAuthCredentials(command.OAuthClientId, command.OAuthScopes, _clock.UtcNow);
        await _repo.UpdateAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return McpConnectionMapper.ToView(conn);
    }
}

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

public sealed class ListMcpConnectionsHandler : IQueryHandler<ListMcpConnectionsQuery, IReadOnlyList<McpConnectionView>>
{
    private readonly IMcpConnectionRepository _repo;

    public ListMcpConnectionsHandler(IMcpConnectionRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<McpConnectionView>> HandleAsync(ListMcpConnectionsQuery query, CancellationToken ct = default)
    {
        var connections = await _repo.ListByProjectAsync(query.ProjectId, ct);
        return connections.Select(McpConnectionMapper.ToView).ToList();
    }
}

public sealed class TestMcpConnectionHandler : ICommandHandler<TestMcpConnectionCommand, McpConnectionView>
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IHttpClientFactory _http;

    public TestMcpConnectionHandler(IMcpConnectionRepository repo, IUnitOfWork uow, IClock clock, IHttpClientFactory http)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
        _http = http;
    }

    public async Task<McpConnectionView> HandleAsync(TestMcpConnectionCommand command, CancellationToken ct = default)
    {
        var conn = await _repo.GetByIdAsync(command.Id, ct) ?? throw new InvalidOperationException($"McpConnection {command.Id} not found.");

        var status = "unknown";
        try
        {
            if (conn.Transport is "http" or "sse" && !string.IsNullOrEmpty(conn.EndpointUrl))
            {
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                // Apply auth headers
                if (conn.AuthType is "bearer" or "oauth" && !string.IsNullOrEmpty(conn.AuthToken))
                {
                    client.DefaultRequestHeaders.Authorization = new("Bearer", conn.AuthToken);
                }
                else if (conn.AuthType == "apikey" && !string.IsNullOrEmpty(conn.AuthToken))
                {
                    client.DefaultRequestHeaders.Add("X-API-Key", conn.AuthToken);
                }
                else if (conn.AuthType == "header" && !string.IsNullOrEmpty(conn.AuthHeader) && !string.IsNullOrEmpty(conn.AuthToken))
                {
                    client.DefaultRequestHeaders.Add(conn.AuthHeader, conn.AuthToken);
                }

                var resp = await client.GetAsync(conn.EndpointUrl, ct);
                status = resp.IsSuccessStatusCode ? "connected" : $"http:{(int)resp.StatusCode}";
            }
            else
            {
                status = "stdio:pending";
            }
        }
        catch (Exception ex)
        {
            status = $"error:{ex.Message[..Math.Min(50, ex.Message.Length)]}";
        }

        conn.RecordConnection(status, _clock.UtcNow);
        await _repo.UpdateAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return McpConnectionMapper.ToView(conn);
    }
}

// Commands
public sealed record UpdateMcpConnectionCommand(
    Guid Id, string Name, string Transport, string? EndpointUrl = null, string? Command = null, string? Args = null,
    string? AuthType = null, string? AuthToken = null, string? AuthHeader = null,
    string? OAuthClientId = null, string? OAuthScopes = null) : ICommand<McpConnectionView>;

public sealed record DeleteMcpConnectionCommand(Guid Id) : ICommand<bool>;

public sealed record ListMcpConnectionsQuery(Guid ProjectId) : IQuery<IReadOnlyList<McpConnectionView>>;

public sealed record TestMcpConnectionCommand(Guid Id) : ICommand<McpConnectionView>;

// Mapper
internal static class McpConnectionMapper
{
    internal static McpConnectionView ToView(McpConnection c) => new(
        c.Id, c.ProjectId, c.Name, c.Transport, c.EndpointUrl, c.Command, c.Args,
        c.AuthType, c.Enabled, c.LastStatus, c.LastConnectedAt, c.CreatedAt,
        c.OAuthAccessToken, c.OAuthTokenExpiresAt, c.OAuthClientId, c.OAuthScopes);
}

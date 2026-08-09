using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp;

public sealed class CreateMcpConnectionHandler(
    IMcpConnectionRepository repository,
    IMcpUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateMcpConnectionCommand, McpConnectionView>
{
    public async Task<McpConnectionView> HandleAsync(
        CreateMcpConnectionCommand command,
        CancellationToken cancellationToken = default)
    {
        var connection = McpConnection.Create(
            command.ProjectId,
            command.Name,
            command.Transport,
            command.EndpointUrl,
            command.Command,
            command.Args,
            command.AuthType,
            command.AuthToken,
            command.AuthHeader,
            clock.UtcNow);

        if (command.AuthType == McpAuthType.OAuth)
            connection.SetOAuthCredentials(command.OAuthClientId, command.OAuthScopes, clock.UtcNow);

        await repository.AddAsync(connection, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return McpConnectionMapper.ToView(connection);
    }
}

public sealed class UpdateMcpConnectionHandler(
    IMcpConnectionRepository repository,
    IMcpUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateMcpConnectionCommand, McpConnectionView>
{
    public async Task<McpConnectionView> HandleAsync(
        UpdateMcpConnectionCommand command,
        CancellationToken cancellationToken = default)
    {
        var connection = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"McpConnection {command.Id} not found.");

        connection.Update(
            command.Name,
            command.Transport,
            command.EndpointUrl,
            command.Command,
            command.Args,
            command.AuthType,
            command.AuthToken,
            command.AuthHeader,
            clock.UtcNow);

        if (command.AuthType == McpAuthType.OAuth)
            connection.SetOAuthCredentials(command.OAuthClientId, command.OAuthScopes, clock.UtcNow);

        await repository.UpdateAsync(connection, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return McpConnectionMapper.ToView(connection);
    }
}

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

public sealed class TestMcpConnectionHandler(
    IMcpConnectionRepository repository,
    IMcpUnitOfWork unitOfWork,
    IClock clock,
    IHttpClientFactory httpClientFactory) : ICommandHandler<TestMcpConnectionCommand, McpConnectionView>
{
    public async Task<McpConnectionView> HandleAsync(
        TestMcpConnectionCommand command,
        CancellationToken cancellationToken = default)
    {
        var connection = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new InvalidOperationException($"McpConnection {command.Id} not found.");

        var status = "unknown";
        try
        {
            if (connection.Transport is McpTransport.Http or McpTransport.Sse
                && !string.IsNullOrEmpty(connection.EndpointUrl))
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                ApplyAuthentication(client, connection);
                var response = await client.GetAsync(connection.EndpointUrl, cancellationToken);
                status = response.IsSuccessStatusCode ? "connected" : $"http:{(int)response.StatusCode}";
            }
            else
            {
                status = "stdio:pending";
            }
        }
        catch (Exception exception)
        {
            status = $"error:{exception.Message[..Math.Min(50, exception.Message.Length)]}";
        }

        connection.RecordConnection(status, clock.UtcNow);
        await repository.UpdateAsync(connection, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return McpConnectionMapper.ToView(connection);
    }

    private static void ApplyAuthentication(HttpClient client, McpConnection connection)
    {
        if (connection.AuthType is McpAuthType.Bearer or McpAuthType.OAuth
            && !string.IsNullOrEmpty(connection.AuthToken))
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", connection.AuthToken);
        }
        else if (connection.AuthType == McpAuthType.ApiKey && !string.IsNullOrEmpty(connection.AuthToken))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", connection.AuthToken);
        }
        else if (connection.AuthType == McpAuthType.Header
                 && !string.IsNullOrEmpty(connection.AuthHeader)
                 && !string.IsNullOrEmpty(connection.AuthToken))
        {
            client.DefaultRequestHeaders.Add(connection.AuthHeader, connection.AuthToken);
        }
    }
}

public sealed class ListMcpConnectionsHandler(IMcpConnectionRepository repository)
    : IQueryHandler<ListMcpConnectionsQuery, IReadOnlyList<McpConnectionView>>
{
    public async Task<IReadOnlyList<McpConnectionView>> HandleAsync(
        ListMcpConnectionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var connections = await repository.ListByProjectAsync(query.ProjectId, cancellationToken);
        return connections.Select(McpConnectionMapper.ToView).ToList();
    }
}

internal static class McpConnectionMapper
{
    internal static McpConnectionView ToView(McpConnection connection) => new(
        connection.Id,
        connection.ProjectId,
        connection.Name,
        connection.Transport,
        connection.EndpointUrl,
        connection.Command,
        connection.Args,
        connection.AuthType,
        connection.Enabled,
        connection.LastStatus,
        connection.LastConnectedAt,
        connection.CreatedAt,
        connection.OAuthAccessToken,
        connection.OAuthTokenExpiresAt,
        connection.OAuthClientId,
        connection.OAuthScopes);
}

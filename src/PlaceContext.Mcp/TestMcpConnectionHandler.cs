using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp;

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

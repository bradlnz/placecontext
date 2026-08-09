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

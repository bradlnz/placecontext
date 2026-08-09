using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp;

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

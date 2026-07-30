using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Cluster;

public sealed record CreateAgentJoinTokenCommand : ICommand<string>;

public sealed class CreateAgentJoinTokenHandler : ICommandHandler<CreateAgentJoinTokenCommand, string>
{
    private readonly IAgentTokenManager _tokens;
    private readonly ICurrentTenant _tenant;

    public CreateAgentJoinTokenHandler(IAgentTokenManager tokens, ICurrentTenant tenant)
        => (_tokens, _tenant) = (tokens, tenant);

    public Task<string> HandleAsync(CreateAgentJoinTokenCommand command, CancellationToken ct = default)
        => _tokens.CreateTokenAsync(_tenant.TenantId);
}

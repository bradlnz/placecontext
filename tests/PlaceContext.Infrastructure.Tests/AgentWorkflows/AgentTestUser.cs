using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

internal sealed class AgentTestUser : ICurrentUser
{
    public Guid UserId { get; } = Guid.NewGuid();
    public string Role => "Admin";
    public bool IsAuthenticated => true;
}

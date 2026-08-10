using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteAgentDefinitionHandler(
    IAgentDefinitionRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteAgentDefinitionCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteAgentDefinitionCommand command, CancellationToken ct = default)
    {
        var agent = await repository.GetByIdAsync(command.AgentId, ct)
            ?? throw new InvalidOperationException($"Agent {command.AgentId} not found.");
        if (agent.Kind == AgentKind.Command)
            throw new InvalidOperationException("The Command Agent cannot be deleted.");

        await repository.RemoveAsync(agent.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}

using PlaceContext.Application.Agents;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class EnsureCommandAgentHandler(
    IAgentDefinitionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<EnsureCommandAgentCommand, AgentDefinitionView>
{
    public async Task<AgentDefinitionView> HandleAsync(EnsureCommandAgentCommand command, CancellationToken ct = default)
    {
        var existing = await repository.GetCommandAsync(command.ProjectId, ct);
        if (existing is not null)
            return AgentDefinitionMapper.ToView(existing);

        var agent = AgentDefinition.CreateCommand(command.ProjectId, clock.UtcNow);
        await repository.AddAsync(agent, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return AgentDefinitionMapper.ToView(agent);
    }
}

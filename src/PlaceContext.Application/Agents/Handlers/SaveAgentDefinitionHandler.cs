using PlaceContext.Application.Agents;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SaveAgentDefinitionHandler(
    IAgentDefinitionRepository repository,
    IJobRepository jobs,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SaveAgentDefinitionCommand, AgentDefinitionView>
{
    public async Task<AgentDefinitionView> HandleAsync(SaveAgentDefinitionCommand command, CancellationToken ct = default)
    {
        foreach (var jobId in command.AllowedJobIds.Distinct())
        {
            var job = await jobs.GetByIdAsync(jobId, ct);
            if (job is null || job.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Every allowed Job must belong to the agent's project.");
        }

        var template = AgentTemplateCatalog.Find(command.TemplateKey);
        var instructions = string.IsNullOrWhiteSpace(command.Instructions)
            ? template?.Instructions ?? string.Empty
            : command.Instructions;
        var capabilities = command.Capabilities.Count == 0
            ? template?.Capabilities ?? [AgentCapability.GraphRead]
            : command.Capabilities;

        AgentDefinition agent;
        if (command.AgentId.HasValue)
        {
            agent = await repository.GetByIdAsync(command.AgentId.Value, ct)
                ?? throw new InvalidOperationException($"Agent {command.AgentId} not found.");
            if (agent.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Agent does not belong to this project.");
            agent.Update(command.Name, command.Description, instructions, command.TemplateKey,
                capabilities, command.AllowedJobIds, command.Enabled, clock.UtcNow);
            await repository.UpdateAsync(agent, ct);
        }
        else
        {
            agent = AgentDefinition.CreateWorker(command.ProjectId, command.Name, command.Description,
                instructions, command.TemplateKey, capabilities, command.AllowedJobIds, clock.UtcNow);
            if (!command.Enabled)
                agent.Update(agent.Name, agent.Description, agent.Instructions, agent.TemplateKey,
                    agent.Capabilities, agent.AllowedJobIds, false, clock.UtcNow);
            await repository.AddAsync(agent, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return AgentDefinitionMapper.ToView(agent);
    }
}

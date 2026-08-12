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
        var projectAgents = await repository.ListForProjectAsync(command.ProjectId, ct);

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

            var parentAgentId = ResolveParentAgentId(agent.Id, agent.Kind, command.ParentAgentId, projectAgents);
            var schema = string.IsNullOrWhiteSpace(command.Schema) ? "{}" : command.Schema;

            agent.Update(command.Name, command.Description, instructions, command.TemplateKey,
                capabilities, command.AllowedJobIds, command.Enabled, parentAgentId, clock.UtcNow, schema);
            await repository.UpdateAsync(agent, ct);
        }
        else
        {
            var schema = string.IsNullOrWhiteSpace(command.Schema) ? "{}" : command.Schema;
            var parentAgentId = ResolveParentAgentId(null, AgentKind.Worker, command.ParentAgentId, projectAgents);
            agent = AgentDefinition.CreateWorker(command.ProjectId, command.Name, command.Description,
                instructions, command.TemplateKey, schema, capabilities, command.AllowedJobIds,
                parentAgentId, clock.UtcNow);
            if (!command.Enabled)
                agent.Update(agent.Name, agent.Description, agent.Instructions, agent.TemplateKey,
                    agent.Capabilities, agent.AllowedJobIds, false, parentAgentId, clock.UtcNow, agent.Schema);
            await repository.AddAsync(agent, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return AgentDefinitionMapper.ToView(agent);
    }

    private static Guid? ResolveParentAgentId(
        Guid? currentAgentId,
        AgentKind agentKind,
        Guid? requestedParentId,
        IReadOnlyList<AgentDefinition> projectAgents)
    {
        if (agentKind == AgentKind.Command)
        {
            if (requestedParentId is not null)
                throw new InvalidOperationException("The command agent cannot have a parent.");

            return null;
        }

        var command = projectAgents.FirstOrDefault(agent => agent.Kind == AgentKind.Command);
        var parentId = NormalizeParentId(requestedParentId, command?.Id);
        if (!parentId.HasValue)
            return null;

        if (projectAgents.FirstOrDefault(agent => agent.Id == parentId.Value) is null)
            throw new InvalidOperationException($"Parent agent {parentId} was not found in this project.");

        if (parentId == currentAgentId)
            throw new InvalidOperationException("An agent cannot be its own parent.");

        var visited = new HashSet<Guid>();
        var current = parentId;
        while (current.HasValue)
        {
            if (!visited.Add(current.Value))
                throw new InvalidOperationException("Invalid parent hierarchy: cycle detected.");

            if (current.Value == currentAgentId)
                throw new InvalidOperationException("Invalid parent hierarchy: cycle detected.");

            current = projectAgents.FirstOrDefault(agent => agent.Id == current.Value)?.ParentAgentId;
        }

        return parentId;
    }

    private static Guid? NormalizeParentId(Guid? requestedParentId, Guid? commandAgentId)
    {
        if (!requestedParentId.HasValue || requestedParentId == Guid.Empty)
            return commandAgentId;

        return requestedParentId;
    }
}

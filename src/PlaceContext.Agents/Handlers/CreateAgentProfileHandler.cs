using PlaceContext.Agents.Contracts.Commands;
using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Domain.Entities;
using PlaceContext.Agents.Domain.Persistence;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Mappers;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Handlers;

public sealed class CreateAgentProfileHandler(
    IAgentsRepository repository, IAgentsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CreateAgentProfileCommand, AgentProfileView>
{
    public async Task<AgentProfileView> HandleAsync(CreateAgentProfileCommand command, CancellationToken ct = default)
    {
        var profile = AgentProfile.Create(command.Name, command.Role, command.Description,
            command.Responsibilities, command.SystemInstructions, command.Provider, command.Model,
            command.ReasoningLevel, command.AllowedTools, command.AllowedJobIds,
            command.AllowedJobChainIds, command.Skills, command.Permissions,
            command.RequirePlanApproval, command.RequireExternalActionApproval,
            command.RequireJobDraftApproval, command.MaxTokensPerAssignment,
            command.MaxCostPerAssignment, command.MaxExecutionMinutes, command.MaxRetries,
            command.MaxDelegationDepth, command.ConcurrencyLimit, clock.UtcNow);
        await repository.AddProfileAsync(profile, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return AgentsViewMapper.ToView(profile);
    }
}

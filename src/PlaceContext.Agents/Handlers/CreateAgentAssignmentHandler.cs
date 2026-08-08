using PlaceContext.Agents.Contracts.Commands;
using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Domain.Entities;
using PlaceContext.Agents.Domain.Persistence;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Domain.ValueObjects;
using PlaceContext.Agents.Mappers;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Handlers;

public sealed class CreateAgentAssignmentHandler(
    IAgentsRepository repository, IAgentsUnitOfWork unitOfWork,
    ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateAgentAssignmentCommand, AgentAssignmentView>
{
    public async Task<AgentAssignmentView> HandleAsync(CreateAgentAssignmentCommand command, CancellationToken ct = default)
    {
        var staff = await repository.GetStaffAsync(command.StaffMemberId, ct)
            ?? throw new InvalidOperationException("Staff member was not found.");
        if (staff.Status != StaffStatus.Active)
            throw new InvalidOperationException("Only active staff can receive new assignments.");
        if (!staff.ProjectIds.Contains(command.ProjectId))
            throw new InvalidOperationException("Staff member is not assigned to this project.");
        var profile = await repository.GetProfileAsync(staff.ProfileId, ct)
            ?? throw new InvalidOperationException("The staff member's profile was not found.");
        var assignment = AgentAssignment.Create(command.StaffMemberId, command.ProjectId,
            command.Objective, profile.Version, currentUser.UserId, clock.UtcNow,
            command.ScheduledFor, command.ParentAssignmentId,
            command.DelegatedByStaffMemberId, command.ScheduleId);
        await repository.AddAssignmentAsync(assignment, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return AgentsViewMapper.ToView(assignment);
    }
}

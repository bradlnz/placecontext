using PlaceContext.Agents.Contracts.Commands;
using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Domain.Entities;
using PlaceContext.Agents.Domain.Persistence;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Mappers;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Handlers;

public sealed class CreateStaffMemberHandler(
    IAgentsRepository repository, IAgentsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CreateStaffMemberCommand, StaffMemberView>
{
    public async Task<StaffMemberView> HandleAsync(CreateStaffMemberCommand command, CancellationToken ct = default)
    {
        if (await repository.GetProfileAsync(command.ProfileId, ct) is null)
            throw new InvalidOperationException("Agent profile was not found.");
        var staff = StaffMember.Create(command.ProfileId, command.Name, command.ProjectIds,
            command.InstructionsOverride, command.ModelOverride, clock.UtcNow);
        await repository.AddStaffAsync(staff, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return AgentsViewMapper.ToView(staff);
    }
}

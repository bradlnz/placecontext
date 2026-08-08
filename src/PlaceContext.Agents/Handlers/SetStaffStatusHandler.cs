using PlaceContext.Agents.Contracts.Commands;
using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Domain.Persistence;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Domain.ValueObjects;
using PlaceContext.Agents.Mappers;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Handlers;

public sealed class SetStaffStatusHandler(
    IAgentsRepository repository, IAgentsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<SetStaffStatusCommand, StaffMemberView?>
{
    public async Task<StaffMemberView?> HandleAsync(SetStaffStatusCommand command, CancellationToken ct = default)
    {
        var staff = await repository.GetStaffAsync(command.Id, ct);
        if (staff is null) return null;
        if (!Enum.TryParse<StaffStatus>(command.Status, true, out var status))
            throw new ArgumentException("Status must be Active, Paused, or Archived.", nameof(command));
        staff.SetStatus(status, clock.UtcNow);
        repository.UpdateStaff(staff);
        await unitOfWork.SaveChangesAsync(ct);
        return AgentsViewMapper.ToView(staff);
    }
}

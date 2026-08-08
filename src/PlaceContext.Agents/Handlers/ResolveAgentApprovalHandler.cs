using PlaceContext.Agents.Contracts.Commands;
using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Agents.Domain.Persistence;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Domain.ValueObjects;
using PlaceContext.Agents.Mappers;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Handlers;

public sealed class ResolveAgentApprovalHandler(
    IAgentsRepository repository, IAgentsUnitOfWork unitOfWork,
    ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ResolveAgentApprovalCommand, AgentApprovalView?>
{
    public async Task<AgentApprovalView?> HandleAsync(ResolveAgentApprovalCommand command, CancellationToken ct = default)
    {
        var approval = await repository.GetApprovalAsync(command.Id, ct);
        if (approval is null) return null;
        if (!Enum.TryParse<ApprovalDecision>(command.Decision, true, out var decision))
            throw new ArgumentException("Decision must be Approve, Reject, or Return.", nameof(command));
        approval.Resolve(decision, currentUser.UserId, command.Comment, clock.UtcNow);
        repository.UpdateApproval(approval);
        await unitOfWork.SaveChangesAsync(ct);
        return AgentsViewMapper.ToView(approval);
    }
}

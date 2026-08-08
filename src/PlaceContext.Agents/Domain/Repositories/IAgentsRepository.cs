using PlaceContext.Agents.Domain.Entities;

namespace PlaceContext.Agents.Domain.Repositories;

public interface IAgentsRepository
{
    Task<IReadOnlyList<AgentProfile>> ListProfilesAsync(CancellationToken ct = default);
    Task<AgentProfile?> GetProfileAsync(Guid id, CancellationToken ct = default);
    Task AddProfileAsync(AgentProfile profile, CancellationToken ct = default);
    void UpdateProfile(AgentProfile profile);
    Task<IReadOnlyList<StaffMember>> ListStaffAsync(Guid? projectId, CancellationToken ct = default);
    Task<StaffMember?> GetStaffAsync(Guid id, CancellationToken ct = default);
    Task AddStaffAsync(StaffMember staffMember, CancellationToken ct = default);
    void UpdateStaff(StaffMember staffMember);
    Task<IReadOnlyList<AgentAssignment>> ListAssignmentsAsync(Guid? projectId, CancellationToken ct = default);
    Task AddAssignmentAsync(AgentAssignment assignment, CancellationToken ct = default);
    Task<IReadOnlyList<AgentApproval>> ListApprovalsAsync(Guid? projectId, CancellationToken ct = default);
    Task<AgentApproval?> GetApprovalAsync(Guid id, CancellationToken ct = default);
    void UpdateApproval(AgentApproval approval);
}

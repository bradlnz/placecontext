using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Agents.Domain.Entities;
using PlaceContext.Agents.Domain.Repositories;
using PlaceContext.Agents.Domain.ValueObjects;

namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class EfAgentsRepository(AgentsDbContext dbContext) : IAgentsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AgentProfile>> ListProfilesAsync(CancellationToken ct = default)
        => (await dbContext.Profiles.AsNoTracking().OrderBy(row => row.Name).ToListAsync(ct))
            .Select(ToDomain).ToArray();

    public async Task<AgentProfile?> GetProfileAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Profiles.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct) is { } row
            ? ToDomain(row) : null;

    public Task AddProfileAsync(AgentProfile profile, CancellationToken ct = default)
        => dbContext.Profiles.AddAsync(ToRow(profile), ct).AsTask();

    public void UpdateProfile(AgentProfile profile) => dbContext.Profiles.Update(ToRow(profile));

    public async Task<IReadOnlyList<StaffMember>> ListStaffAsync(Guid? projectId, CancellationToken ct = default)
    {
        var staff = (await dbContext.Staff.AsNoTracking().OrderBy(row => row.Name).ToListAsync(ct))
            .Select(ToDomain).ToArray();
        return projectId is null ? staff : staff.Where(member => member.ProjectIds.Contains(projectId.Value)).ToArray();
    }

    public async Task<StaffMember?> GetStaffAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Staff.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct) is { } row
            ? ToDomain(row) : null;

    public Task AddStaffAsync(StaffMember staffMember, CancellationToken ct = default)
        => dbContext.Staff.AddAsync(ToRow(staffMember), ct).AsTask();

    public void UpdateStaff(StaffMember staffMember) => dbContext.Staff.Update(ToRow(staffMember));

    public async Task<IReadOnlyList<AgentAssignment>> ListAssignmentsAsync(Guid? projectId, CancellationToken ct = default)
    {
        var query = dbContext.Assignments.AsNoTracking();
        if (projectId is not null) query = query.Where(row => row.ProjectId == projectId);
        return (await query.OrderByDescending(row => row.CreatedAt).ToListAsync(ct)).Select(ToDomain).ToArray();
    }

    public Task AddAssignmentAsync(AgentAssignment assignment, CancellationToken ct = default)
        => dbContext.Assignments.AddAsync(ToRow(assignment), ct).AsTask();

    public async Task<IReadOnlyList<AgentApproval>> ListApprovalsAsync(Guid? projectId, CancellationToken ct = default)
    {
        IQueryable<AgentApprovalRow> query = dbContext.Approvals.AsNoTracking();
        if (projectId is not null)
        {
            var assignmentIds = dbContext.Assignments.Where(row => row.ProjectId == projectId).Select(row => row.Id);
            query = query.Where(row => assignmentIds.Contains(row.AssignmentId));
        }
        return (await query.OrderByDescending(row => row.RequestedAt).ToListAsync(ct)).Select(ToDomain).ToArray();
    }

    public async Task<AgentApproval?> GetApprovalAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Approvals.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct) is { } row
            ? ToDomain(row) : null;

    public void UpdateApproval(AgentApproval approval) => dbContext.Approvals.Update(ToRow(approval));

    private static AgentProfile ToDomain(AgentProfileRow row) => AgentProfile.Rehydrate(
        row.Id, row.Name, row.Role, row.Description, row.Responsibilities,
        row.SystemInstructions, row.Provider, row.Model, row.ReasoningLevel,
        Read<string>(row.AllowedToolsJson), Read<Guid>(row.AllowedJobIdsJson),
        Read<Guid>(row.AllowedJobChainIdsJson), Read<string>(row.SkillsJson),
        Read<string>(row.PermissionsJson), row.RequirePlanApproval,
        row.RequireExternalActionApproval, row.RequireJobDraftApproval,
        row.MaxTokensPerAssignment, row.MaxCostPerAssignment, row.MaxExecutionMinutes,
        row.MaxRetries, row.MaxDelegationDepth, row.ConcurrencyLimit, row.Version,
        row.CreatedAt, row.UpdatedAt);

    private static AgentProfileRow ToRow(AgentProfile profile) => new()
    {
        Id = profile.Id, Name = profile.Name, Role = profile.Role,
        Description = profile.Description, Responsibilities = profile.Responsibilities,
        SystemInstructions = profile.SystemInstructions, Provider = profile.Provider,
        Model = profile.Model, ReasoningLevel = profile.ReasoningLevel,
        AllowedToolsJson = Write(profile.AllowedTools), AllowedJobIdsJson = Write(profile.AllowedJobIds),
        AllowedJobChainIdsJson = Write(profile.AllowedJobChainIds), SkillsJson = Write(profile.Skills),
        PermissionsJson = Write(profile.Permissions), RequirePlanApproval = profile.RequirePlanApproval,
        RequireExternalActionApproval = profile.RequireExternalActionApproval,
        RequireJobDraftApproval = profile.RequireJobDraftApproval,
        MaxTokensPerAssignment = profile.MaxTokensPerAssignment,
        MaxCostPerAssignment = profile.MaxCostPerAssignment,
        MaxExecutionMinutes = profile.MaxExecutionMinutes, MaxRetries = profile.MaxRetries,
        MaxDelegationDepth = profile.MaxDelegationDepth, ConcurrencyLimit = profile.ConcurrencyLimit,
        Version = profile.Version, CreatedAt = profile.CreatedAt, UpdatedAt = profile.UpdatedAt,
    };

    private static StaffMember ToDomain(StaffMemberRow row) => StaffMember.Rehydrate(
        row.Id, row.ProfileId, row.Name, Read<Guid>(row.ProjectIdsJson),
        row.InstructionsOverride, row.ModelOverride, Enum.Parse<StaffStatus>(row.Status),
        row.CreatedAt, row.UpdatedAt);

    private static StaffMemberRow ToRow(StaffMember staff) => new()
    {
        Id = staff.Id, ProfileId = staff.ProfileId, Name = staff.Name,
        ProjectIdsJson = Write(staff.ProjectIds), InstructionsOverride = staff.InstructionsOverride,
        ModelOverride = staff.ModelOverride, Status = staff.Status.ToString(),
        CreatedAt = staff.CreatedAt, UpdatedAt = staff.UpdatedAt,
    };

    private static AgentAssignment ToDomain(AgentAssignmentRow row) => AgentAssignment.Rehydrate(
        row.Id, row.StaffMemberId, row.ProjectId, row.ParentAssignmentId,
        row.DelegatedByStaffMemberId, row.ScheduleId, row.CreatedByUserId,
        row.Objective, row.ProfileVersion, Enum.Parse<AssignmentStatus>(row.Status),
        row.ScheduledFor, row.PlanSummary, row.ResultSummary, row.FailureSummary,
        row.CreatedAt, row.UpdatedAt);

    private static AgentAssignmentRow ToRow(AgentAssignment assignment) => new()
    {
        Id = assignment.Id, StaffMemberId = assignment.StaffMemberId,
        ProjectId = assignment.ProjectId, ParentAssignmentId = assignment.ParentAssignmentId,
        DelegatedByStaffMemberId = assignment.DelegatedByStaffMemberId,
        ScheduleId = assignment.ScheduleId, CreatedByUserId = assignment.CreatedByUserId,
        Objective = assignment.Objective, ProfileVersion = assignment.ProfileVersion,
        Status = assignment.Status.ToString(), ScheduledFor = assignment.ScheduledFor,
        PlanSummary = assignment.PlanSummary, ResultSummary = assignment.ResultSummary,
        FailureSummary = assignment.FailureSummary, CreatedAt = assignment.CreatedAt,
        UpdatedAt = assignment.UpdatedAt,
    };

    private static AgentApproval ToDomain(AgentApprovalRow row) => AgentApproval.Rehydrate(
        row.Id, row.AssignmentId, row.ActionKind, row.Summary, row.PayloadJson,
        Enum.Parse<ApprovalStatus>(row.Status), row.ResolvedByUserId,
        row.ReviewerComment, row.RequestedAt, row.ResolvedAt);

    private static AgentApprovalRow ToRow(AgentApproval approval) => new()
    {
        Id = approval.Id, AssignmentId = approval.AssignmentId,
        ActionKind = approval.ActionKind, Summary = approval.Summary,
        PayloadJson = approval.PayloadJson, Status = approval.Status.ToString(),
        ResolvedByUserId = approval.ResolvedByUserId,
        ReviewerComment = approval.ReviewerComment,
        RequestedAt = approval.RequestedAt, ResolvedAt = approval.ResolvedAt,
    };

    private static IReadOnlyList<T> Read<T>(string json)
        => JsonSerializer.Deserialize<IReadOnlyList<T>>(json, JsonOptions) ?? [];

    private static string Write<T>(IReadOnlyList<T> value)
        => JsonSerializer.Serialize(value, JsonOptions);
}

using PlaceContext.Agents.Domain.Entities;
using PlaceContext.Agents.Domain.ValueObjects;

namespace PlaceContext.Agents.Tests;

public sealed class StaffAndAssignmentTests
{
    [Fact]
    public void Staff_requires_a_project_and_can_be_paused_without_losing_identity()
    {
        var projectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var staff = StaffMember.Create(Guid.NewGuid(), "Ada", [projectId], null, null, now);

        staff.SetStatus(StaffStatus.Paused, now.AddMinutes(1));

        Assert.Equal(StaffStatus.Paused, staff.Status);
        Assert.Contains(projectId, staff.ProjectIds);
    }

    [Fact]
    public void Assignment_pins_profile_version_and_delegation_parent()
    {
        var parentId = Guid.NewGuid();
        var delegatorId = Guid.NewGuid();
        var assignment = AgentAssignment.Create(Guid.NewGuid(), Guid.NewGuid(),
            "Investigate the site and report risks", 4, Guid.NewGuid(), DateTimeOffset.UtcNow,
            parentAssignmentId: parentId, delegatedByStaffMemberId: delegatorId);

        Assert.Equal(4, assignment.ProfileVersion);
        Assert.Equal(parentId, assignment.ParentAssignmentId);
        Assert.Equal(delegatorId, assignment.DelegatedByStaffMemberId);
        Assert.Equal(AssignmentStatus.Queued, assignment.Status);
    }
}

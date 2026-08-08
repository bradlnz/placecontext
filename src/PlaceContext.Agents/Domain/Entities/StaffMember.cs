using PlaceContext.Agents.Domain.ValueObjects;
using PlaceContext.Domain.Common;

namespace PlaceContext.Agents.Domain.Entities;

public sealed class StaffMember : AggregateRoot
{
    private StaffMember() { }

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyList<Guid> ProjectIds { get; private set; } = [];
    public string InstructionsOverride { get; private set; } = string.Empty;
    public string? ModelOverride { get; private set; }
    public StaffStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static StaffMember Create(Guid profileId, string name, IEnumerable<Guid> projectIds,
        string? instructionsOverride, string? modelOverride, DateTimeOffset now)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("Profile is required.", nameof(profileId));
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length is 0 or > 120) throw new ArgumentException("Name must contain 1-120 characters.", nameof(name));
        var projects = projectIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (projects.Length == 0) throw new ArgumentException("At least one project is required.", nameof(projectIds));
        return new StaffMember
        {
            Id = Guid.NewGuid(), ProfileId = profileId, Name = cleanName,
            ProjectIds = projects, InstructionsOverride = (instructionsOverride ?? string.Empty).Trim(),
            ModelOverride = string.IsNullOrWhiteSpace(modelOverride) ? null : modelOverride.Trim(),
            Status = StaffStatus.Active, CreatedAt = now, UpdatedAt = now,
        };
    }

    public void SetStatus(StaffStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }

    public static StaffMember Rehydrate(Guid id, Guid profileId, string name,
        IReadOnlyList<Guid> projectIds, string instructionsOverride, string? modelOverride,
        StaffStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new() { Id = id, ProfileId = profileId, Name = name, ProjectIds = projectIds,
            InstructionsOverride = instructionsOverride, ModelOverride = modelOverride,
            Status = status, CreatedAt = createdAt, UpdatedAt = updatedAt };
}

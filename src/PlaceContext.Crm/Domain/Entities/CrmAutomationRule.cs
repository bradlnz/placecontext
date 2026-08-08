using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>Maps a CRM event and optional lifecycle-stage filter to a project job chain.</summary>
public sealed class CrmAutomationRule
{
    private CrmAutomationRule(
        Guid id, Guid projectId, string name, CrmAutomationEventType eventType,
        CustomerLifecycleStage? lifecycleStage, Guid chainId, bool enabled,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        EventType = eventType;
        LifecycleStage = lifecycleStage;
        ChainId = chainId;
        Enabled = enabled;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Name { get; private set; }
    public CrmAutomationEventType EventType { get; private set; }
    public CustomerLifecycleStage? LifecycleStage { get; private set; }
    public Guid ChainId { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CrmAutomationRule Create(
        Guid projectId, string name, CrmAutomationEventType eventType,
        CustomerLifecycleStage? lifecycleStage, Guid chainId, bool enabled, DateTimeOffset now)
    {
        Validate(projectId, name, chainId);
        return new CrmAutomationRule(Guid.NewGuid(), projectId, name.Trim(), eventType,
            lifecycleStage, chainId, enabled, now, now);
    }

    public static CrmAutomationRule Rehydrate(
        Guid id, Guid projectId, string name, CrmAutomationEventType eventType,
        CustomerLifecycleStage? lifecycleStage, Guid chainId, bool enabled,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, name, eventType, lifecycleStage, chainId, enabled, createdAt, updatedAt);

    public void Update(
        string name, CrmAutomationEventType eventType, CustomerLifecycleStage? lifecycleStage,
        Guid chainId, bool enabled, DateTimeOffset now)
    {
        Validate(ProjectId, name, chainId);
        Name = name.Trim();
        EventType = eventType;
        LifecycleStage = lifecycleStage;
        ChainId = chainId;
        Enabled = enabled;
        UpdatedAt = now;
    }

    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        Enabled = enabled;
        UpdatedAt = now;
    }

    public bool Matches(CrmAutomationEventType eventType, CustomerLifecycleStage stage)
        => Enabled && EventType == eventType
            && (LifecycleStage is null || LifecycleStage == stage);

    private static void Validate(Guid projectId, string name, Guid chainId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId must not be empty.");
        if (chainId == Guid.Empty) throw new ArgumentException("Choose a job chain.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Automation needs a name.");
    }
}

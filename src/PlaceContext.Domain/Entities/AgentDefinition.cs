using PlaceContext.Domain.Common;

namespace PlaceContext.Domain.Entities;

/// <summary>A project-scoped command or worker agent with explicit execution boundaries.</summary>
public sealed class AgentDefinition : AggregateRoot
{
    private AgentDefinition(
        Guid id,
        Guid projectId,
        AgentKind kind,
        string name,
        string description,
        string instructions,
        string templateKey,
        string schema,
        IReadOnlyList<AgentCapability> capabilities,
        IReadOnlyList<Guid> allowedJobIds,
        bool enabled,
        Guid? parentAgentId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Kind = kind;
        Name = name;
        Description = description;
        Instructions = instructions;
        Schema = schema;
        TemplateKey = templateKey;
        Capabilities = capabilities;
        AllowedJobIds = allowedJobIds;
        Enabled = enabled;
        ParentAgentId = parentAgentId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public AgentKind Kind { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string Instructions { get; private set; }
    public string Schema { get; private set; }
    public string TemplateKey { get; private set; }
    public IReadOnlyList<AgentCapability> Capabilities { get; private set; }
    public IReadOnlyList<Guid> AllowedJobIds { get; private set; }
    public bool Enabled { get; private set; }
    public Guid? ParentAgentId { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AgentDefinition CreateCommand(Guid projectId, DateTimeOffset now)
    {
        EnsureProject(projectId);
        return new AgentDefinition(
            Guid.NewGuid(), projectId, AgentKind.Command, "Command Agent",
            "Routes work to the right agent and coordinates the final response.",
            "Coordinate worker agents toward the shared goal. Choose the least-privileged capable collaborators for each request, combine their contributions, and use the project data graph as the authoritative knowledge source.",
            "command", "{}", Enum.GetValues<AgentCapability>(), [], true, null, now, now);
    }

    public static AgentDefinition CreateWorker(
        Guid projectId,
        string name,
        string description,
        string instructions,
        string templateKey,
        string schema,
        IEnumerable<AgentCapability> capabilities,
        IEnumerable<Guid> allowedJobIds,
        Guid? parentAgentId,
        DateTimeOffset now)
    {
        EnsureProject(projectId);
        EnsureName(name);
        return new AgentDefinition(
            Guid.NewGuid(), projectId, AgentKind.Worker, name.Trim(), Normalize(description, 500, nameof(description)),
            Normalize(instructions, 12_000, nameof(instructions)), Normalize(templateKey, 100, nameof(templateKey)), NormalizeSchema(schema),
            NormalizeCapabilities(capabilities),
            NormalizeJobs(allowedJobIds), true, parentAgentId, now, now);
    }

    public static AgentDefinition Rehydrate(
        Guid id,
        Guid projectId,
        AgentKind kind,
        string name,
        string description,
        string instructions,
        string templateKey,
        string schema,
        IEnumerable<AgentCapability> capabilities,
        IEnumerable<Guid> allowedJobIds,
        bool enabled,
        Guid? parentAgentId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        => new(id, projectId, kind, name, description, instructions,
            templateKey,
            kind == AgentKind.Command ? "{}" : NormalizeSchema(schema),
            kind == AgentKind.Command ? Enum.GetValues<AgentCapability>() : NormalizeCapabilities(capabilities),
            kind == AgentKind.Command ? [] : NormalizeJobs(allowedJobIds),
            kind == AgentKind.Command || enabled, kind == AgentKind.Command ? null : parentAgentId, createdAt, updatedAt);

    public void Update(
        string name,
        string description,
        string instructions,
        string templateKey,
        IEnumerable<AgentCapability> capabilities,
        IEnumerable<Guid> allowedJobIds,
        bool enabled,
        Guid? parentAgentId,
        DateTimeOffset updatedAt,
        string schema)
    {
        EnsureName(name);
        Name = name.Trim();
        Description = Normalize(description, 500, nameof(description));
        Instructions = Normalize(instructions, 12_000, nameof(instructions));
        TemplateKey = Normalize(templateKey, 100, nameof(templateKey));
        Schema = NormalizeSchema(schema);
        Capabilities = Kind == AgentKind.Command
            ? Enum.GetValues<AgentCapability>()
            : NormalizeCapabilities(capabilities);
        AllowedJobIds = Kind == AgentKind.Command ? [] : NormalizeJobs(allowedJobIds);
        Enabled = Kind == AgentKind.Command || enabled;
        ParentAgentId = Kind == AgentKind.Command ? null : parentAgentId;
        UpdatedAt = updatedAt;
    }

    private static IReadOnlyList<AgentCapability> NormalizeCapabilities(IEnumerable<AgentCapability>? capabilities)
        => (capabilities ?? [])
            .Append(AgentCapability.GraphRead)
            .Distinct()
            .Order()
            .ToArray();

    private static IReadOnlyList<Guid> NormalizeJobs(IEnumerable<Guid>? jobIds)
        => (jobIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();

    private static string Normalize(string? value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxLength)
            throw new ArgumentException($"{parameterName} must be {maxLength} characters or fewer.", parameterName);
        return normalized;
    }

    private static string NormalizeSchema(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Schema is required.", nameof(value));
        if (normalized.Length > 12_000)
            throw new ArgumentException("Schema must be 12,000 characters or fewer.", nameof(value));
        return normalized;
    }

    private static void EnsureProject(Guid projectId)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
    }

    private static void EnsureName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Agent name must not be empty.", nameof(name));
        if (name.Trim().Length > 100)
            throw new ArgumentException("Agent name must be 100 characters or fewer.", nameof(name));
    }
}

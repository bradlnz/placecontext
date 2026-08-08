using PlaceContext.Domain.Common;

namespace PlaceContext.Agents.Domain.Entities;

public sealed class AgentProfile : AggregateRoot
{
    private AgentProfile() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Responsibilities { get; private set; } = string.Empty;
    public string SystemInstructions { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string ReasoningLevel { get; private set; } = string.Empty;
    public IReadOnlyList<string> AllowedTools { get; private set; } = [];
    public IReadOnlyList<Guid> AllowedJobIds { get; private set; } = [];
    public IReadOnlyList<Guid> AllowedJobChainIds { get; private set; } = [];
    public IReadOnlyList<string> Skills { get; private set; } = [];
    public IReadOnlyList<string> Permissions { get; private set; } = [];
    public bool RequirePlanApproval { get; private set; }
    public bool RequireExternalActionApproval { get; private set; }
    public bool RequireJobDraftApproval { get; private set; }
    public long MaxTokensPerAssignment { get; private set; }
    public decimal MaxCostPerAssignment { get; private set; }
    public int MaxExecutionMinutes { get; private set; }
    public int MaxRetries { get; private set; }
    public int MaxDelegationDepth { get; private set; }
    public int ConcurrencyLimit { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AgentProfile Create(
        string name, string role, string description, string responsibilities,
        string systemInstructions, string provider, string model, string reasoningLevel,
        IEnumerable<string>? allowedTools, IEnumerable<Guid>? allowedJobIds,
        IEnumerable<Guid>? allowedJobChainIds, IEnumerable<string>? skills,
        IEnumerable<string>? permissions, bool requirePlanApproval,
        bool requireExternalActionApproval, bool requireJobDraftApproval,
        long maxTokensPerAssignment, decimal maxCostPerAssignment,
        int maxExecutionMinutes, int maxRetries, int maxDelegationDepth,
        int concurrencyLimit, DateTimeOffset now)
    {
        var profile = new AgentProfile { Id = Guid.NewGuid(), CreatedAt = now, Version = 0 };
        profile.Update(name, role, description, responsibilities, systemInstructions,
            provider, model, reasoningLevel, allowedTools, allowedJobIds, allowedJobChainIds,
            skills, permissions, requirePlanApproval, requireExternalActionApproval,
            requireJobDraftApproval, maxTokensPerAssignment, maxCostPerAssignment,
            maxExecutionMinutes, maxRetries, maxDelegationDepth, concurrencyLimit, now);
        return profile;
    }

    public void Update(
        string name, string role, string description, string responsibilities,
        string systemInstructions, string provider, string model, string reasoningLevel,
        IEnumerable<string>? allowedTools, IEnumerable<Guid>? allowedJobIds,
        IEnumerable<Guid>? allowedJobChainIds, IEnumerable<string>? skills,
        IEnumerable<string>? permissions, bool requirePlanApproval,
        bool requireExternalActionApproval, bool requireJobDraftApproval,
        long maxTokensPerAssignment, decimal maxCostPerAssignment,
        int maxExecutionMinutes, int maxRetries, int maxDelegationDepth,
        int concurrencyLimit, DateTimeOffset now)
    {
        Name = Required(name, nameof(name), 120);
        Role = Required(role, nameof(role), 120);
        Description = (description ?? string.Empty).Trim();
        Responsibilities = (responsibilities ?? string.Empty).Trim();
        SystemInstructions = Required(systemInstructions, nameof(systemInstructions), 50_000);
        Provider = Required(provider, nameof(provider), 80);
        Model = Required(model, nameof(model), 160);
        ReasoningLevel = Required(reasoningLevel, nameof(reasoningLevel), 40);
        AllowedTools = Clean(allowedTools);
        AllowedJobIds = (allowedJobIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        AllowedJobChainIds = (allowedJobChainIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        Skills = Clean(skills);
        Permissions = Clean(permissions);
        RequirePlanApproval = requirePlanApproval;
        RequireExternalActionApproval = requireExternalActionApproval;
        RequireJobDraftApproval = requireJobDraftApproval;
        MaxTokensPerAssignment = Positive(maxTokensPerAssignment, nameof(maxTokensPerAssignment));
        if (maxCostPerAssignment < 0) throw new ArgumentOutOfRangeException(nameof(maxCostPerAssignment));
        MaxCostPerAssignment = maxCostPerAssignment;
        MaxExecutionMinutes = Positive(maxExecutionMinutes, nameof(maxExecutionMinutes));
        MaxRetries = NonNegative(maxRetries, nameof(maxRetries));
        MaxDelegationDepth = NonNegative(maxDelegationDepth, nameof(maxDelegationDepth));
        ConcurrencyLimit = Positive(concurrencyLimit, nameof(concurrencyLimit));
        Version++;
        UpdatedAt = now;
    }

    public static AgentProfile Rehydrate(
        Guid id, string name, string role, string description, string responsibilities,
        string systemInstructions, string provider, string model, string reasoningLevel,
        IReadOnlyList<string> allowedTools, IReadOnlyList<Guid> allowedJobIds,
        IReadOnlyList<Guid> allowedJobChainIds, IReadOnlyList<string> skills,
        IReadOnlyList<string> permissions, bool requirePlanApproval,
        bool requireExternalActionApproval, bool requireJobDraftApproval,
        long maxTokensPerAssignment, decimal maxCostPerAssignment,
        int maxExecutionMinutes, int maxRetries, int maxDelegationDepth,
        int concurrencyLimit, int version, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new()
        {
            Id = id, Name = name, Role = role, Description = description,
            Responsibilities = responsibilities, SystemInstructions = systemInstructions,
            Provider = provider, Model = model, ReasoningLevel = reasoningLevel,
            AllowedTools = allowedTools, AllowedJobIds = allowedJobIds,
            AllowedJobChainIds = allowedJobChainIds, Skills = skills, Permissions = permissions,
            RequirePlanApproval = requirePlanApproval,
            RequireExternalActionApproval = requireExternalActionApproval,
            RequireJobDraftApproval = requireJobDraftApproval,
            MaxTokensPerAssignment = maxTokensPerAssignment,
            MaxCostPerAssignment = maxCostPerAssignment,
            MaxExecutionMinutes = maxExecutionMinutes, MaxRetries = maxRetries,
            MaxDelegationDepth = maxDelegationDepth, ConcurrencyLimit = concurrencyLimit,
            Version = version, CreatedAt = createdAt, UpdatedAt = updatedAt,
        };

    private static string Required(string? value, string parameter, int maxLength)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length == 0) throw new ArgumentException("Value is required.", parameter);
        if (result.Length > maxLength) throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameter);
        return result;
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string>? values)
        => (values ?? []).Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static int Positive(int value, string parameter)
        => value > 0 ? value : throw new ArgumentOutOfRangeException(parameter);

    private static long Positive(long value, string parameter)
        => value > 0 ? value : throw new ArgumentOutOfRangeException(parameter);

    private static int NonNegative(int value, string parameter)
        => value >= 0 ? value : throw new ArgumentOutOfRangeException(parameter);
}

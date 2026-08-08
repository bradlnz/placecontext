namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class AgentProfileRow : IAgentsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public string SystemInstructions { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ReasoningLevel { get; set; } = string.Empty;
    public string AllowedToolsJson { get; set; } = "[]";
    public string AllowedJobIdsJson { get; set; } = "[]";
    public string AllowedJobChainIdsJson { get; set; } = "[]";
    public string SkillsJson { get; set; } = "[]";
    public string PermissionsJson { get; set; } = "[]";
    public bool RequirePlanApproval { get; set; }
    public bool RequireExternalActionApproval { get; set; }
    public bool RequireJobDraftApproval { get; set; }
    public long MaxTokensPerAssignment { get; set; }
    public decimal MaxCostPerAssignment { get; set; }
    public int MaxExecutionMinutes { get; set; }
    public int MaxRetries { get; set; }
    public int MaxDelegationDepth { get; set; }
    public int ConcurrencyLimit { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

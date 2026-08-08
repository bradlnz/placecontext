namespace PlaceContext.Agents.Contracts.Dtos;

public sealed record AgentProfileView(
    Guid Id, string Name, string Role, string Description, string Responsibilities,
    string SystemInstructions, string Provider, string Model, string ReasoningLevel,
    IReadOnlyList<string> AllowedTools, IReadOnlyList<Guid> AllowedJobIds,
    IReadOnlyList<Guid> AllowedJobChainIds, IReadOnlyList<string> Skills,
    IReadOnlyList<string> Permissions, bool RequirePlanApproval,
    bool RequireExternalActionApproval, bool RequireJobDraftApproval,
    long MaxTokensPerAssignment, decimal MaxCostPerAssignment,
    int MaxExecutionMinutes, int MaxRetries, int MaxDelegationDepth,
    int ConcurrencyLimit, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

using PlaceContext.Agents.Contracts.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Contracts.Commands;

public sealed record CreateAgentProfileCommand(
    string Name, string Role, string Description, string Responsibilities,
    string SystemInstructions, string Provider, string Model, string ReasoningLevel,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<Guid>? AllowedJobIds = null,
    IReadOnlyList<Guid>? AllowedJobChainIds = null,
    IReadOnlyList<string>? Skills = null,
    IReadOnlyList<string>? Permissions = null,
    bool RequirePlanApproval = true,
    bool RequireExternalActionApproval = true,
    bool RequireJobDraftApproval = true,
    long MaxTokensPerAssignment = 200_000,
    decimal MaxCostPerAssignment = 25m,
    int MaxExecutionMinutes = 120,
    int MaxRetries = 3,
    int MaxDelegationDepth = 3,
    int ConcurrencyLimit = 1) : ICommand<AgentProfileView>, IRequiresPermission
{
    string IRequiresPermission.RequiredPermission => Permission.AgentsManage;
}

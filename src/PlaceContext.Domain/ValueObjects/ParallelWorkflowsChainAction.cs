namespace PlaceContext.Domain.ValueObjects;

public sealed record ConditionalWorkflowBranch(Guid WorkflowId, string WorkflowName, string Condition);

public sealed class ParallelWorkflowsChainAction : ChainAction
{
    public const string ActionType = "parallelWorkflows";
    public ParallelWorkflowsChainAction(IReadOnlyList<ConditionalWorkflowBranch> branches, string outputField = "parallel_results")
    {
        if (branches is null || branches.Count is < 2 or > 8) throw new ArgumentException("Choose two to eight conditional workflow branches.");
        if (!ScoringGateChainAction.ValidField(outputField)) throw new ArgumentException("Choose a valid output field.");
        foreach (var branch in branches)
        {
            if (branch is null) throw new ArgumentException("A workflow branch is required.");
            _ = new SubWorkflowChainAction(branch.WorkflowId, branch.WorkflowName);
            if (string.IsNullOrWhiteSpace(branch.Condition)) throw new ArgumentException("Each branch needs an if condition.");
        }
        Branches = branches.ToArray(); OutputField = outputField;
    }
    public IReadOnlyList<ConditionalWorkflowBranch> Branches { get; }
    public string OutputField { get; }
    public override string Type => ActionType;
    public override string DisplayName => "Parallel if gates";
}

namespace PlaceContext.Domain.ValueObjects;

public sealed class SubWorkflowChainAction : ChainAction
{
    public const string ActionType = "subWorkflow";
    public SubWorkflowChainAction(Guid workflowId, string workflowName)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("Choose a workflow.");
        if (string.IsNullOrWhiteSpace(workflowName) || workflowName.Length > 200) throw new ArgumentException("Enter a workflow name of up to 200 characters.");
        WorkflowId = workflowId;
        WorkflowName = workflowName.Trim();
    }
    public Guid WorkflowId { get; }
    public string WorkflowName { get; }
    public override string Type => ActionType;
    public override string DisplayName => $"Sub-workflow · {WorkflowName}";
}

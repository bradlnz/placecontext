namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentWorkflow(string Id, string Name, string Industry, string Description,
    bool Paid, IReadOnlyList<string> Steps, IReadOnlyList<string> InputFields)
{
    public string UseCaseId { get; init; } = Id;
    public string UseCaseName { get; init; } = Name;
    public string Outcome { get; init; } = "";
    public string IntendedFor { get; init; } = "";
    public IReadOnlyList<string> DataSources { get; init; } = [];
    public IReadOnlyList<string> Deliverables { get; init; } = [];
    public IReadOnlyList<string> RequiredInputFields { get; init; } = InputFields;
    public IReadOnlyDictionary<string, string> InputHints { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SourceAliases { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public string ExecutionScope { get; init; } = "Reviews supplied information; external actions require a separate workflow.";
}

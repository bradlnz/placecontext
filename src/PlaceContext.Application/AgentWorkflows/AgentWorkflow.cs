namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentWorkflow(string Id, string Name, string Industry, string Description,
    bool Paid, IReadOnlyList<string> Steps, IReadOnlyList<string> InputFields);

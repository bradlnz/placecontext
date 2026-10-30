namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentPreparation(IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<AgentContextSource> Sources, IReadOnlyList<string> MissingFields, string Notice);

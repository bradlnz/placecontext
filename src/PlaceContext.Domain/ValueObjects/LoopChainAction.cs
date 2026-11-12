namespace PlaceContext.Domain.ValueObjects;

/// <summary>A human hand-off or an agent step with external, automatic, or verification execution.</summary>
public sealed class LoopChainAction : ChainAction
{
    public const string HumanType = "humanInput";
    public const string AgentType = "agentInput";
    public const string TaskType = "agentTask";
    public const string TextResponse = "text";
    public const string YesNoResponse = "yesNo";
    public const string ExternalAgent = "external";
    public const string JevAgent = "jev";
    public const string AutomaticAgent = "automatic";

    public LoopChainAction(string type, string instructions, string responseField, string responseType = TextResponse,
        string agentProvider = ExternalAgent, Guid? connectionId = null, string? model = null, Guid? agentId = null)
    {
        if (type is not (HumanType or AgentType or TaskType))
        {
            throw new ArgumentException("Choose a human or agent step.", nameof(type));
        }
        if (responseType is not (TextResponse or YesNoResponse) || type != HumanType && responseType != TextResponse)
        {
            throw new ArgumentException("Choose a text response, or Yes/No for a human step.", nameof(responseType));
        }
        if (agentProvider is not (ExternalAgent or JevAgent or AutomaticAgent)
            || type == HumanType && agentProvider != ExternalAgent
            || type == TaskType && agentProvider == JevAgent)
        {
            throw new ArgumentException("Choose automatic execution, an external agent, or AI verification for an agent step.", nameof(agentProvider));
        }
        if (string.IsNullOrWhiteSpace(instructions) || instructions.Trim().Length > 4000)
        {
            throw new ArgumentException("Instructions are required (up to 4,000 characters).", nameof(instructions));
        }
        responseField = responseField?.Trim() ?? "";
        if (responseField.Length is < 1 or > 80 || !char.IsAsciiLetter(responseField[0])
            || responseField.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
        {
            throw new ArgumentException("Response field must start with a letter and contain only letters, numbers or underscores (up to 80 characters).", nameof(responseField));
        }
        if (agentProvider == AutomaticAgent && (connectionId is null || connectionId == Guid.Empty
            || string.IsNullOrWhiteSpace(model) || model.Length > 200))
            throw new ArgumentException("Choose a model connection and model for automatic execution.");
        if (agentId == Guid.Empty) throw new ArgumentException("Choose an agent from this project.");
        if (agentProvider != AutomaticAgent && (connectionId is not null || model is not null || agentId is not null))
            throw new ArgumentException("Model selection is only used by automatic execution.");
        ConnectionId = connectionId;
        AgentId = agentId;
        Model = model?.Trim();
        Type = type;
        Instructions = instructions.Trim();
        ResponseField = responseField;
        ResponseType = responseType;
        AgentProvider = agentProvider;
    }

    public override string Type
    {
        get;
    }
    public override string DisplayName => Type == TaskType ? "Agent task" : Type == HumanType ? "Human in the loop" : "Agent in the loop";
    public string Instructions
    {
        get;
    }
    public string ResponseField
    {
        get;
    }
    public string ResponseType
    {
        get;
    }
    public Guid? ConnectionId { get; }
    public string? Model { get; }
    public Guid? AgentId { get; }
    public string AgentProvider
    {
        get;
    }
}

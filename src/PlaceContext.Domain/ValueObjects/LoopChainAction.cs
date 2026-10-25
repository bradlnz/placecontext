namespace PlaceContext.Domain.ValueObjects;

/// <summary>A durable hand-off to a human or an external agent.</summary>
public sealed class LoopChainAction : ChainAction
{
    public const string HumanType = "humanInput";
    public const string AgentType = "agentInput";
    public const string TextResponse = "text";
    public const string YesNoResponse = "yesNo";

    public LoopChainAction(string type, string instructions, string responseField, string responseType = TextResponse)
    {
        if (type is not (HumanType or AgentType))
            throw new ArgumentException("Choose a human or agent step.", nameof(type));
        if (responseType is not (TextResponse or YesNoResponse) || type == AgentType && responseType != TextResponse)
            throw new ArgumentException("Choose a text response, or Yes/No for a human step.", nameof(responseType));
        if (string.IsNullOrWhiteSpace(instructions) || instructions.Trim().Length > 4000)
            throw new ArgumentException("Instructions are required (up to 4,000 characters).", nameof(instructions));
        responseField = responseField?.Trim() ?? "";
        if (responseField.Length is < 1 or > 80 || !char.IsAsciiLetter(responseField[0])
            || responseField.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Response field must start with a letter and contain only letters, numbers or underscores (up to 80 characters).", nameof(responseField));
        Type = type;
        Instructions = instructions.Trim();
        ResponseField = responseField;
        ResponseType = responseType;
    }

    public override string Type { get; }
    public override string DisplayName => Type == HumanType ? "Human in the loop" : "Agent in the loop";
    public string Instructions { get; }
    public string ResponseField { get; }
    public string ResponseType { get; }
}

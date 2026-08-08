namespace PlaceContext.Host.Components.ViewModels;

internal sealed class ChatHallucinationResult
{
    public bool Detected { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? ArtifactId { get; init; }
    public string? CorrectionPrompt { get; init; }
}

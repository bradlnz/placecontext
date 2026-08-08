namespace PlaceContext.Host.Components.ViewModels;

internal sealed class ChatArtifactRenderInfo
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsText { get; set; }
    public string? Content { get; set; }
    public string? ExtractedText { get; set; }
    public bool ExtractedTruncated { get; set; }
    public bool Truncated { get; set; }
}

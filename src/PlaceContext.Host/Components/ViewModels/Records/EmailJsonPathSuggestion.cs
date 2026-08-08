namespace PlaceContext.Host.Components.ViewModels;

public sealed record EmailJsonPathSuggestion(
    string Path,
    string Preview,
    bool IsAttachmentCandidate);

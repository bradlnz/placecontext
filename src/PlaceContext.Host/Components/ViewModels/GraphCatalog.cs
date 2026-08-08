using Microsoft.JSInterop;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public static class GraphCatalog
{
    public static GraphNodeKind NodeKind(string? value) =>
        string.Equals(value, "good", StringComparison.OrdinalIgnoreCase)
            ? GraphNodeKind.Artifact
            : GraphNodeKind.Unknown;

    public static GraphLinkConfidence LinkConfidence(string? value) =>
        string.Equals(value, "Ambiguous", StringComparison.OrdinalIgnoreCase)
            ? GraphLinkConfidence.Ambiguous
            : GraphLinkConfidence.Normal;
}

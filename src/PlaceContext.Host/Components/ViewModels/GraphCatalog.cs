using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Host.Components.ViewModels;

public static class GraphCatalog
{
    public static GraphNodeKind NodeKind(string? value) =>
        string.Equals(value, "good", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "artifact", StringComparison.OrdinalIgnoreCase)
            ? GraphNodeKind.Artifact
            : GraphNodeKind.Unknown;

    public static GraphLinkConfidence LinkConfidence(string? value) =>
        string.Equals(value, "Ambiguous", StringComparison.OrdinalIgnoreCase)
            ? GraphLinkConfidence.Ambiguous
            : GraphLinkConfidence.Normal;
}

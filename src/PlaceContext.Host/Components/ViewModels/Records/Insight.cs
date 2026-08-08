namespace PlaceContext.Host.Components.ViewModels;

public sealed record Insight(
    string Title,
    string? Big,
    string? Sub,
    IReadOnlyList<(string Label, string Count, int Frac)> Bars);

namespace PlaceContext.Host.Components.ViewModels;

public sealed record EntityChart(
    string Column,
    IReadOnlyList<(string Label, string Count, int Frac)> Bars);

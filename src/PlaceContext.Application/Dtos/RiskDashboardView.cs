using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a risk dashboard for one project.</summary>
public sealed record RiskDashboardView(
    double Technical,
    string TechnicalBand,
    double Process,
    string ProcessBand,
    IReadOnlyList<RiskSignalView> TechnicalSignals,
    IReadOnlyList<RiskSignalView> ProcessSignals,
    DateTimeOffset? ComputedAt)
{
    public static readonly RiskDashboardView Empty =
        new(0, nameof(RiskBand.Low), 0, nameof(RiskBand.Low),
            Array.Empty<RiskSignalView>(), Array.Empty<RiskSignalView>(), null);
}

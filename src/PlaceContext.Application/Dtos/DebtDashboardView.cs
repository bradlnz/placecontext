using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Read model: a debt dashboard for one project.</summary>
public sealed record DebtDashboardView(
    double Technical,
    string TechnicalBand,
    double Agentic,
    string AgenticBand,
    IReadOnlyList<DebtSignalView> TechnicalSignals,
    IReadOnlyList<DebtSignalView> AgenticSignals,
    DateTimeOffset? ComputedAt)
{
    public static readonly DebtDashboardView Empty =
        new(0, nameof(DebtBand.Low), 0, nameof(DebtBand.Low),
            Array.Empty<DebtSignalView>(), Array.Empty<DebtSignalView>(), null);
}

using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure pricing policy: converts token usage to a USD cost using a per-model rate table. Models are
/// matched by family substring (opus / sonnet / haiku / …) so specific version ids resolve to a family.
/// Rates are defaults and can be overridden via the constructor; pricing is data, not behavior.
/// </summary>
public sealed class TokenCostCalculator
{
    // Published Claude list prices per 1M tokens (USD). Override via the constructor as prices change.
    private static readonly IReadOnlyList<(string Family, ModelRate Rate)> DefaultRates = new[]
    {
        ("opus", new ModelRate(15m, 75m)),
        ("sonnet", new ModelRate(3m, 15m)),
        ("haiku", new ModelRate(1m, 5m)),
        ("fable", new ModelRate(5m, 25m)),
    };

    private readonly IReadOnlyList<(string Family, ModelRate Rate)> _rates;
    private readonly ModelRate _fallback;

    public TokenCostCalculator(
        IReadOnlyList<(string Family, ModelRate Rate)>? rates = null, ModelRate? fallback = null)
    {
        _rates = rates ?? DefaultRates;
        _fallback = fallback ?? new ModelRate(3m, 15m); // sonnet-like default for unknown models
    }

    public ModelRate RateFor(string model)
    {
        var m = (model ?? string.Empty).ToLowerInvariant();
        foreach (var (family, rate) in _rates)
            if (m.Contains(family, StringComparison.Ordinal))
                return rate;
        return _fallback;
    }

    /// <summary>USD cost of the given usage at the matched model's rate.</summary>
    public decimal CostUsd(TokenUsage usage)
    {
        var rate = RateFor(usage.Model);
        return usage.InputTokens / 1_000_000m * rate.InputPerMillion
             + usage.OutputTokens / 1_000_000m * rate.OutputPerMillion;
    }
}

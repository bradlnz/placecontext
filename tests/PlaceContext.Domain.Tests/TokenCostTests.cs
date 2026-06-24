using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class TokenCostTests
{
    private readonly TokenCostCalculator _calc = new();

    [Theory]
    [InlineData("claude-opus-4-8", 1_000_000, 1_000_000, 90.0)]   // 15 + 75
    [InlineData("claude-sonnet-4-6", 1_000_000, 1_000_000, 18.0)] // 3 + 15
    [InlineData("claude-haiku-4-5", 1_000_000, 1_000_000, 6.0)]   // 1 + 5
    public void Cost_uses_the_model_family_rate(string model, long input, long output, double expected)
    {
        var cost = _calc.CostUsd(TokenUsage.From(model, input, output));
        Assert.Equal((decimal)expected, cost);
    }

    [Fact]
    public void Unknown_model_falls_back_to_default_rate()
    {
        // 500k in + 500k out at the sonnet-like fallback (3/15) = 1.5 + 7.5
        var cost = _calc.CostUsd(TokenUsage.From("some-other-llm", 500_000, 500_000));
        Assert.Equal(9.0m, cost);
    }

    [Fact]
    public void Token_counts_must_be_non_negative()
        => Assert.Throws<ArgumentException>(() => TokenUsage.From("opus", -1, 0));
}

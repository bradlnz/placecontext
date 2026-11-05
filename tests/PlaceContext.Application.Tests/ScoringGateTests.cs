using System.Text.Json.Nodes;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Tests;

public class ScoringGateTests
{
    public static ScoreCriterion Criterion(string id, double weight = 1, double minimum = 80)
        => new(id, "Does current evidence demonstrate " + id + "?", ["Absent or failing evidence", "Partial evidence", "Complete passing evidence"], weight, minimum);

    public static JsonObject Answer(double score, double confidence = 0.95) => new()
    {
        ["type"] = "score", ["score"] = score, ["confidence"] = confidence,
        ["probabilities"] = new JsonObject { ["0"] = 1 - score / 2, ["1"] = 0d, ["2"] = score / 2 }
    };
    public static JsonObject Response(params (string Id, double Score, double Confidence)[] scores)
    {
        var answers = new JsonObject();
        foreach (var (id, score, confidence) in scores) answers[id] = Answer(score, confidence);
        return new JsonObject { ["model"] = "test-scorer", ["answers"] = answers };
    }

    [Theory]
    [InlineData(2, 0.95, true)]
    [InlineData(1.5, 0.95, false)]
    [InlineData(2, 0.79, false)]
    public void Required_floor_and_confidence_block_high_averages(double security, double confidence, bool passed)
    {
        var gate = new ScoringGateChainAction("review", "gate", [Criterion("features", 95), Criterion("security", 5)]);
        var result = ScoringGateEvaluator.Evaluate(gate, Response(("features", 2, .95), ("security", security, confidence)));
        Assert.True(result["score"]!.GetValue<double>() >= 90);
        Assert.Equal(passed, result["passed"]!.GetValue<bool>());
        Assert.Equal(!passed, ((JsonArray)result["blockers"]!).Count > 0);
    }

    [Theory]
    [InlineData("score", "3")]
    [InlineData("score", "\"2\"")]
    [InlineData("confidence", "null")]
    [InlineData("confidence", "1.1")]
    [InlineData("probabilities", "{}")]
    [InlineData("probabilities", "{\"0\":0,\"1\":0,\"2\":0.1}")]
    public void Invalid_provider_values_fail_closed(string property, string value)
    {
        var gate = new ScoringGateChainAction("review", "gate", [Criterion("quality")]);
        var response = Response(("quality", 2, .95));
        response["answers"]!["quality"]![property] = JsonNode.Parse(value);
        Assert.Throws<InvalidOperationException>(() => ScoringGateEvaluator.Evaluate(gate, response));
    }

    [Fact]
    public void Missing_criterion_and_invalid_configuration_fail_closed()
    {
        var criterion = Criterion("security");
        Assert.Throws<ArgumentException>(() => new ScoringGateChainAction("review", "gate", [criterion, criterion]));
        Assert.Throws<ArgumentException>(() => new ScoringGateChainAction("gate.evidence", "gate", [criterion]));
        Assert.Throws<ArgumentException>(() => new ScoringGateChainAction("review", "gate", [criterion], double.NaN));
        Assert.Throws<ArgumentException>(() => Criterion("security", 0));
        Assert.Throws<InvalidOperationException>(() => ScoringGateEvaluator.Evaluate(new("review", "gate", [criterion]), Response()));
    }

    [Fact]
    public void Weighted_average_is_computed_in_code_without_rounding_a_failure_up()
    {
        var gate = new ScoringGateChainAction("review", "gate", [Criterion("a", 3, 0), Criterion("b", 1, 0)], 90);
        var result = ScoringGateEvaluator.Evaluate(gate, Response(("a", 2, .95), ("b", 1.199, .95)));
        Assert.Equal(89.9875, result["score"]!.GetValue<double>(), 4);
        Assert.False(result["passed"]!.GetValue<bool>());
    }
}

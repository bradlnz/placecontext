using System.Text.Json.Nodes;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public static class ScoringGateEvaluator
{
    public static JsonObject Evaluate(ScoringGateChainAction gate, JsonObject response)
    {
        if (response["model"] is not JsonValue model || !model.TryGetValue<string>(out var modelName) || string.IsNullOrWhiteSpace(modelName)
            || response["answers"] is not JsonObject answers)
            throw new InvalidOperationException("Scoring service returned an invalid response.");
        var breakdown = new JsonObject();
        var blockers = new JsonArray();
        double weighted = 0, weight = 0, confidence = 1;
        foreach (var criterion in gate.Criteria)
        {
            if (answers[criterion.Id] is not JsonObject answer || answer["type"]?.ToString() != "score")
                throw new InvalidOperationException($"Missing score for {criterion.Id}.");
            var raw = Number(answer["score"], criterion.Levels.Count - 1);
            var certainty = Number(answer["confidence"], 1);
            if (answer["probabilities"] is not JsonObject probabilities || probabilities.Count != criterion.Levels.Count)
                throw new InvalidOperationException($"Missing score probabilities for {criterion.Id}.");
            double sum = 0, expected = 0;
            for (var i = 0; i < criterion.Levels.Count; i++)
            {
                var probability = Number(probabilities[i.ToString(System.Globalization.CultureInfo.InvariantCulture)], 1);
                sum += probability; expected += i * probability;
            }
            if (Math.Abs(sum - 1) > 0.02 || Math.Abs(expected - raw) > 0.02 * criterion.Levels.Count)
                throw new InvalidOperationException($"Inconsistent score probabilities for {criterion.Id}.");
            var score = 100 * raw / (criterion.Levels.Count - 1);
            var passed = score >= criterion.MinimumScore && certainty >= gate.MinimumConfidence;
            if (score < criterion.MinimumScore) blockers.Add($"{criterion.Id}: score {score:F1} below required {criterion.MinimumScore:F1}.");
            if (certainty < gate.MinimumConfidence) blockers.Add($"{criterion.Id}: confidence {certainty:F2} below required {gate.MinimumConfidence:F2}.");
            breakdown[criterion.Id] = new JsonObject
            {
                ["score"] = score, ["confidence"] = certainty, ["weight"] = criterion.Weight,
                ["minimum_score"] = criterion.MinimumScore, ["passed"] = passed, ["question"] = criterion.Question,
                ["levels"] = new JsonArray(criterion.Levels.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["probabilities"] = probabilities.DeepClone()
            };
            weighted += score * criterion.Weight; weight += criterion.Weight; confidence = Math.Min(confidence, certainty);
        }
        var total = weighted / weight;
        if (total < gate.PassScore) blockers.Add($"Weighted score {total:F1} below required {gate.PassScore:F1}.");
        return new JsonObject
        {
            ["passed"] = blockers.Count == 0, ["score"] = total, ["confidence"] = confidence,
            ["pass_score"] = gate.PassScore, ["minimum_confidence"] = gate.MinimumConfidence,
            ["model"] = modelName, ["input_field"] = gate.InputField, ["criteria"] = breakdown, ["blockers"] = blockers
        };
    }

    private static double Number(JsonNode? node, double maximum)
    {
        if (node is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number) || number < 0 || number > maximum)
            throw new InvalidOperationException("Scoring service returned an invalid number.");
        return number;
    }
}

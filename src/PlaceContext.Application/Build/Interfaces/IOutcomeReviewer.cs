using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public interface IOutcomeReviewer
{
    Task<JsonObject> ScoreAsync(IReadOnlyList<PlaceContext.Domain.ValueObjects.ScoreCriterion> criteria, string state, CancellationToken ct, Guid? projectId = null);
    Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null);
}

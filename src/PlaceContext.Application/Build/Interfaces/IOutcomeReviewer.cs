using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public interface IOutcomeReviewer
{
    Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null);
}

using System.Text.Json.Nodes;

namespace PlaceContext.Application.Ports;

public interface IJevReviewer
{
    Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct);
}

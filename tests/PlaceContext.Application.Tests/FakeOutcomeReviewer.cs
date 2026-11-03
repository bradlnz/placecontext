using System.Text.Json.Nodes;
using PlaceContext.Application.Build;
namespace PlaceContext.Application.Tests;

public sealed class FakeOutcomeReviewer(double probability = 0.95, bool fail = false) : IOutcomeReviewer
{
    public List<(string Question, string? State)> Requests { get; } = [];
    public Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
    {
        ct.ThrowIfCancellationRequested();
        Requests.Add((question, state));
        if (fail)
        {
            throw new InvalidOperationException("Jev unavailable");
        }
        return Task.FromResult(new JsonObject
        {
            ["type"] = "noul",
            ["noul"] = probability,
            ["question"] = question,
            ["model"] = "jev-test"
        });
    }
}

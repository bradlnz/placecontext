using System.Text.Json.Nodes;
using PlaceContext.Application.Build;
namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

internal sealed class AgentTestReviewer : IOutcomeReviewer
{
    public double Score { get; set; } = 0.95;
    public bool Fail
    {
        get; set;
    }
    public Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null) => Fail
        ? throw new InvalidOperationException("Private upstream error must not leak.")
        : Task.FromResult(new JsonObject { ["noul"] = Score });
}

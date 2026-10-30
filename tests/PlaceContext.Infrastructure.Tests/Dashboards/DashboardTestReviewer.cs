using System.Text.Json.Nodes;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

internal sealed class DashboardTestReviewer : IJevReviewer
{
    public Guid? ProjectId { get; private set; }
    public string? Evidence { get; private set; }
    public int Calls { get; private set; }
    public double Score { get; set; } = .95;
    public bool Fail { get; set; }
    public Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
    {
        Calls++; Evidence = state; ProjectId = projectId;
        if (Fail) throw new InvalidOperationException("private provider details");
        return Task.FromResult(new JsonObject { ["noul"] = Score });
    }
}

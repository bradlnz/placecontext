using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

internal sealed class DashboardTestReviewer : IJevReviewer
{
    public Guid? ProjectId { get; private set; }
    public string? Evidence { get; private set; }
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public ConcurrentQueue<(string Question, string? Evidence, Guid? ProjectId)> Requests { get; } = new();
    public double Score { get; set; } = .95;
    public bool Fail { get; set; }
    public Func<string, string?, CancellationToken, Guid?, Task<JsonObject>>? Handler { get; set; }
    public Task<JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
    {
        Interlocked.Increment(ref _calls); Evidence = state; ProjectId = projectId;
        Requests.Enqueue((question, state, projectId));
        if (Fail) throw new InvalidOperationException("private provider details");
        if (Handler is not null) return Handler(question, state, ct, projectId);
        return Task.FromResult(new JsonObject { ["noul"] = Score, ["model"] = "jev-test" });
    }
}

using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeCodeMetricsProbe : ICodeMetricsProbe
{
    public CodeMetrics Metrics { get; set; } = CodeMetrics.From(0, 0, 0, 0);
    public Task<CodeMetrics> ProbeAsync(ProjectPath path, CancellationToken ct = default) => Task.FromResult(Metrics);
}

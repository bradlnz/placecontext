using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Probes the working tree for code-level technical-debt metrics.</summary>
public interface ICodeMetricsProbe
{
    Task<CodeMetrics> ProbeAsync(RepoPath path, CancellationToken ct = default);
}

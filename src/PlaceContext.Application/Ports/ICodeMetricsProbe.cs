using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Probes the working tree for code-level technical-risk metrics.</summary>
public interface ICodeMetricsProbe
{
    Task<CodeMetrics> ProbeAsync(ProjectPath path, CancellationToken ct = default);
}

using Microsoft.Extensions.Hosting;

namespace PlaceContext.Jobs.Infrastructure.Observability;

/// <summary>
/// No-op hosted service whose only job is to force DI to construct the singleton
/// <see cref="JobTelemetryCollector"/> at Host startup — otherwise a singleton isn't built until
/// something first injects it, which could be after the very first job run's telemetry has already
/// fired and been missed.
/// </summary>
public sealed class JobTelemetryCollectorStartup : IHostedService
{
    // Constructor injection alone triggers JobTelemetryCollector's construction (and listener wiring).
    public JobTelemetryCollectorStartup(JobTelemetryCollector collector) { }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

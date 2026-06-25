using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlaceContext.Infrastructure.Activation;

/// <summary>
/// Background driver for online activation: refreshes the <see cref="RemoteActivationService"/> once at
/// startup and then on its <see cref="RemoteActivationService.RefreshInterval"/>, so entitlement state and
/// rotated signing keys stay current without blocking request handling. Registered only when a licensing
/// server URL is configured.
/// </summary>
public sealed class ActivationRefreshService : BackgroundService
{
    private readonly RemoteActivationService _activation;
    private readonly ILogger<ActivationRefreshService> _log;

    public ActivationRefreshService(RemoteActivationService activation, ILogger<ActivationRefreshService> log)
    {
        _activation = activation;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _activation.RefreshAsync(stoppingToken);
                var info = _activation.Current;
                _log.LogInformation("Activation refreshed: {Status} — {Reason}", info.Status, info.Reason);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Activation refresh failed unexpectedly; keeping the last known state.");
            }

            try
            {
                await Task.Delay(_activation.RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

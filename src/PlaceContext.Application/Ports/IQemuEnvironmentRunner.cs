using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

public interface IQemuEnvironmentRunner
{
    Task<QemuEnvironmentResult> RunAsync(QemuChainAction action, string? input, CancellationToken ct,
        Func<QemuReplayCapture, CancellationToken, Task>? recordFrame = null);
}

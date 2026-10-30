namespace PlaceContext.Application.Ports;

public interface IQemuTaskAgent
{
    Task<IQemuTaskAgent> ForProjectAsync(Guid? projectId, CancellationToken ct) => Task.FromResult(this);
    string? ConfigurationError { get; }
    string Model { get; }
    Task<QemuEnvironmentResult> RunAsync(string prompt, string? input,
        Func<string, int, CancellationToken, Task<QemuEnvironmentResult>> executeGuest, CancellationToken ct,
        Func<QemuReplayCapture, CancellationToken, Task>? recordFrame = null, QemuAgentInteraction? interaction = null, Func<CancellationToken, Task<byte[]>>? readLiveFrame = null);
}

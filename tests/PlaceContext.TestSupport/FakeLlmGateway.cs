using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>Test double for <see cref="ILlmGateway"/>. Disabled by default (mirrors no API key);
/// when enabled it wraps the input so tests can assert the polish pass ran.</summary>
public sealed class FakeLlmGateway : ILlmGateway
{
    public FakeLlmGateway(bool enabled = false) => IsEnabled = enabled;

    public bool IsEnabled { get; }

    public Task<string> GenerateAsync(string system, string user, CancellationToken ct = default)
        => Task.FromResult("POLISHED:\n" + user);
}

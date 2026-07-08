using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>Test double for <see cref="ILlmGateway"/>. Disabled by default (mirrors no API key);
/// when enabled it wraps the input so tests can assert the polish pass ran — or returns
/// <see cref="Reply"/> verbatim when a test scripts the model's answer.</summary>
public sealed class FakeLlmGateway : ILlmGateway
{
    public FakeLlmGateway(bool enabled = false) => IsEnabled = enabled;

    public bool IsEnabled { get; }

    /// <summary>When set, returned verbatim from <see cref="GenerateAsync"/>.</summary>
    public string? Reply { get; set; }

    public Task<string> GenerateAsync(string system, string user, CancellationToken ct = default)
        => Task.FromResult(Reply ?? "POLISHED:\n" + user);
}

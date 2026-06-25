using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Llm;

/// <summary>
/// The default <see cref="ILlmGateway"/> when no generation backend is configured. Reports its
/// disabled, so the report generator returns its deterministically-assembled Markdown unchanged.
/// </summary>
public sealed class NullLlmGateway : ILlmGateway
{
    public bool IsEnabled => false;

    public Task<string> GenerateAsync(string system, string user, CancellationToken ct = default)
        => Task.FromResult(user);
}

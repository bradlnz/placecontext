using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>
/// Validates the deployment's self-host activation key and reports its state. Implemented in
/// Infrastructure (offline signature verification against a configured public key) so the licensing
/// crypto never leaks into Domain/Application. The result is shown in the portal and may gate access.
/// </summary>
public interface IActivationService
{
    /// <summary>The current activation state (validates the configured key; result is cached).</summary>
    ActivationInfo Current { get; }

    /// <summary>
    /// Whether activation is enforced. When true and <see cref="Current"/> is not active, the host blocks
    /// the portal/MCP (except the activation status surface). When false, an inactive deployment only warns.
    /// </summary>
    bool Enforced { get; }
}

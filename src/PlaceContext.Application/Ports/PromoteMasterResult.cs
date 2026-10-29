namespace PlaceContext.Application.Ports;

/// <summary>Outcome of promoting a node to fleet master.</summary>
public sealed record PromoteMasterResult(
    string NodeName,
    bool Succeeded,
    string Message,
    /// <summary>
    /// When the target is only a worker today, the operator must reinstall k3s as a server on that
    /// machine (agent → server). This is the exact command / steps to run on that node over Tailscale.
    /// </summary>
    string? HostActionRequired = null);

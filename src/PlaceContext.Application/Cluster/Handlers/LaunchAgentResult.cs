using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Cluster;

/// <summary>Outcome of minting a Tailscale key and building an agent join code around it.</summary>
public sealed record LaunchAgentResult(
    bool Minted,
    string? JoinCode,
    string? ServerUrl,
    string ConnectCommand,
    string Message);

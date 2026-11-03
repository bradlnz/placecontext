using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record NetworkFlowResult(Guid NetworkId, Guid GroupId, string NetworkName, string GroupName,
    long Version, string Direction, string Protocol, string Address, int Port, IReadOnlyList<NetworkRule> Matches,
    string Status, string Message, double? ElapsedMs, DateTimeOffset CheckedAt)
{
    public bool Allowed => Matches.Count > 0;
    public string? SourceAddress
    {
        get; init;
    }
}

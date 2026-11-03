using System.Buffers.Binary;
using System.Net.Sockets;
using System.Net;

namespace PlaceContext.Domain.ValueObjects;

public sealed record NetworkSecurityGroup(Guid Id, string Name, IReadOnlyList<NetworkRule> Rules);

using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;

namespace PlaceContext.Domain.ValueObjects;

public sealed record NetworkSecurityGroup(Guid Id, string Name, IReadOnlyList<NetworkRule> Rules);

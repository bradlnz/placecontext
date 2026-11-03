using System.Buffers.Binary;
using System.Net.Sockets;
using System.Net;

namespace PlaceContext.Domain.ValueObjects;

public sealed record WorkerNetworkPolicy(Guid TenantId, Guid NetworkId, string Cidr, NetworkSecurityGroup Group)
{
    public string ResourceName => $"pc-net-{NetworkId:N}";

    public static IPNetwork ParseCidr(string cidr)
    {
        if (!IPNetwork.TryParse(cidr, out var network) || network.BaseAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Enter an IPv4 network CIDR with no host bits, for example 10.20.0.0/24.");
        }
        var address = BinaryPrimitives.ReadUInt32BigEndian(network.BaseAddress.GetAddressBytes());
        var mask = network.PrefixLength == 0 ? 0 : uint.MaxValue << (32 - network.PrefixLength);
        if ((address & mask) != address || cidr != network.ToString())
        {
            throw new ArgumentException("Enter a canonical IPv4 network CIDR with no host bits, for example 10.20.0.0/24.");
        }
        return network;
    }

    public static void ValidateNetwork(string name, string cidr, IReadOnlyList<NetworkSecurityGroup> groups)
    {
        ValidateName(name);
        var network = ParseCidr(cidr);
        if (network.PrefixLength > 29 || !new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" }
            .Select(IPNetwork.Parse).Any(range => range.PrefixLength <= network.PrefixLength && range.Contains(network.BaseAddress)))
        {
            throw new ArgumentException("Use a private IPv4 CIDR within 10/8, 172.16/12 or 192.168/16, with prefix /29 or larger.");
        }
        if (groups.Count is < 1 or > 20 || groups.Any(group => group.Id == Guid.Empty) || groups.Select(group => group.Id).Distinct().Count() != groups.Count)
        {
            throw new ArgumentException("A VPC needs 1–20 uniquely identified security groups.");
        }
        foreach (var group in groups)
        {
            ValidateName(group.Name);
            if (group.Rules.Count > 100)
            {
                throw new ArgumentException("A security group supports at most 100 rules.");
            }
            foreach (var rule in group.Rules)
            {
                rule.Validate();
            }
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
        {
            throw new ArgumentException("Names must contain 1–80 characters without control characters.");
        }
    }
}

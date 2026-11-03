using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public sealed class WorkerNetworkPolicyTests
{
    [Theory]
    [InlineData("10.2.0.0/24")]
    [InlineData("172.16.0.0/12")]
    [InlineData("192.168.8.0/29")]
    public void Private_networks_accept_empty_deny_all_groups(string cidr) =>
        WorkerNetworkPolicy.ValidateNetwork("Workers", cidr, [new(Guid.NewGuid(), "Isolated", [])]);

    [Theory]
    [InlineData("10.2.0.1/24")]
    [InlineData("0.0.0.0/0")]
    [InlineData("192.168.0.0/8")]
    [InlineData("127.0.0.0/24")]
    [InlineData("10.0.0.0/32")]
    [InlineData("2001:db8::/64")]
    public void Invalid_or_public_network_ranges_are_rejected(string cidr) => Assert.Throws<ArgumentException>(() =>
        WorkerNetworkPolicy.ValidateNetwork("Workers", cidr, [new(Guid.NewGuid(), "Isolated", [])]));

    [Theory]
    [InlineData("egress", "TCP", "0.0.0.0/0", 443, 443, true)]
    [InlineData("ingress", "UDP", "10.0.0.0/8", 1024, 65535, true)]
    [InlineData("out", "TCP", "10.0.0.0/8", 80, 80, false)]
    [InlineData("egress", "ICMP", "10.0.0.0/8", 80, 80, false)]
    [InlineData("egress", "TCP", "10.0.0.0/8", 0, 80, false)]
    [InlineData("egress", "TCP", "10.0.0.0/8", 443, 80, false)]
    [InlineData("egress", "TCP", "10.0.0.0/8", 1, 65536, false)]
    public void Rules_validate_direction_protocol_and_port_ranges(string direction, string protocol, string cidr, int start, int end, bool valid)
    {
        var rule = new NetworkRule(direction, protocol, cidr, start, end);
        if (valid)
        {
            rule.Validate();
            return;
        }
        Assert.Throws<ArgumentException>(rule.Validate);
    }
}

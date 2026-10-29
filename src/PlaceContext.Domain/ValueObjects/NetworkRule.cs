using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;

namespace PlaceContext.Domain.ValueObjects;

public sealed record NetworkRule(string Direction, string Protocol, string Cidr, int FromPort, int ToPort)
{
    public void Validate()
    {
        if (Direction is not ("ingress" or "egress")) throw new ArgumentException("Choose inbound or outbound traffic.");
        if (Protocol is not ("TCP" or "UDP")) throw new ArgumentException("Choose TCP or UDP.");
        WorkerNetworkPolicy.ParseCidr(Cidr);
        if (FromPort < 1 || ToPort > 65535 || ToPort < FromPort)
            throw new ArgumentException("Ports must be between 1 and 65535, with the last port at least the first.");
    }
}

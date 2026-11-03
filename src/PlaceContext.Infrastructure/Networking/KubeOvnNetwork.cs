using System.Net;
using System.Text.Json;
using PlaceContext.Domain.ValueObjects;
using k8s.Autorest;
using k8s.Models;
using k8s;

namespace PlaceContext.Infrastructure.Networking;

internal static class KubeOvnNetwork
{
    public static async Task EnsureAsync(IKubernetes client, WorkerNetworkPolicy network, string ns, CancellationToken ct)
    {
        WorkerNetworkPolicy.ValidateNetwork("VPC", network.Cidr, [network.Group]);
        var labels = new Dictionary<string, string> { ["placecontext.io/tenant"] = network.TenantId.ToString("N") };
        var vpc = new
        {
            apiVersion = "kubeovn.io/v1",
            kind = "Vpc",
            metadata = new
            {
                name = network.ResourceName,
                labels
            },
            spec = new
            {
                namespaces = new[] { ns }
            }
        };
        var subnet = new
        {
            apiVersion = "kubeovn.io/v1",
            kind = "Subnet",
            metadata = new
            {
                name = network.ResourceName,
                labels
            },
            spec = new
            {
                vpc = network.ResourceName,
                cidrBlock = network.Cidr,
                protocol = "IPv4",
                disableGatewayCheck = true
            }
        };
        await EnsureResourceAsync(client, "vpcs", network, vpc, ct);
        await EnsureResourceAsync(client, "subnets", network, subnet, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                var resource = JsonSerializer.SerializeToElement(await client.CustomObjects.GetClusterCustomObjectAsync(
                    "kubeovn.io", "v1", "subnets", network.ResourceName, cancellationToken: deadline.Token));
                if (resource.TryGetProperty("status", out var status) && status.TryGetProperty("conditions", out var conditions)
                    && conditions.EnumerateArray().Any(condition => condition.GetProperty("type").GetString() == "Ready"
                        && condition.GetProperty("status").GetString() == "True"))
                {
                    return;
                }
                await Task.Delay(500, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("The worker VPC is not ready. Check the Kube-OVN subnet status before retrying this job.");
        }
    }

    private static async Task EnsureResourceAsync(IKubernetes client, string plural, WorkerNetworkPolicy network, object body, CancellationToken ct)
    {
        try
        {
            await client.CustomObjects.CreateClusterCustomObjectAsync(body, "kubeovn.io", "v1", plural, cancellationToken: ct);
        }
        catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.Conflict) { }
        var actual = JsonSerializer.SerializeToElement(await client.CustomObjects.GetClusterCustomObjectAsync(
            "kubeovn.io", "v1", plural, network.ResourceName, cancellationToken: ct));
        var desired = JsonSerializer.SerializeToElement(body);
        if (!actual.GetProperty("metadata").TryGetProperty("labels", out var labels)
            || !labels.TryGetProperty("placecontext.io/tenant", out var owner) || owner.GetString() != network.TenantId.ToString("N"))
        {
            throw new InvalidOperationException("The existing network resource belongs to another workspace.");
        }
        foreach (var property in desired.GetProperty("spec").EnumerateObject())
        {
            if (!actual.GetProperty("spec").TryGetProperty(property.Name, out var value) || !JsonElement.DeepEquals(value, property.Value))
            {
                throw new InvalidOperationException("The existing network resource differs from the saved VPC. Ask an administrator to reconcile it.");
            }
        }
    }

    public static Dictionary<string, string> Annotations(WorkerNetworkPolicy network) => new()
    {
        ["ovn.kubernetes.io/logical_switch"] = network.ResourceName,
        ["ovn.kubernetes.io/port_security"] = "true",
    };

    public static V1NetworkPolicy Policy(string name, WorkerNetworkPolicy network)
    {
        WorkerNetworkPolicy.ValidateNetwork("VPC", network.Cidr, [network.Group]);
        return new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta { Name = name },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["placecontext-run"] = name } },
                PolicyTypes = ["Ingress", "Egress"],
                Ingress = network.Group.Rules.Where(rule => rule.Direction == "ingress").Select(rule => new V1NetworkPolicyIngressRule
                {
                    FromProperty = [new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = rule.Cidr } }],
                    Ports = [Port(rule)],
                }).ToList(),
                Egress = network.Group.Rules.Where(rule => rule.Direction == "egress").Select(rule => new V1NetworkPolicyEgressRule
                {
                    To = [new V1NetworkPolicyPeer { IpBlock = new V1IPBlock { Cidr = rule.Cidr } }],
                    Ports = [Port(rule)],
                }).ToList(),
            },
        };
    }

    private static V1NetworkPolicyPort Port(NetworkRule rule) => new()
    {
        Protocol = rule.Protocol,
        Port = rule.FromPort,
        EndPort = rule.ToPort == rule.FromPort ? null : rule.ToPort,
    };
}

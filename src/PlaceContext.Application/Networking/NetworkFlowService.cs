using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using PlaceContext.Application.Access;
using PlaceContext.Application.Networking;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class NetworkFlowService(IWorkerNetworkService networks, IPermissionService permissions,
    ICurrentTenant tenant, IWorkloadRunner runner)
{
    public async Task<NetworkFlowResult> CheckAsync(Guid projectId, Guid networkId, Guid groupId,
        string direction, string protocol, string address, int port, bool live, CancellationToken ct = default)
    {
        if (!tenant.IsResolved || !await permissions.HasAsync(Permission.SettingsManage, ct))
        {
            throw new UnauthorizedAccessException("Network administration permission is required.");
        }
        var ip = ValidateAddress(address);
        if (direction is not ("ingress" or "egress") || protocol is not ("TCP" or "UDP") || port is < 1 or > 65535)
        {
            throw new ArgumentException("Choose a direction, TCP or UDP, and a port between 1 and 65535.");
        }
        if (live && (direction != "egress" || protocol != "TCP"))
        {
            throw new ArgumentException("Live tests support outbound TCP connections. Use Check rules for inbound or UDP traffic.");
        }
        var state = await networks.LoadAsync(projectId, ct);
        var network = state.Networks.SingleOrDefault(item => item.Id == networkId)
            ?? throw new ArgumentException("Choose a VPC in this project.");
        var group = network.Groups.SingleOrDefault(item => item.Id == groupId)
            ?? throw new ArgumentException("Choose a security group in this VPC.");
        var matches = group.Rules.Where(rule => rule.Direction == direction && rule.Protocol == protocol
            && port >= rule.FromPort && port <= rule.ToPort && WorkerNetworkPolicy.ParseCidr(rule.Cidr).Contains(ip)).ToArray();
        var result = new NetworkFlowResult(network.Id, group.Id, network.Name, group.Name, network.Version,
            direction, protocol, address, port, matches, "Rules checked",
            matches.Length > 0 ? "A saved rule allows this flow. Routing and destination reachability have not been tested."
                : "No saved rule allows this flow. New connections should be blocked.", null, DateTimeOffset.UtcNow);
        if (!live)
        {
            return result;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        var request = new WorkloadRunRequest(null, JsonSerializer.Serialize(new
        {
            address,
            port
        }),
            new Dictionary<string, string>(), [], "netprobe-" + Guid.NewGuid().ToString("N"),
            [("probe.py", ProbeScript)], "python", "probe.py", AllowNetworkEgress: false, TimeoutSeconds: 90,
            Network: new(tenant.TenantId, network.Id, network.Cidr, group));
        try
        {
            var run = await runner.RunAsync(request, deadline.Token);
            if (run.ExitCode != 0)
            {
                return result with
                {
                    Status = "Test could not run",
                    Message = Limit(string.IsNullOrWhiteSpace(run.Stderr) ? run.Stdout : run.Stderr)
                };
            }
            using var output = JsonDocument.Parse(run.Artifact ?? run.Stdout);
            var root = output.RootElement;
            var status = root.GetProperty("status").GetString();
            var message = status switch
            {
                "Connected" when matches.Length == 0 => "Unexpected connection: no saved rule allows this target. Check worker enforcement and other policies selecting the probe pod.",
                "Connected" => "The worker established a TCP connection. Application responses and TLS were not tested.",
                "Refused" => "The destination or an intermediate device refused the connection. This does not prove the security group blocked it.",
                "Timed out" => "No TCP response within 8 seconds. Policy, routing, or an unavailable destination may be responsible.",
                "Unreachable" => "The worker could not reach the destination. Check its route, gateway, and destination availability.",
                _ => throw new InvalidOperationException("The worker returned an invalid probe result."),
            };
            return result with
            {
                Status = status!,
                Message = message,
                ElapsedMs = root.GetProperty("elapsedMs").GetDouble(),
                SourceAddress = root.TryGetProperty("sourceAddress", out var source) ? source.GetString() : null,
                CheckedAt = DateTimeOffset.UtcNow
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return result with
            {
                Status = "Test could not run",
                Message = "The worker test exceeded its two-minute startup and execution limit."
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return result with
            {
                Status = "Test could not run",
                Message = Limit(ex.Message)
            };
        }
    }

    public static IPAddress ValidateAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || ip.ToString() != address
            || IPAddress.IsLoopback(ip) || ip.GetAddressBytes()[0] is 0 or >= 224
            || (ip.GetAddressBytes()[0] == 169 && ip.GetAddressBytes()[1] == 254))
        {
            throw new ArgumentException("Enter a unicast IPv4 address, such as 10.20.0.10. Hostnames, loopback, and link-local addresses are not supported.");
        }
        return ip;
    }

    private static string Limit(string value) => string.IsNullOrWhiteSpace(value) ? "The worker returned no diagnostic output." : value[..Math.Min(value.Length, 1200)];

    // TCP connect only: no shell interpolation, payload, credentials, DNS lookup, or HTTP redirect.
    public const string ProbeScript = """
        import json, socket, sys, time, errno
        target = json.load(sys.stdin)
        started = time.monotonic()
        status = "Unreachable"
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as connection:
            connection.settimeout(8)
            try:
                connection.connect((target["address"], target["port"]))
                status = "Connected"
            except ConnectionRefusedError:
                status = "Refused"
            except TimeoutError:
                status = "Timed out"
            except OSError as error:
                if error.errno == errno.ETIMEDOUT:
                    status = "Timed out"
            source = connection.getsockname()[0]
        print(json.dumps({"status": status, "sourceAddress": source, "elapsedMs": round((time.monotonic() - started) * 1000, 1)}))
        """;
}

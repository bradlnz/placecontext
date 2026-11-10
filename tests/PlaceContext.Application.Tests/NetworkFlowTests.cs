using System.Text.Json;
using PlaceContext.Application.Features;
using PlaceContext.Application.Networking;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using PlaceContext.Application.Access;

namespace PlaceContext.Application.Tests;

public sealed class NetworkFlowTests
{
    private sealed class Permissions(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }

    private static (NetworkFlowService Service, FakeWorkloadRunner Runner, WorkerNetwork Network) Setup(bool permitted = true, IWorkloadRunner? overrideRunner = null)
    {
        var networks = new FakeWorkerNetworkService();
        var group = new NetworkSecurityGroup(Guid.NewGuid(), "Web", [new("egress", "TCP", "10.20.0.0/24", 443, 445), new("ingress", "UDP", "10.30.0.0/24", 53, 53)]);
        var network = new WorkerNetwork(Guid.NewGuid(), Guid.NewGuid(), "Private", "10.20.0.0/24", [group], 3);
        networks.Networks.Add(network);
        var runner = new FakeWorkloadRunner();
        return (new(networks, new Permissions(permitted), new FakeCurrentTenant(Guid.NewGuid()), overrideRunner ?? runner), runner, network);
    }

    private sealed class WaitingRunner : IWorkloadRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Expected cancellation");
        }
        public Task CancelAsync(string correlationId, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Cancellation_reaches_the_worker_and_is_not_reported_as_a_network_failure()
    {
        var worker = new WaitingRunner();
        var (service, _, network) = Setup(overrideRunner: worker);
        using var cancel = new CancellationTokenSource();
        var test = service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, "egress", "TCP", "10.20.0.10", 443, true, cancel.Token);
        await worker.Started.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
    }

    [Theory]
    [InlineData("egress", "TCP", "10.20.0.10", 443, true)]
    [InlineData("egress", "TCP", "10.20.0.255", 445, true)]
    [InlineData("egress", "TCP", "10.20.1.10", 443, false)]
    [InlineData("egress", "TCP", "10.20.0.10", 446, false)]
    [InlineData("egress", "UDP", "10.20.0.10", 443, false)]
    [InlineData("ingress", "TCP", "10.20.0.10", 443, false)]
    [InlineData("ingress", "UDP", "10.30.0.10", 53, true)]
    public async Task Rule_checks_match_direction_protocol_CIDR_and_port_without_launching_a_worker(string direction, string protocol, string address, int port, bool allowed)
    {
        var (service, runner, network) = Setup();
        var result = await service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, direction, protocol, address, port, false);
        Assert.Equal(allowed, result.Allowed);
        Assert.Equal("Rules checked", result.Status);
        Assert.Equal(3, result.Version);
        Assert.Empty(runner.ReceivedRequests);
    }

    [Theory]
    [InlineData("Connected")]
    [InlineData("Refused")]
    [InlineData("Timed out")]
    [InlineData("Unreachable")]
    public async Task Live_test_uses_the_saved_policy_and_reports_the_observation(string status)
    {
        var (service, runner, network) = Setup();
        runner.EnqueueResult(new(0, JsonSerializer.Serialize(new
        {
            status,
            sourceAddress = "10.20.0.2",
            elapsedMs = 12.3
        }), "", ""));
        var result = await service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, "egress", "TCP", "10.20.0.10", 443, true);
        Assert.Equal(status, result.Status);
        Assert.Equal(12.3, result.ElapsedMs);
        Assert.Equal("10.20.0.2", result.SourceAddress);
        var request = Assert.Single(runner.ReceivedRequests);
        Assert.Equal(network.Id, request.Network!.NetworkId);
        Assert.Equal(network.Groups[0], request.Network.Group);
        Assert.False(request.AllowNetworkEgress);
        Assert.Equal("python", request.RuntimeId);
        Assert.DoesNotContain("10.20.0.10", Assert.Single(request.CodeFiles!).Content);
        Assert.Equal(443, JsonDocument.Parse(request.StdinPayload).RootElement.GetProperty("port").GetInt32());
        Assert.Equal(90, request.TimeoutSeconds);
    }

    [Fact]
    public async Task Unexpected_connectivity_is_not_reported_as_policy_success()
    {
        var (service, runner, network) = Setup();
        runner.EnqueueResult(new(0, """{"status":"Connected","elapsedMs":1} """, "", ""));
        var result = await service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, "egress", "TCP", "10.99.0.10", 443, true);
        Assert.False(result.Allowed);
        Assert.Contains("Unexpected connection", result.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_worker_does_not_appear_as_a_blocked_connection(bool combinedLogs)
    {
        var (service, runner, network) = Setup();
        runner.EnqueueResult(new(1, null, combinedLogs ? "Image pull failed" : "", combinedLogs ? "" : "Image pull failed"));
        var result = await service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, "egress", "TCP", "10.20.0.10", 443, true);
        Assert.Equal("Test could not run", result.Status);
        Assert.Equal("Image pull failed", result.Message);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.20.0.10; echo bad")]
    [InlineData("10.20.0.10/24")]
    [InlineData("::1")]
    public void Probe_requires_an_unambiguous_unicast_IP(string address) => Assert.Throws<ArgumentException>(() => NetworkFlowService.ValidateAddress(address));

    [Fact]
    public async Task Authorization_project_scope_and_protocol_are_checked_before_execution()
    {
        var (service, runner, network) = Setup(false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, "egress", "TCP", "10.20.0.10", 443, true));
        Assert.Empty(runner.ReceivedRequests);
        (service, runner, network) = Setup();
        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckAsync(Guid.NewGuid(), network.Id, network.Groups[0].Id, "egress", "TCP", "10.20.0.10", 443, true));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckAsync(network.ProjectId, network.Id, Guid.NewGuid(), "egress", "TCP", "10.20.0.10", 443, true));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, "egress", "UDP", "10.20.0.10", 443, true));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckAsync(network.ProjectId, network.Id, network.Groups[0].Id, "egress", "TCP", "10.20.0.10", 0, false));
        Assert.Empty(runner.ReceivedRequests);
    }
}

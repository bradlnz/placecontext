using System.Net;
using System.Text;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ClusterWorkloadAdmissionTests
{
    [Theory]
    [InlineData("StartError", "resource temporarily unavailable", true)]
    [InlineData("StartError", "cannot allocate memory", true)]
    [InlineData("StartError", "executable file not found", false)]
    [InlineData("OOMKilled", "memory limit exceeded", false)]
    [InlineData("Error", "resource temporarily unavailable", false)]
    public void Only_confirmed_resource_failures_before_execution_are_retried(string reason, string message, bool expected)
    {
        var pod = new V1Pod { Status = new V1PodStatus { ContainerStatuses =
            [new V1ContainerStatus { Name = "run", State = new V1ContainerState { Terminated = new V1ContainerStateTerminated
                { Reason = reason, Message = message, ExitCode = 128 } } }] } };
        Assert.Equal(expected, ClusterWorkloadRunner.IsRetryableStartupFailure(pod));
        Assert.Contains(reason, ClusterWorkloadRunner.DescribePodFailure(pod));
        pod.Status.ContainerStatuses[0].LastState = new V1ContainerState
        { Terminated = new V1ContainerStateTerminated { ExitCode = 1 } };
        Assert.False(ClusterWorkloadRunner.IsRetryableStartupFailure(pod));
    }

    [Fact]
    public void Eviction_before_start_can_retry_but_eviction_after_start_cannot()
    {
        var pod = new V1Pod { Status = new V1PodStatus { Reason = "Evicted" } };
        Assert.True(ClusterWorkloadRunner.IsRetryableStartupFailure(pod));
        pod.Status.ContainerStatuses = [new V1ContainerStatus { Name = "run", State = new V1ContainerState
        { Terminated = new V1ContainerStateTerminated { Reason = "Error", ExitCode = 137 } } }];
        Assert.False(ClusterWorkloadRunner.IsRetryableStartupFailure(pod));
    }

    [Fact]
    public void Queue_time_is_separate_from_runtime_and_leases_remain_bounded()
    {
        var now = DateTimeOffset.UtcNow;
        var start = now.AddMinutes(-5);
        var queueDeadline = now.AddMinutes(10);
        Assert.Equal(900, ClusterWorkloadRunner.SchedulingDeadlineSeconds(start, null, queueDeadline, 30, now));
        Assert.Equal(330, ClusterWorkloadRunner.SchedulingDeadlineSeconds(start, now, queueDeadline, 30, now));
        Assert.Equal(360, ClusterWorkloadRunner.LeaseDeadlineSeconds(start, 900, now));
        Assert.Equal(330, ClusterWorkloadRunner.LeaseDeadlineSeconds(start, 330, now));
    }

    [Fact]
    public async Task Admission_waits_for_resource_pressure_and_cancellation_stops_waiting()
    {
        using var api = new PodsApi();
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost" }, api);
        var runner = new ClusterWorkloadRunner(Options.Create(new WorkloadRunnerOptions()));
        using var stop = new CancellationTokenSource();
        var wait = runner.WaitForCapacityAsync(client, "tenant", "current", "run", DateTimeOffset.UtcNow.AddMinutes(1), false, stop.Token);
        await api.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(wait.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        api.Blocked = false;
        await runner.WaitForCapacityAsync(client, "tenant", "current", "run", DateTimeOffset.UtcNow.AddMinutes(1), false, default);
        Assert.All(api.Paths, path => Assert.Contains("/namespaces/tenant/pods", path));
    }

    [Theory]
    [InlineData("StartError", "resource temporarily unavailable", 2)]
    [InlineData("StartError", "executable not found", 1)]
    [InlineData("OOMKilled", "cannot allocate memory", 1)]
    public async Task Startup_retries_wait_for_peers_and_do_not_retry_code_or_configuration_failures(
        string reason, string message, int attempts)
    {
        using var api = new RetryApi(reason, message);
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost" }, api);
        var runner = new ClusterWorkloadRunner(Options.Create(new WorkloadRunnerOptions()));
        var job = new V1Job { Metadata = new V1ObjectMeta { Name = "current" }, Spec = new V1JobSpec() };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await runner.RunPreparedJobAsync(client, "tenant", job, "run", 30, stop.Token);
        Assert.Equal(attempts, api.Attempts);
        Assert.Equal(attempts == 2 ? 0 : 128, result.ExitCode);
        if (attempts == 2)
        {
            Assert.True(api.PeerChecks >= 2);
            Assert.Contains("deferred", result.Stderr);
        }
    }

    private sealed class RetryApi(string reason, string message) : DelegatingHandler
    {
        public int Attempts;
        public int PeerChecks;
        private bool _deleted;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK)
            { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                Attempts++;
                _deleted = false;
                return Reply(await request.Content!.ReadAsStringAsync(ct));
            }
            if (request.Method == HttpMethod.Delete)
            {
                _deleted = true;
                return Reply("{\"kind\":\"Status\",\"status\":\"Success\"}");
            }
            if (path.EndsWith("/log"))
            {
                return Reply("{}");
            }
            if (path.EndsWith("/pods"))
            {
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                if (query.Contains("placecontext-workload"))
                {
                    var peer = _deleted && ++PeerChecks == 1
                        ? new V1Pod { Metadata = new V1ObjectMeta { Name = "peer" }, Status = new V1PodStatus { Phase = "Running" } }
                        : null;
                    return Reply(KubernetesJson.Serialize(new V1PodList { Items = peer is null ? [] : [peer] }));
                }
                var pod = new V1Pod { Metadata = new V1ObjectMeta { Name = "pod" }, Status = new V1PodStatus
                {
                    Phase = Attempts == 1 ? "Failed" : "Succeeded",
                    ContainerStatuses = [new V1ContainerStatus { Name = "run", State = new V1ContainerState
                    { Terminated = new V1ContainerStateTerminated { ExitCode = Attempts == 1 ? 128 : 0,
                        Reason = Attempts == 1 ? reason : "Completed", Message = message } } }]
                } };
                return Reply(KubernetesJson.Serialize(new V1PodList { Items = [pod] }));
            }
            if (_deleted)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                { RequestMessage = request, Content = new StringContent("{\"kind\":\"Status\",\"status\":\"Failure\",\"reason\":\"NotFound\",\"code\":404}", Encoding.UTF8, "application/json") };
            }
            return Reply(KubernetesJson.Serialize(new V1Job { Metadata = new V1ObjectMeta { Name = "current" },
                Spec = new V1JobSpec(), Status = new V1JobStatus { Failed = Attempts == 1 ? 1 : 0, Succeeded = Attempts == 2 ? 1 : 0 } }));
        }
    }

    private sealed class PodsApi : DelegatingHandler
    {
        public bool Blocked = true;
        public TaskCompletionSource Observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Paths = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var pod = Blocked ? """
                {"metadata":{"name":"peer","labels":{"job-name":"peer"}},"status":{"phase":"Pending","conditions":[{"type":"PodScheduled","status":"False","reason":"Unschedulable","message":"Insufficient memory"}]}}
                """ : "";
            Observed.TrySetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { RequestMessage = request, Content = new StringContent("{\"apiVersion\":\"v1\",\"kind\":\"PodList\",\"items\":[" + pod + "]}", Encoding.UTF8, "application/json") });
        }
    }
}

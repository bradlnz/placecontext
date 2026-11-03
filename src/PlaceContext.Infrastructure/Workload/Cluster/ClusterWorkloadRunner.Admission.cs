using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;
using PlaceContext.Application.Workload;

namespace PlaceContext.Infrastructure.Workload;

public sealed partial class ClusterWorkloadRunner
{
    internal async Task<WorkloadRunResult> RunPreparedJobAsync(Kubernetes client, string ns, V1Job job,
        string correlationId, int timeoutSeconds, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, _options.ClusterQueueTimeoutSeconds));
        var name = job.Metadata.Name;
        var history = new System.Text.StringBuilder();
        for (var attempt = 0; ; attempt++)
        {
            await WaitForCapacityAsync(client, ns, name, correlationId, deadline, drain: attempt > 0, ct);
            await client.BatchV1.CreateNamespacedJobAsync(job, ns, cancellationToken: ct);
            var (result, retryable) = await AwaitResultAsync(client, ns, name, correlationId, timeoutSeconds, deadline, ct);
            if (!retryable || attempt >= Math.Max(0, _options.ClusterStartupRetries) || DateTimeOffset.UtcNow >= deadline)
            {
                return result with { Stderr = history + result.Stderr };
            }
            history.AppendLine($"[scheduler] Startup attempt {attempt + 1} deferred: {result.Stderr}");
            _output?.Set(correlationId, $"[scheduler] Temporary startup failure. Waiting for other workloads before retry {attempt + 1}.\n{result.Stderr}");
            await DeleteAttemptAsync(client, ns, name, ct);
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 5 * Math.Pow(2, attempt))), ct);
        }
    }

    internal async Task WaitForCapacityAsync(Kubernetes client, string ns, string name, string correlationId,
        DateTimeOffset deadline, bool drain, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for other workloads to release cluster capacity.");
            }
            var pods = await client.CoreV1.ListNamespacedPodAsync(ns,
                labelSelector: "placecontext-workload=job", cancellationToken: ct);
            var peers = pods.Items.Where(p => p.Metadata?.Labels?.Any(label => label.Key == "job-name" && label.Value == name) != true
                && p.Status?.Phase is not ("Succeeded" or "Failed")).ToList();
            if (!peers.Any(p => drain || IsWaitingForResources(p)))
            {
                return;
            }
            _output?.Set(correlationId, $"[scheduler] Waiting for cluster capacity ({peers.Count} other workloads). The execution timeout has not started.");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    internal static bool IsWaitingForResources(V1Pod pod) => pod.Status?.Conditions?.Any(c =>
        c.Type == "PodScheduled" && c.Status == "False" && c.Reason == "Unschedulable"
        && IsResourcePressure(c.Message)) == true;

    private static bool IsResourcePressure(string? message) => message is not null &&
        new[] { "insufficient cpu", "insufficient memory", "insufficient ephemeral-storage", "too many pods",
            "resource temporarily unavailable", "cannot allocate memory", "not enough memory", "no space left on device",
            "memorypressure", "diskpressure", "pidpressure" }
        .Any(reason => message.Contains(reason, StringComparison.OrdinalIgnoreCase));

    internal static bool IsRetryableStartupFailure(V1Pod pod)
    {
        var run = pod.Status?.ContainerStatuses?.FirstOrDefault(c => c.Name == "run");
        // Once user code may have run, automatic infrastructure retries could duplicate its effects.
        if (run?.State?.Running is not null || run?.LastState?.Terminated is not null)
        {
            return false;
        }
        if (run?.State?.Terminated is { } terminated)
        {
            return terminated.Reason == "StartError" && IsResourcePressure(terminated.Message);
        }
        if (pod.Status?.InitContainerStatuses?.Any(c => c.State?.Terminated is { Reason: "StartError" } ended
            && IsResourcePressure(ended.Message)) == true)
        {
            return true;
        }
        return (pod.Status?.Reason is "Evicted" or "NodeLost") && run is null;
    }

    internal static string DescribePodFailure(V1Pod? pod)
    {
        if (pod?.Status is not { } status)
        {
            return "Waiting for the scheduler to create a pod.";
        }
        var states = (status.InitContainerStatuses ?? []).Concat(status.ContainerStatuses ?? []);
        var messages = states.Select(c => c.State?.Terminated is { ExitCode: not 0 } ended
            ? $"{c.Name}: {ended.Reason} (exit {ended.ExitCode}) {ended.Message}"
            : c.State?.Waiting is { } waiting ? $"{c.Name}: {waiting.Reason} {waiting.Message}" : null)
            .Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (!string.IsNullOrWhiteSpace(status.Reason))
        {
            messages.Add($"{status.Reason}: {status.Message}");
        }
        messages.AddRange((status.Conditions ?? []).Where(c => c.Type == "PodScheduled" && c.Status == "False")
            .Select(c => $"{c.Reason}: {c.Message}"));
        return string.Join("; ", messages);
    }

    internal static int SchedulingDeadlineSeconds(DateTimeOffset? jobStarted, DateTimeOffset? executionStarted,
        DateTimeOffset queueDeadline, int timeoutSeconds, DateTimeOffset now)
    {
        var deadline = executionStarted?.AddSeconds(timeoutSeconds) ?? queueDeadline;
        return (int)Math.Clamp(Math.Ceiling((deadline - (jobStarted ?? now)).TotalSeconds), 1, int.MaxValue);
    }

    private static async Task DeleteAttemptAsync(Kubernetes client, string ns, string name, CancellationToken ct)
    {
        using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cleanup.CancelAfter(TimeSpan.FromSeconds(30));
        await client.BatchV1.DeleteNamespacedJobAsync(name, ns,
            body: new V1DeleteOptions { PropagationPolicy = "Foreground", GracePeriodSeconds = 0 }, cancellationToken: cleanup.Token);
        while (true)
        {
            try
            {
                await client.BatchV1.ReadNamespacedJobAsync(name, ns, cancellationToken: cleanup.Token);
            }
            catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }
            await Task.Delay(200, cleanup.Token);
        }
    }
}

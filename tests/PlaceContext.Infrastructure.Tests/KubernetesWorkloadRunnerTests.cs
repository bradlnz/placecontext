using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="KubernetesWorkloadRunner"/> helpers that don't need a live cluster.
/// </summary>
public class KubernetesWorkloadRunnerTests
{
    // Regression: the shared SandboxMemory option is written in Docker notation ("256m" = 256 MiB),
    // but a bare "m" in a Kubernetes quantity means milli (10⁻³). Passing it verbatim set the
    // container memory limit to ~0 bytes, so runc OOM-killed the container init before any user code
    // ran (exit 128, StartError, empty logs) — every in-cluster job failed. ToK8sMemory translates it.
    [Theory]
    [InlineData("256m", "256Mi")]   // Docker MiB → k8s MiB (the bug that broke all jobs)
    [InlineData("512m", "512Mi")]
    [InlineData("1g", "1Gi")]       // Docker GiB → k8s GiB
    [InlineData("64k", "64Ki")]     // Docker KiB → k8s KiB
    [InlineData("1024b", "1024")]   // explicit bytes → bare number
    [InlineData("268435456", "268435456")] // bare bytes → unchanged
    [InlineData("256Mi", "256Mi")]  // already a k8s suffix → unchanged
    [InlineData("2Gi", "2Gi")]
    [InlineData("", "")]            // empty → unchanged (limit omitted upstream)
    public void ToK8sMemory_translates_docker_notation(string input, string expected)
        => Assert.Equal(expected, KubernetesWorkloadRunner.ToK8sMemory(input));
}

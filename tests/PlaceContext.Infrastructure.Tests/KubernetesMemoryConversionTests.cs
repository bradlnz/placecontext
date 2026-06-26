using PlaceContext.Infrastructure.Workload;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Guards <see cref="KubernetesWorkloadRunner.ToK8sMemory"/>: Docker memory notation must be translated
/// to Kubernetes notation. A bare "m" in a k8s quantity means <i>milli</i>, so passing Docker's "256m"
/// verbatim would set the limit to ~0 bytes and OOM-kill every job container before it ran.
/// </summary>
public class KubernetesMemoryConversionTests
{
    [Theory]
    [InlineData("256m", "256Mi")]
    [InlineData("512M", "512Mi")]
    [InlineData("1g", "1Gi")]
    [InlineData("64k", "64Ki")]
    [InlineData("1048576b", "1048576")] // explicit bytes
    [InlineData("268435456", "268435456")] // bare number = bytes, unchanged
    [InlineData("512Mi", "512Mi")] // already a k8s suffix, untouched
    [InlineData("2Gi", "2Gi")]
    public void ToK8sMemory_translates_docker_notation(string docker, string expected)
        => Assert.Equal(expected, KubernetesWorkloadRunner.ToK8sMemory(docker));
}

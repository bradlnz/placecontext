using k8s;

namespace PlaceContext.Infrastructure.Workload;

internal static class ClusterRuntime
{
    public static KubernetesClientConfiguration Configuration(WorkloadRunnerOptions options) => KubernetesClientConfiguration.IsInCluster()
        ? KubernetesClientConfiguration.InClusterConfig()
        : KubernetesClientConfiguration.BuildConfigFromConfigFile(options.KubeConfigPath);
}

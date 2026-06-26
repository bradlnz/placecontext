using System.Runtime.CompilerServices;

// Expose internal helpers (e.g. KubernetesWorkloadRunner.ToK8sMemory) to the unit test project.
[assembly: InternalsVisibleTo("PlaceContext.Infrastructure.Tests")]

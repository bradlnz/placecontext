using k8s.Models;
using k8s;

namespace PlaceContext.Infrastructure.Workload;

public sealed partial class ClusterServiceConsole
{
    internal static V1Namespace BuildNamespace(Guid tenantId, Guid projectId, Guid id, string kind, string name, Guid? owner = null, string? engine = null)
    {
        var labels = new Dictionary<string, string>
        {
            [Managed] = "true",
            [Tenant] = tenantId.ToString("D"),
            [Project] = projectId.ToString("D"),
            [Resource] = id.ToString("D"),
            [Kind] = kind,
            ["pod-security.kubernetes.io/enforce"] = "restricted",
            ["pod-security.kubernetes.io/audit"] = "restricted",
            ["pod-security.kubernetes.io/warn"] = "restricted",
        };
        if (owner is { } app)
        {
            labels[Owner] = app.ToString("D");
        }
        if (engine is not null)
        {
            labels[Engine] = engine;
        }
        return new()
        {
            Metadata = new()
            {
                Name = kind == "service" ? AppNamespace(id) : DatabaseNamespace(id),
                Labels = labels,
                Annotations = new Dictionary<string, string> { [Name] = name.Trim() }
            }
        };
    }
    private async Task ProvisionNamespaceAsync(V1Namespace ns, CancellationToken ct)
    {
        await client.CoreV1.CreateNamespaceAsync(ns, cancellationToken: ct);
        // Management binds only the explicit hosted-resource role; workloads receive no role binding.
        await client.RbacAuthorizationV1.CreateNamespacedRoleBindingAsync(new V1RoleBinding
        {
            Metadata = new() { Name = "placecontext-management" },
            RoleRef = new() { ApiGroup = "rbac.authorization.k8s.io", Kind = "ClusterRole", Name = "placecontext-hosted-runtime" },
            Subjects = [new Rbacv1Subject { Kind = "ServiceAccount", Name = "placecontext", NamespaceProperty = "placecontext" }],
        }, ns.Metadata.Name, cancellationToken: ct);
        await client.CoreV1.CreateNamespacedServiceAccountAsync(new V1ServiceAccount { Metadata = new() { Name = "workload" }, AutomountServiceAccountToken = false }, ns.Metadata.Name, cancellationToken: ct);
        await client.CoreV1.CreateNamespacedResourceQuotaAsync(new V1ResourceQuota
        {
            Metadata = new() { Name = "workload-limits" },
            Spec = new()
            {
                Hard = new Dictionary<string, ResourceQuantity>
                {
                    ["pods"] = new("3"),
                    ["services"] = new("1"),
                    ["persistentvolumeclaims"] = new("1"),
                    ["requests.storage"] = new("20Gi"),
                    ["limits.cpu"] = new("16"),
                    ["limits.memory"] = new("16Gi")
                }
            },
        }, ns.Metadata.Name, cancellationToken: ct);
    }
    internal static V1Deployment BuildDeployment(string name, string image, int memory, decimal cpus, long uid, long gid, int? port)
    {
        if (uid <= 0 || gid <= 0)
        {
            throw new ArgumentException("Root users and groups are forbidden.");
        }
        var security = ClusterWorkloadRunner.BuildRestrictedContainerSecurityContext(new()
        {
            RunAsUser = uid,
            RunAsGroup = gid
        });
        security.ReadOnlyRootFilesystem = true;
        security.Privileged = false;
        return new V1Deployment
        {
            Metadata = new() { Name = name, Annotations = new Dictionary<string, string>() },
            Spec = new()
            {
                Replicas = 1,
                Strategy = new()
                {
                    Type = "Recreate"
                },
                Selector = new()
                {
                    MatchLabels = new Dictionary<string, string> { ["app"] = "workload" }
                },
                Template = new()
                {
                    Metadata = new()
                    {
                        Labels = new Dictionary<string, string> { ["app"] = "workload" }
                    },
                    Spec = new()
                    {
                        ServiceAccountName = "workload",
                        AutomountServiceAccountToken = false,
                        EnableServiceLinks = false,
                        SecurityContext = new()
                        {
                            RunAsNonRoot = true,
                            RunAsUser = uid,
                            RunAsGroup = gid,
                            FsGroup = gid,
                            FsGroupChangePolicy = "OnRootMismatch",
                            SeccompProfile = new()
                            {
                                Type = "RuntimeDefault"
                            }
                        },
                        Containers = [new V1Container
                        {
                            Name = "workload", Image = image, SecurityContext = security,
                            Ports = port is { } number ? [new V1ContainerPort { ContainerPort = number }] : [],
                            Resources = new() { Limits = new Dictionary<string, ResourceQuantity> { ["memory"] = new($"{memory}Mi"), ["cpu"] = new(cpus.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
                                Requests = new Dictionary<string, ResourceQuantity> { ["memory"] = new($"{Math.Min(memory, 256)}Mi"), ["cpu"] = new("100m") } },
                            VolumeMounts = [new() { Name = "tmp", MountPath = "/tmp" }],
                            ReadinessProbe = port is { } probePort ? new() { TcpSocket = new() { Port = probePort }, InitialDelaySeconds = 3, PeriodSeconds = 5 } : null,
                        }],
                        Volumes = [new() { Name = "tmp", EmptyDir = new() { SizeLimit = new("64Mi") } }],
                    },
                },
            },
        };
    }
    internal static V1Service BuildService(string name, int port) => new()
    {
        Metadata = new()
        {
            Name = name
        },
        Spec = new()
        {
            Type = "ClusterIP",
            Selector = new Dictionary<string, string> { ["app"] = "workload" },
            Ports = [new() { Port = port, TargetPort = port }]
        },
    };
    internal static V1NetworkPolicyPeer Peer(string ns, string app) => new()
    {
        NamespaceSelector = new()
        {
            MatchLabels = new Dictionary<string, string> { ["kubernetes.io/metadata.name"] = ns }
        },
        PodSelector = new()
        {
            MatchLabels = new Dictionary<string, string> { ["app"] = app }
        },
    };
    internal static V1NetworkPolicy BuildNetworkPolicy(string ns, int? webPort, IReadOnlyList<(string Namespace, int Port)> databases, (string Namespace, int Port)? owner, bool allowInternet = false)
    {
        var ingress = new List<V1NetworkPolicyIngressRule>();
        var egress = new List<V1NetworkPolicyEgressRule>
        {
            new() { To = [new() { NamespaceSelector = new() { MatchLabels = new Dictionary<string, string> { ["kubernetes.io/metadata.name"] = "kube-system" } }, PodSelector = new() { MatchLabels = new Dictionary<string, string> { ["k8s-app"] = "kube-dns" } } }],
                Ports = [new() { Port = 53, Protocol = "UDP" }, new() { Port = 53, Protocol = "TCP" }] },
        };
        void DeleteResources()
        {
            if (owner is { } databaseOwner)
            {
                ingress.Add(new()
                {
                    FromProperty = [Peer(databaseOwner.Namespace, "workload"), Peer("placecontext", "placecontext")],
                    Ports = [new() { Port = databaseOwner.Port, Protocol = "TCP" }]
                });
                return;
            }
            if (webPort is { } port)
            {
                ingress.Add(new()
                {
                    FromProperty = [Peer("placecontext", "placecontext")],
                    Ports = [new() { Port = port, Protocol = "TCP" }]
                });
            }
            egress.Add(new()
            {
                To = [Peer("placecontext", "placecontext")],
                Ports = [new() { Port = 7700, Protocol = "TCP" }]
            });
            if (allowInternet)
            {
                egress.AddRange(ClusterWorkloadRunner.BuildEgressPolicy("", "", true).Spec.Egress
                .Where(rule => rule.To.Any(peer => peer.IpBlock is not null)));
            }
            foreach (var database in databases)
            {
                egress.Add(new()
                {
                    To = [Peer(database.Namespace, "workload")],
                    Ports = [new() { Port = database.Port, Protocol = "TCP" }]
                });
            }
        }
        DeleteResources();
        return new()
        {
            Metadata = new()
            {
                Name = "workload-isolation",
                NamespaceProperty = ns
            },
            Spec = new()
            {
                PodSelector = new(),
                PolicyTypes = ["Ingress", "Egress"],
                Ingress = ingress,
                Egress = egress
            }
        };
    }
}

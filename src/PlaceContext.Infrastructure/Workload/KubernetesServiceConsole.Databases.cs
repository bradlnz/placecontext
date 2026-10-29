using System.Net;
using System.Security.Cryptography;
using System.Text;
using k8s;
using k8s.Autorest;
using k8s.Models;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Workload;

public sealed partial class KubernetesServiceConsole
{
    async Task<IReadOnlyList<DatabaseInstance>> IDatabaseConsole.ListAsync(Guid projectId, CancellationToken ct)
    {
        var result = new List<DatabaseInstance>();
        foreach (var ns in await ListNamespacesAsync(projectId, "database", ct))
        {
            var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(ns.Metadata.Name, cancellationToken: ct);
            var deployment = deployments.Items.FirstOrDefault(item => item.Metadata.Name == "database");
            var pg = ns.Metadata.Labels[Engine] == "postgres";
            result.Add(new(Guid.Parse(ns.Metadata.Labels[Resource]), ns.Metadata.Annotations[Name], ns.Metadata.Labels[Engine], Status(deployment),
                deployment?.Status?.AvailableReplicas > 0 ? "ready" : "pending", $"database.{ns.Metadata.Name}.svc.cluster.local", pg ? 5432 : 6379,
                pg ? "app" : "0", pg ? "app" : "default", "data", new(ns.Metadata.CreationTimestamp ?? DateTime.UtcNow),
                Guid.TryParse(Value(ns.Metadata.Labels, Owner), out var app) ? app : null, ns.Metadata.Name));
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }
    internal static void ValidateDatabase(DatabaseLaunch database)
    {
        if (database.ServiceId is null || database.ServiceId == Guid.Empty) throw new ArgumentException("Choose the app that owns this database.");
        if (database.Engine is not ("postgres" or "redis")) throw new ArgumentException("Choose PostgreSQL or Redis.");
        Validate(new(database.Name, "database", null, database.MemoryMb, database.Cpus));
        if (database.Engine == "postgres" && database.MemoryMb < 256) throw new ArgumentException("PostgreSQL requires at least 256 MiB.");
    }
    public async Task CreateAsync(Guid projectId, DatabaseLaunch database, CancellationToken ct = default)
    {
        ValidateDatabase(database);
        var app = await OwnedAsync(projectId, database.ServiceId!.Value, "service", ct);
        if ((await ListNamespacesAsync(projectId, "database", ct)).Any(item => Value(item.Metadata.Labels, Owner) == database.ServiceId.Value.ToString("D") && item.Metadata.Labels[Engine] == database.Engine))
            throw new InvalidOperationException("This app already owns a store of this type.");
        var id = Guid.NewGuid();
        var pg = database.Engine == "postgres";
        var ns = BuildNamespace(tenant.TenantId, projectId, id, "database", database.Name, database.ServiceId, database.Engine);
        // Provisioning failures retain their namespace and PVC for recovery; never delete database data automatically.
        await ProvisionNamespaceAsync(ns, ct);
        await client.NetworkingV1.CreateNamespacedNetworkPolicyAsync(BuildNetworkPolicy(ns.Metadata.Name, null, [], (app.Metadata.Name, pg ? 5432 : 6379)), ns.Metadata.Name, cancellationToken: ct);
        await client.CoreV1.CreateNamespacedSecretAsync(new V1Secret
        {
            Metadata = new() { Name = "credentials" }, Type = "Opaque", StringData = new Dictionary<string, string>
            { ["POSTGRES_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), ["APP_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) },
        }, ns.Metadata.Name, cancellationToken: ct);
        await client.CoreV1.CreateNamespacedPersistentVolumeClaimAsync(new V1PersistentVolumeClaim
        { Metadata = new() { Name = "data" }, Spec = new() { AccessModes = ["ReadWriteOnce"], Resources = new() { Requests = new Dictionary<string, ResourceQuantity> { ["storage"] = new("5Gi") } } } }, ns.Metadata.Name, cancellationToken: ct);
        if (pg) await client.CoreV1.CreateNamespacedConfigMapAsync(new V1ConfigMap
        {
            Metadata = new() { Name = "database-init" }, Data = new Dictionary<string, string> { ["01-app.sh"] = """
                #!/bin/sh
                set -eu
                psql -v ON_ERROR_STOP=1 --username postgres --dbname postgres -v app_password="$APP_PASSWORD" <<'SQL'
                CREATE ROLE app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD :'app_password';
                CREATE DATABASE app OWNER app;
                SQL
                """ },
        }, ns.Metadata.Name, cancellationToken: ct);
        await client.CoreV1.CreateNamespacedServiceAsync(BuildService("database", pg ? 5432 : 6379), ns.Metadata.Name, cancellationToken: ct);
        await client.AppsV1.CreateNamespacedDeploymentAsync(BuildDatabaseDeployment(database), ns.Metadata.Name, cancellationToken: ct);
        await BindDatabaseAsync(projectId, database.ServiceId.Value, id, ct);
    }
    internal static V1Deployment BuildDatabaseDeployment(DatabaseLaunch database)
    {
        ValidateDatabase(database);
        var pg = database.Engine == "postgres";
        var deployment = BuildDeployment("database", pg ? "postgres:17-alpine" : "redis:7-alpine", database.MemoryMb, database.Cpus, pg ? 70 : 999, pg ? 70 : 1000, pg ? 5432 : 6379);
        var pod = deployment.Spec.Template.Spec;
        var container = pod.Containers[0];
        container.EnvFrom = [new() { SecretRef = new() { Name = "credentials" } }];
        pod.Volumes.Add(new() { Name = "data", PersistentVolumeClaim = new() { ClaimName = "data" } });
        container.VolumeMounts.Add(new() { Name = "data", MountPath = pg ? "/var/lib/postgresql/data" : "/data" });
        if (pg)
        {
            container.Env = [new() { Name = "PGDATA", Value = "/var/lib/postgresql/data/pgdata" }, new() { Name = "POSTGRES_USER", Value = "postgres" }, new() { Name = "POSTGRES_DB", Value = "postgres" }, new() { Name = "POSTGRES_INITDB_ARGS", Value = "--auth-host=scram-sha-256" }];
            pod.Volumes.Add(new() { Name = "socket", EmptyDir = new() { SizeLimit = new("16Mi") } });
            container.VolumeMounts.Add(new() { Name = "socket", MountPath = "/var/run/postgresql" });
            pod.Volumes.Add(new() { Name = "init", ConfigMap = new() { Name = "database-init", DefaultMode = 365 } });
            container.VolumeMounts.Add(new() { Name = "init", MountPath = "/docker-entrypoint-initdb.d", ReadOnlyProperty = true });
            container.ReadinessProbe = new() { Exec = new() { Command = ["pg_isready", "-U", "postgres", "-d", "app"] }, InitialDelaySeconds = 5, PeriodSeconds = 5 };
        }
        else
        {
            container.Command = ["sh", "-c"];
            container.Args = ["printf 'user default on >%s ~* &* +@read +@write +@connection -@admin -@dangerous +ping\n' \"$APP_PASSWORD\" > /tmp/users.acl; exec redis-server --appendonly yes --aclfile /tmp/users.acl"];
        }
        return deployment;
    }
    private async Task BindDatabaseAsync(Guid projectId, Guid appId, Guid databaseId, CancellationToken ct)
    {
        var app = await OwnedAsync(projectId, appId, "service", ct);
        var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync("app", app.Metadata.Name, cancellationToken: ct);
        var ids = BoundIds(deployment).Append(databaseId).Distinct().ToArray();
        var connections = new Dictionary<string, string>();
        var networks = new List<(string Namespace, int Port)>();
        foreach (var id in ids)
        {
            var database = await OwnedAsync(projectId, id, "database", ct);
            RequireDatabaseOwner(database, appId);
            var secret = await client.CoreV1.ReadNamespacedSecretAsync("credentials", database.Metadata.Name, cancellationToken: ct);
            var password = Encoding.UTF8.GetString(secret.Data["APP_PASSWORD"]);
            var host = $"database.{database.Metadata.Name}.svc.cluster.local";
            var pg = database.Metadata.Labels[Engine] == "postgres";
            networks.Add((database.Metadata.Name, pg ? 5432 : 6379));
            if (pg)
            {
                connections["DATABASE_URL"] = $"postgresql://app:{Uri.EscapeDataString(password)}@{host}:5432/app";
                connections["PGHOST"] = host; connections["PGPORT"] = "5432"; connections["PGDATABASE"] = "app"; connections["PGUSER"] = "app"; connections["PGPASSWORD"] = password;
            }
            else connections["REDIS_URL"] = $"redis://default:{Uri.EscapeDataString(password)}@{host}:6379/0";
        }
        var credentials = new V1Secret { Metadata = new() { Name = "database-connections" }, StringData = connections, Type = "Opaque" };
        try
        {
            var existing = await client.CoreV1.ReadNamespacedSecretAsync(credentials.Metadata.Name, app.Metadata.Name, cancellationToken: ct);
            credentials.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
            await client.CoreV1.ReplaceNamespacedSecretAsync(credentials, credentials.Metadata.Name, app.Metadata.Name, cancellationToken: ct);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        { await client.CoreV1.CreateNamespacedSecretAsync(credentials, app.Metadata.Name, cancellationToken: ct); }
        var container = deployment.Spec.Template.Spec.Containers[0];
        var policy = BuildNetworkPolicy(app.Metadata.Name, container.Ports?.SingleOrDefault()?.ContainerPort, networks, null, Value(deployment.Metadata.Annotations, Internet) == "true");
        policy.Metadata.ResourceVersion = (await client.NetworkingV1.ReadNamespacedNetworkPolicyAsync(policy.Metadata.Name, app.Metadata.Name, cancellationToken: ct)).Metadata.ResourceVersion;
        await client.NetworkingV1.ReplaceNamespacedNetworkPolicyAsync(policy, policy.Metadata.Name, app.Metadata.Name, cancellationToken: ct);
        container.EnvFrom = [new() { SecretRef = new() { Name = "app-environment" } }, new() { SecretRef = new() { Name = "database-connections" } }];
        deployment.Metadata.Annotations[Databases] = string.Join(',', ids);
        deployment.Spec.Template.Metadata.Annotations = new Dictionary<string, string> { ["placecontext.io/database-change"] = Guid.NewGuid().ToString("N") };
        await client.AppsV1.ReplaceNamespacedDeploymentAsync(deployment, "app", app.Metadata.Name, cancellationToken: ct);
    }
    internal static void RequireDatabaseOwner(V1Namespace database, Guid appId)
    {
        if (appId == Guid.Empty || Value(database.Metadata.Labels, Owner) != appId.ToString("D")) throw new UnauthorizedAccessException("This database belongs to a different app or has no assigned app.");
    }
    async Task IDatabaseConsole.ControlAsync(Guid projectId, Guid databaseId, string action, CancellationToken ct)
    {
        var ns = await OwnedAsync(projectId, databaseId, "database", ct);
        await ControlDeploymentAsync(ns.Metadata.Name, "database", action, ct);
    }
    async Task<string> IDatabaseConsole.LogsAsync(Guid projectId, Guid databaseId, CancellationToken ct)
    {
        var ns = await OwnedAsync(projectId, databaseId, "database", ct);
        return await ReadLogsAsync(ns.Metadata.Name, ct);
    }
}

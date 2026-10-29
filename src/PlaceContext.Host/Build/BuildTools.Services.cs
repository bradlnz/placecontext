using System.ComponentModel;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Build;

public sealed partial class BuildTools
{
    private IServiceConsole Services => services ?? throw new InvalidOperationException("Services hosting is not configured.");
    private IDatabaseConsole Databases => databases ?? throw new InvalidOperationException("Database hosting is not configured.");

    private async Task<object> HostingContextAsync(CancellationToken ct)
    {
        var available = services is not null && databases is not null && await permissions.HasAsync(Permission.SettingsManage, ct);
        if (!available) return new { Available = false, Reason = "Services requires configured hosting and settings management permission." };
        try { return new { Available = true, Services = await Services.ListAsync(projectId, ct), Databases = await Databases.ListAsync(projectId, ct) }; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new { Available = false, Error = ex.Message }; }
    }

    [Description("Read how to build and host apps with databases in this project's Services. No external hosting account is needed.")]
    public string service_authoring_guide() => """
        Services hosts long-running web apps and workers on the existing Kubernetes cluster.
        Use list_services/get_service before creating or changing an app. Launch with launch_service:
        provide name, image and containerPort (null for a worker); web apps must set requireSso=true.
        For a source app, set image="code" and code={runtimeId:"python" or "node", entrypoint:"main.py" or "index.js", files:[{path,content}], allowNetworkEgress:false}.
        Source is limited to 100 files / 400 KB; larger apps use prebuilt images. Files live in /work.
        Listen on 0.0.0.0 and the PORT environment variable. Python/Node are invoked on the entrypoint.
        Include requirements.txt or package.json (and lockfile) for dependencies; these require code.allowNetworkEgress=true
        to download packages from public networks. Private network access stays restricted to platform services and owned databases.
        Use only source and non-secret configuration in tool arguments. Never embed credentials in files or chat.
        Containers run non-root, with a read-only root filesystem and ephemeral /tmp and /work. Durable data belongs in a database.
        Create the app first, then create_database with its serviceId and engine postgres or redis.
        The platform generates credentials, provisions persistent storage, binds the database and restarts the app.
        PostgreSQL exposes DATABASE_URL and PGHOST/PGPORT/PGDATABASE/PGUSER/PGPASSWORD; Redis exposes REDIS_URL.
        Read these from the environment; never log or return their values. Initialize/migrate app tables in app startup,
        retry database readiness, and preserve existing data. query_database is read-only and cannot create a schema.
        Use update_service_code for a complete replacement file set; it preserves the service identity, database bindings and SSO.
        Existing image services require an already-built image; source updates apply to source apps.
        Refresh list_services, inspect service_logs, and inspect database status/logs to diagnose startup.
        Return the supplied OpenUrl to the user once healthy; the app uses platform SSO and its own origin.
        Do not delete/recreate apps or databases to fix an error. Service removal is blocked while it owns databases.
        """;

    [Description("List this project's hosted apps/workers and their status, details URL and SSO open URL.")]
    public async Task<object> list_services(CancellationToken ct)
    {
        await RequireAsync(Permission.SettingsManage, ct);
        return (await Services.ListAsync(projectId, ct)).Select(item => new { Service = item,
            DetailsUrl = $"/project/{projectId}/services/{item.Id}", OpenUrl = item.RequireSso ? $"/services/open/{projectId}/{item.Id}" : null }).ToArray();
    }

    [Description("Read a hosted service and its complete source before editing. Credentials are never returned.")]
    public async Task<object> get_service(Guid serviceId, CancellationToken ct)
    {
        await RequireAsync(Permission.SettingsManage, ct);
        var item = (await Services.ListAsync(projectId, ct)).SingleOrDefault(item => item.Id == serviceId)
            ?? throw new InvalidOperationException("Service not found in this project.");
        return new { Service = item, Code = await Services.GetCodeAsync(projectId, serviceId, ct) };
    }

    [Description("Deploy an app/worker in Services from source or an existing image. Read service_authoring_guide. Returns a stable receipt; inspect status/logs before claiming it is ready. Create owned databases after deployment.")]
    public async Task<object> launch_service(ServiceLaunch definition, CancellationToken ct)
    {
        await RequireAsync(Permission.SettingsManage, ct);
        var id = await Services.LaunchAsync(projectId, definition, ct);
        return new { ServiceId = id, Status = "submitted", DetailsUrl = $"/project/{projectId}/services/{id}", OpenUrl = definition.ContainerPort is not null ? $"/services/open/{projectId}/{id}" : null };
    }

    [Description("Replace an existing source app's complete file set and redeploy, preserving its identity, settings and databases. Read get_service first.")]
    public async Task<object> update_service_code(Guid serviceId, ServiceCode code, CancellationToken ct)
    {
        await RequireAsync(Permission.SettingsManage, ct);
        await Services.UpdateCodeAsync(projectId, serviceId, code, ct);
        return new { ServiceId = serviceId, Status = "submitted" };
    }

    [Description("Read recent hosted app/container logs to diagnose startup or runtime failures. Never repeat any credentials a program logs.")]
    public async Task<string> service_logs(Guid serviceId, CancellationToken ct)
    { await RequireAsync(Permission.SettingsManage, ct); return await Services.LogsAsync(projectId, serviceId, ct); }

    [Description("Start, stop or restart a service. Use remove only for explicit user-requested deletion; apps owning databases cannot be removed.")]
    public async Task<object> control_service(Guid serviceId, string action, CancellationToken ct)
    { await RequireAsync(Permission.SettingsManage, ct); await Services.ControlAsync(projectId, serviceId, action, ct); return new { ServiceId = serviceId, Action = action }; }

    [Description("List project databases, their owning app IDs and readiness, without credentials.")]
    public async Task<object> list_databases(CancellationToken ct)
    { await RequireAsync(Permission.SettingsManage, ct); return await Databases.ListAsync(projectId, ct); }

    [Description("Provision persistent PostgreSQL or Redis owned by an existing service. Credentials and networking are bound automatically. One store of each engine per app. Inspect existing databases before creating.")]
    public async Task<object> create_database(DatabaseLaunch definition, CancellationToken ct)
    {
        await RequireAsync(Permission.SettingsManage, ct);
        await Databases.CreateAsync(projectId, definition, ct);
        return (await Databases.ListAsync(projectId, ct)).Single(item => item.ServiceId == definition.ServiceId && item.Engine == definition.Engine);
    }

    [Description("Run a read-only SQL query or supported Redis read command on an app-owned database. Schema/data changes belong in the app, not this tool.")]
    public async Task<object> query_database(Guid databaseId, string query, string? cursor, CancellationToken ct)
    { await RequireAsync(Permission.SettingsManage, ct); return await Databases.QueryAsync(projectId, databaseId, query, cursor, ct); }

    [Description("Read database container logs to diagnose readiness. Never repeat any credentials a program logs.")]
    public async Task<string> database_logs(Guid databaseId, CancellationToken ct)
    { await RequireAsync(Permission.SettingsManage, ct); return await Databases.LogsAsync(projectId, databaseId, ct); }
}

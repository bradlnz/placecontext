using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Startup;

/// <summary>
/// Creates a useful first-run workspace for local development. The bootstrap is name-based and
/// idempotent: existing user content is never replaced, and each demonstration record is created
/// only when its canonical name is absent.
/// </summary>
public static class DefaultWorkspaceBootstrap
{
    public static async Task RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger logger,
        CancellationToken ct = default)
    {
        var enabled = configuration.GetValue<bool?>("PlaceContext:Bootstrap:Enabled")
            ?? environment.IsDevelopment();
        if (!enabled)
            return;

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var tenant = await provider.GetRequiredService<ITenantStore>().GetOrCreateAsync("default", ct);
        CurrentTenant.Set(tenant);

        try
        {
            var auth = provider.GetRequiredService<IAuthService>();
            var admin = await auth.GetOrCreateOperatorAsync(ct);
            CurrentUser.Set(new UserIdentity(admin.Id, admin.Role));

            var app = provider.GetRequiredService<PlaceContextService>();
            var projects = await app.GetProjectsAsync(ct);
            var project = projects.FirstOrDefault(item =>
                string.Equals(item.Name, "Default project", StringComparison.OrdinalIgnoreCase));
            project ??= await app.CreateProjectAsync(
                configuration["PlaceContext:Bootstrap:DefaultProjectPath"] ?? "/workspace/default",
                "Default project",
                ct);

            if (configuration.GetValue("PlaceContext:Bootstrap:SeedDemoData", true))
                await SeedDemoDataAsync(app, project.Id, ct);

            logger.LogInformation(
                "Default workspace ready: tenant {TenantId}, administrator {UserId}, project {ProjectId}.",
                tenant.Id,
                admin.Id,
                project.Id);
        }
        finally
        {
            CurrentUser.Clear();
            CurrentTenant.Clear();
        }
    }

    private static async Task SeedDemoDataAsync(PlaceContextService app, Guid projectId, CancellationToken ct)
    {
        await SeedSecretsAsync(app, projectId, ct);
        var mcpIds = await SeedMcpConnectionsAsync(app, projectId, ct);
        var jobs = await SeedJobsAsync(app, projectId, mcpIds, ct);
        await SeedChainAsync(app, projectId, jobs, ct);
        await SeedProjectDataAsync(app, projectId, jobs["Ingest customer activity"].Id, ct);
    }

    private static async Task SeedSecretsAsync(PlaceContextService app, Guid projectId, CancellationToken ct)
    {
        var existing = await app.ListProjectSecretsAsync(projectId, ct);
        if (!existing.Any(item => item.Name == "REPORT_SIGNING_KEY"))
            await app.AddProjectSecretAsync(projectId, "REPORT_SIGNING_KEY", "local-demo-report-signing-key", ct);
    }

    private static async Task<IReadOnlyList<Guid>> SeedMcpConnectionsAsync(
        PlaceContextService app,
        Guid projectId,
        CancellationToken ct)
    {
        var connections = (await app.ListMcpConnectionsAsync(projectId, ct)).ToList();
        foreach (var definition in new[]
                 {
                     (Name: "Commerce MCP", Endpoint: "https://mcp.demo.local/commerce"),
                     (Name: "Warehouse MCP", Endpoint: "https://mcp.demo.local/warehouse"),
                 })
        {
            if (connections.Any(item => item.Name == definition.Name))
                continue;
            connections.Add(await app.CreateMcpConnectionAsync(
                new CreateMcpConnectionCommand(
                    projectId,
                    definition.Name,
                    "http",
                    EndpointUrl: definition.Endpoint,
                    AuthType: "none"),
                ct));
        }

        return connections.Select(item => item.Id).ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, JobView>> SeedJobsAsync(
        PlaceContextService app,
        Guid projectId,
        IReadOnlyList<Guid> mcpIds,
        CancellationToken ct)
    {
        var jobs = (await app.ListJobsAsync(projectId, ct)).ToDictionary(item => item.Name);

        async Task Ensure(
            string name,
            string description,
            string source,
            JobReturnType returnType,
            IReadOnlyList<PostJobActionKind> actions,
            string? returnFileName = null)
        {
            if (jobs.ContainsKey(name))
                return;

            jobs[name] = await app.CreateJobAsync(new CreateJobCommand(
                projectId,
                name,
                description,
                MapImage: null,
                MapRuntimeId: "python",
                MapSource: source,
                MapEntrypoint: "main.py",
                InputPayloads: ["{}"],
                MapEnv: new Dictionary<string, string> { ["ENVIRONMENT"] = "demo" },
                ReduceImage: null,
                ReduceRuntimeId: null,
                ReduceSource: null,
                ReduceEntrypoint: null,
                ReduceEnv: null,
                ConcurrencyLimit: 4,
                SuccessExitCodes: [0],
                PartialExitCodes: [],
                AllowNetworkEgress: false,
                AllowApiInvocation: true,
                Parameters: [new JobParameterDto("run_date", "Run date", false, "date")],
                PostJobActions: actions,
                ReturnType: returnType,
                ReturnFileName: returnFileName,
                RetryCount: 1,
                RetryDelaySeconds: 5,
                McpConnectionIds: mcpIds), ct);
        }

        await Ensure(
            "Ingest customer activity",
            "Normalises customer activity into the project data store.",
            """
            import json, sys
            payload = json.load(sys.stdin) if not sys.stdin.isatty() else {}
            print(json.dumps({"customers": payload.get("customers", []), "source": "demo"}))
            """,
            JobReturnType.Table,
            [PostJobActionKind.Csv]);
        await Ensure(
            "Score account health",
            "Calculates a health score for every customer account.",
            """
            import json, sys
            payload = json.load(sys.stdin)
            rows = payload.get("customers", payload if isinstance(payload, list) else [])
            print(json.dumps([{**row, "health_score": 92 if row.get("status") == "Active" else 58} for row in rows]))
            """,
            JobReturnType.Json,
            [PostJobActionKind.Chart]);
        await Ensure(
            "Prepare success digest",
            "Builds the customer-success summary used by downstream actions.",
            """
            import json, sys
            payload = json.load(sys.stdin)
            print(json.dumps({"summary": "Customer health review complete", "records": len(payload) if isinstance(payload, list) else 1}))
            """,
            JobReturnType.Json,
            [PostJobActionKind.HtmlReport]);
        await Ensure(
            "Generate weekly report",
            "Produces the final workspace health report artifact.",
            """
            import json, sys
            payload = json.load(sys.stdin)
            print(json.dumps({"title": "Workspace health report", "status": "ready", "input": payload}))
            """,
            JobReturnType.Html,
            [PostJobActionKind.HtmlReport, PostJobActionKind.Csv]);
        await Ensure(
            "Generate PDF report",
            "Creates a self-contained PDF artifact for graph preview and download.",
            """
            import json, sys
            from pathlib import Path

            payload = json.load(sys.stdin) if not sys.stdin.isatty() else {}
            summary = json.dumps(payload, sort_keys=True, default=str)[:240]

            def escape_pdf(value):
                return value.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")

            lines = [
                "PlaceContext workspace report",
                "Generated by the PDF return job",
                f"Input: {summary}",
            ]
            commands = ["BT", "/F1 18 Tf", "72 750 Td", f"({escape_pdf(lines[0])}) Tj", "/F1 11 Tf"]
            for line in lines[1:]:
                commands.extend(["0 -28 Td", f"({escape_pdf(line)}) Tj"])
            commands.append("ET")
            content = "\n".join(commands).encode("latin-1", "replace")

            objects = [
                b"<< /Type /Catalog /Pages 2 0 R >>",
                b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
                f"<< /Length {len(content)} >>\nstream\n".encode() + content + b"\nendstream",
                b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            ]
            pdf = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
            offsets = [0]
            for index, obj in enumerate(objects, 1):
                offsets.append(len(pdf))
                pdf.extend(f"{index} 0 obj\n".encode() + obj + b"\nendobj\n")
            xref = len(pdf)
            pdf.extend(f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode())
            for offset in offsets[1:]:
                pdf.extend(f"{offset:010d} 00000 n \n".encode())
            pdf.extend(f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())

            output = Path("/out")
            output.mkdir(parents=True, exist_ok=True)
            (output / "workspace-report.pdf").write_bytes(pdf)
            result = {"status": "ready", "file": "workspace-report.pdf", "bytes": len(pdf)}
            (output / "result.json").write_text(json.dumps(result), encoding="utf-8")
            print(json.dumps(result))
            """,
            JobReturnType.Pdf,
            [],
            returnFileName: "workspace-report.pdf");

        return jobs;
    }

    private static async Task<JobChainView> SeedChainAsync(
        PlaceContextService app,
        Guid projectId,
        IReadOnlyDictionary<string, JobView> jobs,
        CancellationToken ct)
    {
        var existing = (await app.ListJobChainsAsync(projectId, ct)).FirstOrDefault(item =>
            item.Name == "Customer health pipeline");
        if (existing is not null)
            return existing;

        var ingest = jobs["Ingest customer activity"].Id;
        var score = jobs["Score account health"].Id;
        var digest = jobs["Prepare success digest"].Id;
        var report = jobs["Generate weekly report"].Id;
        return await app.CreateJobChainAsync(
            projectId,
            "Customer health pipeline",
            "Ingests activity, fans out scoring and digest preparation, then generates a report.",
            [ingest, score, digest, report],
            stages: [[ingest], [score, digest], [report]],
            ct: ct);
    }

    private static async Task SeedProjectDataAsync(
        PlaceContextService app,
        Guid projectId,
        Guid ingestJobId,
        CancellationToken ct)
    {
        var tables = await app.ListProjectDataTablesAsync(projectId, ct);
        if (!tables.Any(item => item.Name == "customer_accounts"))
        {
            var columns = new[]
            {
                new ProjectColumnSpec("id", "uuid", true, true),
                new ProjectColumnSpec("customer_name", "text", true, false),
                new ProjectColumnSpec("email", "text", true, false),
                new ProjectColumnSpec("plan", "text", false, false),
                new ProjectColumnSpec("status", "text", false, false),
                new ProjectColumnSpec("monthly_value", "numeric", false, false),
                new ProjectColumnSpec("last_activity", "timestamptz", false, false),
            };
            await app.ImportCsvToProjectTableAsync(
                projectId,
                "customer_accounts",
                columns,
                [
                    ["11111111-1111-4111-8111-111111111111", "Northwind Labs", "ops@northwind.example", "Enterprise", "Active", "12400.00", "2026-08-10T01:25:00Z"],
                    ["22222222-2222-4222-8222-222222222222", "Acme Field Services", "hello@acme.example", "Growth", "Active", "4800.00", "2026-08-09T23:40:00Z"],
                    ["33333333-3333-4333-8333-333333333333", "Bluebird Studio", "team@bluebird.example", "Starter", "Needs attention", "950.00", "2026-08-05T04:15:00Z"],
                    ["44444444-4444-4444-8444-444444444444", "Summit Retail Group", "data@summit.example", "Enterprise", "Active", "18900.00", "2026-08-10T02:05:00Z"],
                ],
                createTable: true,
                ct);
        }

        var entities = await app.ListDataEntitiesAsync(projectId, ct);
        if (!entities.Any(item => item.Name == "Customer account"))
        {
            await app.SaveDataEntityAsync(new SaveDataEntityCommand(
                projectId,
                "Customer account",
                "customer_accounts",
                "customer_name",
                [],
                ["customer", "account", "demo"]), ct);
        }

        var mappings = await app.ListDataMappingsAsync(projectId, ct);
        if (!mappings.Any(item => item.JobId == ingestJobId && item.TargetTable == "customer_accounts"))
        {
            await app.SaveDataMappingAsync(new SaveDataMappingCommand(
                projectId,
                ingestJobId,
                "customer_accounts",
                "customers",
                [
                    new DataFieldDto("id", "id", "uuid"),
                    new DataFieldDto("name", "customer_name", "text"),
                    new DataFieldDto("email", "email", "text"),
                    new DataFieldDto("plan", "plan", "text"),
                    new DataFieldDto("status", "status", "text"),
                    new DataFieldDto("monthly_value", "monthly_value", "numeric"),
                    new DataFieldDto("last_activity", "last_activity", "timestamptz"),
                ]), ct);
        }

        if (!(await app.ListSavedQueriesAsync(projectId, ct)).Any(item => item.Name == "Active customer accounts"))
        {
            await app.SaveSavedQueryAsync(
                projectId,
                "Active customer accounts",
                "SELECT customer_name, plan, monthly_value, last_activity FROM customer_accounts WHERE status = 'Active' ORDER BY monthly_value DESC;",
                ct);
        }

        var charts = await app.ListProjectChartsAsync(projectId, ct);
        if (!charts.Any(item => item.TableName == "sql:Account value by customer"))
        {
            await app.SaveSqlChartAsync(
                projectId,
                "Account value by customer",
                "SELECT customer_name, monthly_value FROM customer_accounts ORDER BY monthly_value DESC",
                "bar",
                ct);
        }
        if (!charts.Any(item => item.TableName == "sql:Accounts by plan"))
        {
            await app.SaveSqlChartAsync(
                projectId,
                "Accounts by plan",
                "SELECT plan, count(*) AS accounts FROM customer_accounts GROUP BY plan ORDER BY accounts DESC",
                "pie",
                ct);
        }
    }
}

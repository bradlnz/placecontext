namespace PlaceContext.Host.Tests;

public sealed class RpcChainIngestionContractTests
{
    [Fact]
    public void Mcp_exposes_durable_idempotent_submission_and_polling_tools()
    {
        var tools = Source("src/PlaceContext.Host/Tools/PlaceContextTools.cs");

        Assert.Contains("Name = \"submit_job_chain\"", tools);
        Assert.Contains("Name = \"get_job_chain_submission\"", tools);
        Assert.Contains("idempotencyKey", tools);
        Assert.Contains("full-feasibility-report", tools);
        Assert.Contains("Provide exactly one of chainId or chainName", tools);
        Assert.Contains("IJobChainSubmissionQueue", tools);
        Assert.Contains("ListRunArtifactsAsync", tools);
    }

    [Fact]
    public void Submission_worker_is_replica_safe_and_does_not_duplicate_established_runs()
    {
        var worker = Source(
            "src/PlaceContext.Infrastructure/Scheduling/RpcChainSubmissionWorker.cs");

        Assert.Contains("FOR UPDATE SKIP LOCKED", worker);
        Assert.Contains("HeartbeatAt", worker);
        Assert.Contains("var existing = await LoadRunAsync", worker);
        Assert.Contains("if (existing is not null)", worker);
        Assert.Contains("IDataEncryptor.Purpose.RpcChainSubmission", worker);
        Assert.Contains("LoadSubmitterAsync", worker);
    }

    [Fact]
    public void Submission_schema_has_stable_receipts_and_tenant_scoped_idempotency()
    {
        var infrastructure = Source(
            "src/PlaceContext.Infrastructure/DependencyInjection.cs");
        var queue = Source(
            "src/PlaceContext.Infrastructure/Scheduling/DbJobChainSubmissionQueue.cs");

        Assert.Contains("CREATE TABLE IF NOT EXISTS rpc_chain_submissions", infrastructure);
        Assert.Contains("\"ChainRunId\" uuid NOT NULL UNIQUE", infrastructure);
        Assert.Contains("\"TenantId\", \"IdempotencyKey\"", infrastructure);
        Assert.Contains("ON CONFLICT (\"TenantId\", \"IdempotencyKey\")", queue);
        Assert.Contains("_encryptor.Protect(inputPayload", queue);
    }

    [Fact]
    public void Rpc_bearer_can_download_the_artifacts_returned_by_polling()
    {
        var artifacts = Source("src/PlaceContext.Host/Controllers/ArtifactsController.cs");
        var program = Source("src/PlaceContext.Host/Program.cs");

        Assert.Contains("AgentAuthenticationDefaults.SchemeName", artifacts);
        Assert.Contains("app.MapMcp(\"/mcp\")", program);
        Assert.Contains("AuthenticationSchemes = AgentAuthenticationDefaults.SchemeName", program);
        Assert.Contains("Permission.ArtifactsView", artifacts);
    }

    private static string Source(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlaceContext.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the PlaceContext repository root.");
    }
}

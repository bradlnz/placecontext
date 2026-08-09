using PlaceContext.App.Proxy;

namespace PlaceContext.App.Tests;

public sealed class MicroserviceProxyRouteCatalogTests
{
    [Fact]
    public void Catalog_contains_each_independently_hosted_service_route()
    {
        var routes = MicroserviceProxyRouteCatalog.All;

        Assert.Contains(routes, route => route.ServiceName == "AgentChat" && route.Matches("/api/agent-chat"));
        Assert.Contains(routes, route => route.ServiceName == "Agents" && route.Matches("/api/v1/agents"));
        Assert.Contains(routes, route => route.ServiceName == "Artifacts" && route.Matches("/api/artifacts"));
        Assert.Contains(routes, route => route.ServiceName == "Crm" && route.Matches("/api/crm"));
        Assert.Contains(routes, route => route.ServiceName == "Data" && route.Matches("/api/data"));
        Assert.Contains(routes, route => route.ServiceName == "Jobs" && route.Matches("/api/jobs"));
        Assert.Contains(routes, route => route.ServiceName == "Jobs" && route.Matches("/ingest/deploy.completed"));
        Assert.Contains(routes, route => route.ServiceName == "Identity" && route.Matches("/api/v1/identity/context"));
        Assert.Contains(routes, route => route.ServiceName == "Identity" && route.Matches("/mcp-oauth/start"));
        Assert.Contains(routes, route => route.ServiceName == "Mcp" && route.Matches("/api/mcp"));
        Assert.Contains(routes, route => route.ServiceName == "Mcp" && route.Matches("/api/job-mcp/project/tools"));
        Assert.Contains(routes, route => route.ServiceName == "Search" && route.Matches("/api/search"));
        Assert.Contains(routes, route => route.ServiceName == "Search" && route.Matches("/api/v1/search"));
        Assert.Contains(routes, route => route.ServiceName == "Vault" && route.Matches("/api/vault"));
    }

    [Fact]
    public void Catalog_claims_only_the_exact_legacy_search_route()
    {
        var route = Assert.Single(MicroserviceProxyRouteCatalog.All, candidate =>
            candidate.Matches("/api/v1/search"));

        Assert.Equal("Search", route.ServiceName);
        Assert.True(route.Matches("/api/v1/search/"));
        Assert.False(route.Matches("/api/v1/search/history"));
    }

    [Fact]
    public void Catalog_routes_the_legacy_opensearch_contract_to_search_and_jobs_to_jobs()
    {
        var route = Assert.Single(MicroserviceProxyRouteCatalog.All, candidate =>
            candidate.Matches("/api/v1/projects/8fbcc0ff-e259-4d9f-b9eb-c0ea064a3e46/opensearch/search"));

        Assert.Equal("Search", route.ServiceName);
        var jobsRoute = Assert.Single(MicroserviceProxyRouteCatalog.All, candidate =>
            candidate.Matches("/api/v1/projects/8fbcc0ff-e259-4d9f-b9eb-c0ea064a3e46/jobs"));
        Assert.Equal("Jobs", jobsRoute.ServiceName);
    }

    [Theory]
    [InlineData("/runs/98f44a9b-6067-4a17-8636-cc16b4d51c45/artifacts/8ae108e3-c13f-4b1c-9874-73b0dd144e1f")]
    [InlineData("/share/artifacts/pc_share_example")]
    [InlineData("/chat/attachments/chat/98f44a9b-6067-4a17-8636-cc16b4d51c45/project/session/file.txt")]
    public void Catalog_routes_artifact_download_contracts_to_artifacts(string path)
    {
        var route = Assert.Single(
            MicroserviceProxyRouteCatalog.All,
            candidate => candidate.Matches(path));

        Assert.Equal("Artifacts", route.ServiceName);
    }

    [Theory]
    [InlineData("/api/v1/jobs/98f44a9b-6067-4a17-8636-cc16b4d51c45")]
    [InlineData("/api/v1/schedules/98f44a9b-6067-4a17-8636-cc16b4d51c45")]
    [InlineData("/api/v1/job-runs/98f44a9b-6067-4a17-8636-cc16b4d51c45/cancel")]
    [InlineData("/api/v1/chain-runs/98f44a9b-6067-4a17-8636-cc16b4d51c45/cancel")]
    [InlineData("/api/v1/chains/98f44a9b-6067-4a17-8636-cc16b4d51c45/trigger")]
    [InlineData("/api/v1/chains/98f44a9b-6067-4a17-8636-cc16b4d51c45/replay")]
    public void Catalog_routes_job_operations_to_jobs(string path)
    {
        var route = Assert.Single(
            MicroserviceProxyRouteCatalog.All,
            candidate => candidate.Matches(path));

        Assert.Equal("Jobs", route.ServiceName);
    }

    [Theory]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/projects/")]
    [InlineData("/api/v1/projects/98f44a9b-6067-4a17-8636-cc16b4d51c45")]
    public void Catalog_routes_project_management_contracts_to_projects(string path)
    {
        var route = Assert.Single(
            MicroserviceProxyRouteCatalog.All,
            candidate => candidate.Matches(path));

        Assert.Equal("Projects", route.ServiceName);
    }

    [Theory]
    [InlineData("/api/v1/jobs/98f44a9b-6067-4a17-8636-cc16b4d51c45/history")]
    [InlineData("/api/v1/schedules/98f44a9b-6067-4a17-8636-cc16b4d51c45/history")]
    [InlineData("/api/v1/chains/98f44a9b-6067-4a17-8636-cc16b4d51c45/trigger/extra")]
    [InlineData("/api/v1/projects/98f44a9b-6067-4a17-8636-cc16b4d51c45/history")]
    public void Catalog_does_not_claim_unowned_nested_management_routes(string path)
        => Assert.DoesNotContain(MicroserviceProxyRouteCatalog.All, route => route.Matches(path));
}

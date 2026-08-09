using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Contracts.Api;
using PlaceContext.Crm.Controllers;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Tests;

public sealed class CustomerPortalControllerTests
{
    [Fact]
    public async Task Job_chain_contract_preserves_parameters_and_condition_expression()
    {
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var chain = new CrmJobChainSummary(
            Guid.NewGuid(),
            projectId,
            "Prepare report",
            1,
            "Customer-facing report",
            [new CrmJobChainStageSummary(
                [new CrmJobChainStageJobSummary(jobId, "Render PDF")],
                "exists:customer.email")]);
        var jobs = new StubJobsClient(new CrmJobsCatalog(
            [chain],
            [new CrmJobSummary(
                jobId,
                projectId,
                "Render PDF",
                null,
                "File",
                [new CrmJobParameterSummary(
                    "template",
                    "Template",
                    true,
                    "select",
                    ["summary", "full"])])]));
        var controller = Controller(jobs);

        var result = await controller.JobChains(projectId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.Single(
            Assert.IsAssignableFrom<IEnumerable<CustomerPortalJobChainResponse>>(ok.Value));
        var step = Assert.Single(response.Steps);
        Assert.Equal("exists:customer.email", step.ConditionExpression);
        var parameter = Assert.Single(step.Parameters);
        Assert.Equal("template", parameter.Name);
        Assert.Equal(["summary", "full"], parameter.Options);
    }

    [Fact]
    public async Task Artifact_download_requires_the_requested_client_association()
    {
        var artifact = Artifact(Guid.NewGuid());
        var storage = new StubArtifactsClient(new CrmArtifactContent(
            Convert.ToBase64String([1, 2, 3]),
            "application/pdf"));
        var controller = ArtifactController(artifact, storage);

        var result = await controller.Get(Guid.NewGuid(), artifact.Id);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(0, storage.ReadCount);
    }

    [Fact]
    public async Task Artifact_download_preserves_inline_preview_headers()
    {
        var clientId = Guid.NewGuid();
        var artifact = Artifact(clientId);
        var storage = new StubArtifactsClient(new CrmArtifactContent(
            Convert.ToBase64String([1, 2, 3]),
            "application/pdf"));
        var controller = ArtifactController(artifact, storage);

        var result = await controller.Get(clientId, artifact.Id);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("SAMEORIGIN", controller.Response.Headers["X-Frame-Options"]);
        Assert.StartsWith("inline;", controller.Response.Headers.ContentDisposition.ToString());
        Assert.Equal(1, storage.ReadCount);
    }

    private static CustomerPortalController Controller(StubJobsClient jobs)
        => new(
            new StubDispatcher(),
            new StubProjectsClient(),
            jobs,
            new StubCurrentTenant())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static CustomerPortalArtifactsController ArtifactController(
        CrmClientArtifact artifact,
        StubArtifactsClient storage)
        => new(new StubDispatcher(), new StubArtifactRepository(artifact), storage)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static CrmClientArtifact Artifact(Guid clientId)
        => CrmClientArtifact.CreateUpload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            clientId,
            "report.pdf",
            "reports",
            "crm/report.pdf",
            "application/pdf",
            3,
            DateTimeOffset.UtcNow);

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }

    private sealed class StubProjectsClient : ICrmProjectsClient
    {
        public Task<IReadOnlyList<CrmProjectSummary>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmProjectSummary>>([]);

        public Task<bool> ExistsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class StubJobsClient(CrmJobsCatalog catalog) : ICrmJobsClient
    {
        public Task<CrmJobsCatalog> GetCatalogAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(catalog);

        public Task<CrmJobChainRun> RunChainAsync(
            CrmRunJobChainRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CrmJobChainRun?> GetRunAsync(Guid chainRunId, CancellationToken ct = default)
            => Task.FromResult<CrmJobChainRun?>(null);
    }

    private sealed class StubDispatcher : IDispatcher
    {
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            object result = query switch
            {
                ListCrmClientArtifactsQuery => Array.Empty<CrmClientArtifactView>(),
                ListCrmClientsQuery => Array.Empty<CrmClientView>(),
                ListCrmClientAssignedJobChainsQuery => Array.Empty<Guid>(),
                _ => throw new NotSupportedException(query.GetType().Name),
            };
            return Task.FromResult((TResult)result);
        }
    }

    private sealed class StubArtifactRepository(CrmClientArtifact artifact)
        : ICrmClientArtifactRepository
    {
        public Task AddAsync(CrmClientArtifact value, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<CrmClientArtifact?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == artifact.Id ? artifact : null);

        public Task<bool> ExistsForSourceAsync(
            Guid clientId,
            Guid sourceArtifactId,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<CrmClientArtifact>> ListForClientAsync(
            Guid clientId,
            int take = 200,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmClientArtifact>>([]);

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubArtifactsClient(CrmArtifactContent? content) : ICrmArtifactsClient
    {
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<CrmRunArtifactSummary>> ListForRunAsync(
            Guid runId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmRunArtifactSummary>>([]);

        public Task<CrmStoredObject> StoreAsync(
            Guid projectId,
            Guid clientId,
            Guid objectId,
            byte[] value,
            string contentType,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CrmArtifactContent?> ReadAsync(
            string bucket,
            string objectKey,
            CancellationToken ct = default)
        {
            ReadCount++;
            return Task.FromResult(content);
        }

        public Task DeleteAsync(
            string bucket,
            string objectKey,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

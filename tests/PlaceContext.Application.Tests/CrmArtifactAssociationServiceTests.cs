using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public sealed class CrmArtifactAssociationServiceTests
{
    [Fact]
    public async Task Terminal_crm_chain_tags_pdf_artifact_to_customer_idempotently()
    {
        var projectId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var jobRunId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 3, 3, 0, 0, TimeSpan.Zero);
        var chain = JobChain.Create(projectId, "Generate customer report", null,
            new[] { jobId }, now);
        var run = ChainRun.Start(chain, new[] { "Generate PDF" }, now, crmClientId: clientId);
        run.MarkStepRunning(0, jobRunId, now);
        run.MarkStepFinished(0, jobRunId, ChainStepStatus.Succeeded, now.AddMinutes(1));
        run.Complete(ChainRunStatus.Succeeded, null, now.AddMinutes(1));

        var pdf = RunArtifactLink.Create(jobRunId, jobId, projectId,
            PostJobActionKind.HtmlReport, "Site feasibility report.pdf", "reports",
            "runs/report.pdf", "application/pdf", 12_345, now.AddMinutes(1));
        var runArtifacts = new RunArtifacts(pdf);
        var clientArtifacts = new ClientArtifacts();
        var uow = new RecordingUnitOfWork();
        var service = new CrmArtifactAssociationService(runArtifacts, clientArtifacts, uow);

        Assert.Equal(1, await service.AssociateAsync(run));
        Assert.Equal(0, await service.AssociateAsync(run));

        var tagged = Assert.Single(clientArtifacts.Values);
        Assert.Equal(clientId, tagged.ClientId);
        Assert.Equal(run.Id, tagged.ChainRunId);
        Assert.Equal(pdf.Id, tagged.SourceArtifactId);
        Assert.Equal("application/pdf", tagged.ContentType);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Waiting_crm_chain_is_not_reconciled_before_its_pdf_stage_finishes()
    {
        var projectId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chain = JobChain.Create(projectId, "Delayed report", null,
            new[] { new ChainStage(new[] { jobId }, new WaitGate(TimeSpan.FromMinutes(5))) }, now);
        var run = ChainRun.Start(chain, new[] { "Generate PDF" }, now, crmClientId: clientId);
        run.Pause(0, null, now.AddMinutes(5));
        var clientArtifacts = new ClientArtifacts();
        var service = new CrmArtifactAssociationService(
            new RunArtifacts(), clientArtifacts, new RecordingUnitOfWork());

        Assert.Equal(0, await service.AssociateAsync(run));
        Assert.Empty(clientArtifacts.Values);
    }

    private sealed class RunArtifacts(params RunArtifactLink[] values) : IRunArtifactLinkRepository
    {
        public Task AddAsync(RunArtifactLink link, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(values.Where(value => value.RunId == runId).ToList());
        public Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(values.FirstOrDefault(value => value.Id == id));
        public Task<IReadOnlyList<RunArtifactLink>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(values.Where(value => value.JobId == jobId).ToList());
        public Task<IReadOnlyList<RunArtifactLink>> ListRecentAsync(int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(values.Take(take).ToList());
        public Task<IReadOnlyList<RunArtifactLink>> ListForProjectAsync(Guid projectId, int take,
            string? search = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunArtifactLink>>(values.Where(value => value.ProjectId == projectId).Take(take).ToList());
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ClientArtifacts : ICrmClientArtifactRepository
    {
        public List<CrmClientArtifact> Values { get; } = new();
        public Task AddAsync(CrmClientArtifact artifact, CancellationToken ct = default)
        {
            Values.Add(artifact);
            return Task.CompletedTask;
        }
        public Task<CrmClientArtifact?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Values.FirstOrDefault(value => value.Id == id));
        public Task<bool> ExistsForSourceAsync(Guid clientId, Guid sourceArtifactId, CancellationToken ct = default)
            => Task.FromResult(Values.Any(value => value.ClientId == clientId
                && value.SourceArtifactId == sourceArtifactId));
        public Task<IReadOnlyList<CrmClientArtifact>> ListForClientAsync(Guid clientId, int take = 200,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrmClientArtifact>>(Values.Where(value => value.ClientId == clientId).Take(take).ToList());
        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            Values.RemoveAll(value => value.Id == id);
            return Task.CompletedTask;
        }
    }
}

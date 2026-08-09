using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Crm.Services;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class RunCrmClientAutomationHandler
    : ICommandHandler<RunCrmClientAutomationCommand, CrmChainRunView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmChainRunRepository _crmRuns;
    private readonly ICrmJobsClient _jobs;
    private readonly CrmArtifactAssociationService _artifactAssociation;
    private readonly ICrmUnitOfWork _uow;

    public RunCrmClientAutomationHandler(
        ICrmClientRepository clients,
        ICrmChainRunRepository crmRuns,
        ICrmJobsClient jobs,
        CrmArtifactAssociationService artifactAssociation,
        ICrmUnitOfWork uow)
        => (_clients, _crmRuns, _jobs, _artifactAssociation, _uow)
            = (clients, crmRuns, jobs, artifactAssociation, uow);

    public async Task<CrmChainRunView> HandleAsync(
        RunCrmClientAutomationCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var chain = (await _jobs.GetCatalogAsync(client.ProjectId, ct)).Chains
            .FirstOrDefault(candidate => candidate.Id == command.ChainId)
            ?? throw new InvalidOperationException($"Job chain {command.ChainId} not found.");
        if (chain.ProjectId != client.ProjectId)
            throw new InvalidOperationException("The job chain and client must belong to the same project.");

        var payload = JsonSerializer.Serialize(new
        {
            customer = new
            {
                id = client.Id,
                name = client.Name,
                company = client.Company,
                email = client.Email,
                phone = client.Phone,
                lifecycleStage = client.LifecycleStage.ToString(),
                notes = client.Notes,
            },
            lifecycle = client.LifecycleStage.ToString(),
        });

        var result = await _jobs.RunChainAsync(
            new CrmRunJobChainRequest(
                client.ProjectId,
                chain.Id,
                command.InputPayload ?? payload,
                StepPayloadOverrides: command.StepPayloadOverrides,
                CrmClientId: client.Id),
            ct);
        var link = await _crmRuns.GetByChainRunIdAsync(result.Id, ct);
        if (link is null)
        {
            link = CrmChainRun.Create(client.ProjectId, client.Id, chain.Id, result.Id,
                client.LifecycleStage, result.StartedAt);
            await _crmRuns.AddAsync(link, ct);
            await _uow.SaveChangesAsync(ct);
        }
        await _artifactAssociation.AssociateAsync(
            client.ProjectId,
            client.Id,
            result.Id,
            result.Steps.Select(step => step.RunId).OfType<Guid>(),
            ct);

        return new CrmChainRunView(link.Id, client.Id, chain.Id, result.ChainName, result.Id,
            link.LifecycleStage.ToString(), result.Status, result.StartedAt, result.FinishedAt);
    }
}

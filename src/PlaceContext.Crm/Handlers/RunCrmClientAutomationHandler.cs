using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class RunCrmClientAutomationHandler
    : ICommandHandler<RunCrmClientAutomationCommand, CrmChainRunView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmChainRunRepository _crmRuns;
    private readonly IJobChainRepository _chains;
    private readonly ICommandHandler<RunJobChainCommand, ChainRunView> _chainRunner;
    private readonly IRunArtifactLinkRepository _runArtifacts;
    private readonly ICrmClientArtifactRepository _clientArtifacts;
    private readonly ICrmUnitOfWork _uow;

    public RunCrmClientAutomationHandler(
        ICrmClientRepository clients,
        ICrmChainRunRepository crmRuns,
        IJobChainRepository chains,
        ICommandHandler<RunJobChainCommand, ChainRunView> chainRunner,
        IRunArtifactLinkRepository runArtifacts,
        ICrmClientArtifactRepository clientArtifacts,
        ICrmUnitOfWork uow)
        => (_clients, _crmRuns, _chains, _chainRunner, _runArtifacts, _clientArtifacts, _uow)
            = (clients, crmRuns, chains, chainRunner, runArtifacts, clientArtifacts, uow);

    public async Task<CrmChainRunView> HandleAsync(
        RunCrmClientAutomationCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
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

        var result = await _chainRunner.HandleAsync(
            new RunJobChainCommand(
                chain.Id,
                command.InputPayload ?? payload,
                StepPayloadOverrides: command.StepPayloadOverrides,
                CrmClientId: client.Id), ct);
        var link = CrmChainRun.Create(client.ProjectId, client.Id, chain.Id, result.Id,
            client.LifecycleStage, result.StartedAt);
        await _crmRuns.AddAsync(link, ct);

        foreach (var runId in result.Steps.Where(step => step.RunId is not null)
                     .Select(step => step.RunId!.Value).Distinct())
        {
            foreach (var artifact in await _runArtifacts.ListForRunAsync(runId, ct))
            {
                if (await _clientArtifacts.ExistsForSourceAsync(client.Id, artifact.Id, ct)) continue;
                await _clientArtifacts.AddAsync(CrmClientArtifact.CreateFromRunArtifact(
                    client.ProjectId, client.Id, artifact.Id, result.Id, artifact.Title,
                    artifact.Bucket, artifact.ObjectKey, artifact.ContentType,
                    artifact.SizeBytes, artifact.CreatedAt), ct);
            }
        }
        await _uow.SaveChangesAsync(ct);

        return new CrmChainRunView(link.Id, client.Id, chain.Id, result.ChainName, result.Id,
            link.LifecycleStage.ToString(), result.Status, result.StartedAt, result.FinishedAt);
    }
}

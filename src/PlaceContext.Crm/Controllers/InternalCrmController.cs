using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Crm.Contracts.Api;
using PlaceContext.Crm.Integration;
using PlaceContext.Crm.Services;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Crm.Controllers;

[ApiController]
[Route("api/crm/internal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalCrmController(
    ICrmClientRepository clients,
    ICrmChainRunRepository runs,
    ICrmUnitOfWork unitOfWork,
    CrmArtifactAssociationService artifacts) : ControllerBase
{
    [HttpGet("customers/{clientId:guid}")]
    public async Task<IActionResult> GetCustomer(Guid clientId, CancellationToken ct)
    {
        var client = await clients.GetByIdAsync(clientId, ct);
        return client is null
            ? NotFound()
            : Ok(new
            {
                client.Id,
                client.Name,
                client.Company,
                client.Email,
                client.Phone,
            });
    }

    [HttpPost("chain-runs/completed")]
    public async Task<IActionResult> CompleteChainRun(
        CrmChainCompletionRequest request,
        CancellationToken ct)
    {
        var client = await clients.GetByIdAsync(request.ClientId, ct);
        if (client is null || client.ProjectId != request.ProjectId) return NotFound();

        if (await runs.GetByChainRunIdAsync(request.ChainRunId, ct) is null)
        {
            await runs.AddAsync(CrmChainRun.Create(
                request.ProjectId,
                request.ClientId,
                request.ChainId,
                request.ChainRunId,
                client.LifecycleStage,
                request.StartedAt), ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        var added = await artifacts.AssociateAsync(
            request.ProjectId,
            request.ClientId,
            request.ChainRunId,
            request.RunIds,
            ct);
        return Ok(new { added });
    }
}

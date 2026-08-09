using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Contracts.Api;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Controllers;

[ApiController]
[Route("api/customer-portal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class CustomerPortalController(
    IDispatcher dispatcher,
    ICrmProjectsClient projects,
    ICrmJobsClient jobs,
    ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet("health")]
    public ActionResult<object> Health()
        => currentTenant.IsResolved
            ? Ok(new { status = "ok", tenantId = currentTenant.TenantId, enabled = true })
            : NotFound();

    [HttpGet("projects")]
    public async Task<IActionResult> Projects(CancellationToken ct)
        => Ok(await projects.ListAsync(ct));

    [HttpGet("clients")]
    public async Task<IActionResult> Clients([FromQuery] Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        return Ok(await dispatcher.Query(new ListCrmClientsQuery(projectId), ct));
    }

    [HttpGet("clients/{id:guid}")]
    public async Task<IActionResult> Client(
        Guid id,
        [FromQuery] Guid projectId,
        CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        var client = (await dispatcher.Query(new ListCrmClientsQuery(projectId), ct))
            .FirstOrDefault(candidate => candidate.Id == id);
        return client is null ? NotFound() : Ok(client);
    }

    [HttpPost("clients")]
    public async Task<IActionResult> Create(
        [FromBody] SaveCustomerPortalClientRequest request,
        CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("project_id and name are required.");
        var saved = await dispatcher.Send(request.ToCommand(), ct);
        return CreatedAtAction(
            nameof(Client),
            new { id = saved.Id, projectId = saved.ProjectId },
            saved);
    }

    [HttpPut("clients/{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] SaveCustomerPortalClientRequest request,
        CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("project_id and name are required.");
        return Ok(await dispatcher.Send(request.ToCommand(id), ct));
    }

    [HttpDelete("clients/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await dispatcher.Send(new DeleteCrmClientCommand(id), ct)
            ? NoContent()
            : NotFound();

    [HttpGet("job-chains")]
    public async Task<IActionResult> JobChains(
        [FromQuery] Guid projectId,
        CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        var catalog = await jobs.GetCatalogAsync(projectId, ct);
        return Ok(catalog.Chains.Select(chain => ToCustomerPortalView(chain, catalog.Jobs ?? [])));
    }

    [HttpGet("job-chains/{id:guid}")]
    public async Task<IActionResult> JobChain(
        Guid id,
        [FromQuery] Guid projectId,
        CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        var catalog = await jobs.GetCatalogAsync(projectId, ct);
        var chain = catalog.Chains.FirstOrDefault(candidate => candidate.Id == id);
        return chain is null
            ? NotFound()
            : Ok(ToCustomerPortalView(chain, catalog.Jobs ?? []));
    }

    [HttpPost("job-chains/{id:guid}/run")]
    public async Task<IActionResult> RunJobChain(
        Guid id,
        [FromBody] RunCustomerPortalJobChainRequest request,
        CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest("project_id is required.");

        var chain = (await jobs.GetCatalogAsync(request.ProjectId, ct)).Chains
            .FirstOrDefault(candidate => candidate.Id == id);
        if (chain is null) return NotFound();

        try
        {
            if (request.ClientId is not { } clientId || clientId == Guid.Empty)
                return BadRequest(new { error = "client_id is required for customer portal automations." });

            var clients = await dispatcher.Query(new ListCrmClientsQuery(request.ProjectId), ct);
            if (clients.All(client => client.Id != clientId))
                return BadRequest(new { error = "The selected client does not belong to this project." });

            var assignedChains = await dispatcher.Query(
                new ListCrmClientAssignedJobChainsQuery(clientId, request.ProjectId),
                ct);
            if (!assignedChains.Contains(id))
                return BadRequest(new { error = "This automation is not assigned to the selected client." });

            var crmRun = await dispatcher.Send(
                new RunCrmClientAutomationCommand(
                    clientId,
                    id,
                    request.InputPayload,
                    request.StepPayloadOverrides),
                ct);
            var run = await jobs.GetRunAsync(crmRun.ChainRunId, ct);
            if (run is null)
                return Problem("The automation started but its run could not be loaded.");
            return Ok(run);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("clients/{id:guid}/job-chains")]
    public async Task<IActionResult> ListClientAssignedJobChains(
        Guid id,
        [FromQuery] Guid projectId,
        CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        return Ok(await dispatcher.Query(
            new ListCrmClientAssignedJobChainsQuery(id, projectId),
            ct));
    }

    [HttpPut("clients/{id:guid}/job-chains")]
    public async Task<IActionResult> SetClientAssignedJobChains(
        Guid id,
        [FromQuery] Guid projectId,
        [FromBody] SetCustomerPortalClientJobChainsRequest request,
        CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        return Ok(await dispatcher.Send(
            new SetCrmClientAssignedJobChainsCommand(projectId, id, request.ChainIds),
            ct));
    }

    [HttpGet("chain-runs/{id:guid}")]
    public async Task<IActionResult> ChainRun(Guid id, CancellationToken ct)
        => await jobs.GetRunAsync(id, ct) is { } run ? Ok(run) : NotFound();

    private static CustomerPortalJobChainResponse ToCustomerPortalView(
        CrmJobChainSummary chain,
        IReadOnlyList<CrmJobSummary> jobs)
    {
        var jobById = jobs.ToDictionary(job => job.Id);
        var steps = new List<CustomerPortalJobChainStepResponse>(chain.StepCount);
        var index = 0;
        foreach (var stage in chain.Stages ?? [])
        {
            foreach (var job in stage.Jobs)
            {
                var parameters = jobById.TryGetValue(job.JobId, out var definition)
                    ? definition.Parameters.Select(parameter => new CustomerPortalJobParameterResponse(
                        parameter.Name,
                        parameter.Label,
                        parameter.Required,
                        parameter.Type,
                        parameter.Options)).ToList()
                    : [];
                steps.Add(new CustomerPortalJobChainStepResponse(
                    index++,
                    job.JobId,
                    job.JobName,
                    parameters,
                    stage.ConditionExpression));
            }
        }

        return new CustomerPortalJobChainResponse(
            chain.Id,
            chain.ProjectId,
            chain.Name,
            chain.Description,
            steps);
    }
}

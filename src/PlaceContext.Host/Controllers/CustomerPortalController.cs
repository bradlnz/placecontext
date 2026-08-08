using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/customer-portal")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
public sealed class CustomerPortalController : ControllerBase
{
    private readonly IPlaceContextService _service;
    private readonly ITenantStore _tenants;

    public CustomerPortalController(IPlaceContextService service, ITenantStore tenants)
        => (_service, _tenants) = (service, tenants);

    [HttpGet("health")]
    public async Task<ActionResult<object>> Health(CancellationToken ct)
    {
        var tenant = await _tenants.GetRowAsync(CurrentTenantId(), ct);
        return tenant is null
            ? NotFound()
            : Ok(new { status = "ok", tenantId = tenant.Id, enabled = tenant.CustomerPortalEnabled });
    }

    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<ProjectSummaryView>>> Projects(CancellationToken ct)
        => Ok(await _service.GetProjectsAsync(ct));

    [HttpGet("clients")]
    public async Task<ActionResult<IReadOnlyList<CrmClientView>>> Clients([FromQuery] Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        return Ok(await _service.ListCrmClientsAsync(projectId, ct));
    }

    [HttpGet("clients/{id:guid}")]
    public async Task<ActionResult<CrmClientView>> Client(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        var client = (await _service.ListCrmClientsAsync(projectId, ct)).FirstOrDefault(x => x.Id == id);
        return client is null ? NotFound() : Ok(client);
    }

    [HttpPost("clients")]
    public async Task<ActionResult<CrmClientView>> Create([FromBody] SaveClientRequest request, CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("project_id and name are required.");
        var saved = await _service.SaveCrmClientAsync(request.ToCommand(), ct);
        return CreatedAtAction(nameof(Client), new { id = saved.Id, projectId = saved.ProjectId }, saved);
    }

    [HttpPut("clients/{id:guid}")]
    public async Task<ActionResult<CrmClientView>> Update(Guid id, [FromBody] SaveClientRequest request, CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("project_id and name are required.");
        var saved = await _service.SaveCrmClientAsync(request.ToCommand(id), ct);
        return Ok(saved);
    }

    [HttpDelete("clients/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await _service.DeleteCrmClientAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet("job-chains")]
    public async Task<ActionResult<IReadOnlyList<CustomerPortalJobChainView>>> JobChains([FromQuery] Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        var chains = await _service.ListJobChainsAsync(projectId, ct);
        var jobs = await _service.ListJobsAsync(projectId, ct);
        return Ok(chains.Select(chain => ToCustomerPortalView(chain, jobs)).ToList());
    }

    [HttpGet("job-chains/{id:guid}")]
    public async Task<ActionResult<CustomerPortalJobChainView>> JobChain(Guid id, [FromQuery] Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        var chain = (await _service.ListJobChainsAsync(projectId, ct)).FirstOrDefault(x => x.Id == id);
        if (chain is null) return NotFound();
        var jobs = await _service.ListJobsAsync(projectId, ct);
        return Ok(ToCustomerPortalView(chain, jobs));
    }

    [HttpPost("job-chains/{id:guid}/run")]
    public async Task<ActionResult<ChainRunView>> RunJobChain(
        Guid id,
        [FromBody] RunJobChainRequest request,
        CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty)
            return BadRequest("project_id is required.");

        var chain = (await _service.ListJobChainsAsync(request.ProjectId, ct)).FirstOrDefault(x => x.Id == id);
        if (chain is null) return NotFound();

        try
        {
            if (request.ClientId is not { } clientId || clientId == Guid.Empty)
                return BadRequest(new { error = "client_id is required for customer portal automations." });

            var clients = await _service.ListCrmClientsAsync(request.ProjectId, ct);
            if (clients.All(client => client.Id != clientId))
                return BadRequest(new { error = "The selected client does not belong to this project." });

            var assignedChains = await _service.ListCrmClientAssignedJobChainIdsAsync(
                clientId,
                request.ProjectId,
                ct);
            if (!assignedChains.Contains(id))
                return BadRequest(new { error = "This automation is not assigned to the selected client." });

            var crmRun = await _service.RunCrmClientAutomationAsync(
                clientId,
                id,
                request.InputPayload,
                request.StepPayloadOverrides,
                ct);
            var run = await _service.GetChainRunAsync(crmRun.ChainRunId, ct);
            if (run is null)
                return Problem("The automation started but its run could not be loaded.");
            return Ok(run);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("clients/{id:guid}/job-chains")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> ListClientAssignedJobChains(
        Guid id,
        [FromQuery] Guid projectId,
        CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        return Ok(await _service.ListCrmClientAssignedJobChainIdsAsync(id, projectId, ct));
    }

    [HttpPut("clients/{id:guid}/job-chains")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> SetClientAssignedJobChains(
        Guid id,
        [FromQuery] Guid projectId,
        [FromBody] SetClientJobChainsRequest request,
        CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest("project_id is required.");
        return Ok(await _service.SetCrmClientAssignedJobChainIdsAsync(
            id,
            projectId,
            request.ChainIds,
            ct));
    }

    [HttpGet("chain-runs/{id:guid}")]
    public async Task<ActionResult<ChainRunView>> ChainRun(Guid id, CancellationToken ct)
    {
        var run = await _service.GetChainRunAsync(id, ct);
        return run is null ? NotFound() : Ok(run);
    }

    private Guid CurrentTenantId()
        => Guid.TryParse(Request.Headers["X-PlaceContext-Tenant-Id"], out var id) ? id : Guid.Empty;

    private static CustomerPortalJobChainView ToCustomerPortalView(JobChainView chain, IReadOnlyList<JobView> jobs)
    {
        var jobById = jobs.ToDictionary(j => j.Id);
        var steps = new List<CustomerPortalJobChainStepView>(chain.Steps.Count);
        var flatIndex = 0;
        foreach (var stage in chain.Stages)
        {
            foreach (var job in stage.Jobs)
            {
                var parameters = jobById.TryGetValue(job.JobId, out var jobView)
                    ? jobView.Parameters
                    : Array.Empty<JobParameterDto>();
                steps.Add(new CustomerPortalJobChainStepView(
                    flatIndex,
                    job.JobId,
                    job.JobName,
                    parameters,
                    // Flatten stage gates into each step for simple customer forms.
                    stage.Gate is ConditionGateView condition
                        ? condition.Expression
                        : null));
                flatIndex++;
            }
        }

        return new CustomerPortalJobChainView(
            chain.Id,
            chain.ProjectId,
            chain.Name,
            chain.Description,
            steps);
    }

}

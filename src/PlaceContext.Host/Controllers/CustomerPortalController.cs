using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
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
            var run = await _service.RunJobChainAsync(
                id,
                inputPayload: request.InputPayload,
                stepPayloadOverrides: request.StepPayloadOverrides,
                ct: ct);
            return Ok(run);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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

    public sealed record SaveClientRequest(
        Guid ProjectId,
        string Name,
        string? Company,
        string? Email,
        string? Phone,
        CustomerLifecycleStage LifecycleStage,
        string? Notes)
    {
        public SaveCrmClientCommand ToCommand(Guid? id = null)
            => new(ProjectId, Name, Company, Email, Phone, LifecycleStage, Notes, id);
    }

    public sealed record RunJobChainRequest(
        Guid ProjectId,
        string? InputPayload = null,
        IReadOnlyDictionary<int, string>? StepPayloadOverrides = null);

    public sealed record CustomerPortalJobChainView(
        Guid Id,
        Guid ProjectId,
        string Name,
        string? Description,
        IReadOnlyList<CustomerPortalJobChainStepView> Steps);

    public sealed record CustomerPortalJobChainStepView(
        int Index,
        Guid JobId,
        string JobName,
        IReadOnlyList<JobParameterDto> Parameters,
        string? ConditionExpression);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Management;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Produces("application/json")]
public sealed class JobOperationsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("jobs/{id:guid}", Name = "GetJobById")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<JobResponse>> GetJob(Guid id, CancellationToken ct)
    {
        var job = await dispatcher.Query(new GetJobQuery(id), ct);
        return job is null ? NotFound() : Ok(JobApiMapper.ToResponse(job));
    }

    [HttpPut("jobs/{id:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> UpdateJob(
        Guid id,
        [FromBody] JobRequest request,
        CancellationToken ct)
    {
        if (await dispatcher.Query(new GetJobQuery(id), ct) is null)
            return NotFound();

        try
        {
            var updated = await dispatcher.Send(JobApiMapper.ToUpdateCommand(id, request), ct);
            return Ok(JobApiMapper.ToResponse(updated));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("jobs/{id:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken ct)
        => await dispatcher.Send(new DeleteJobCommand(id), ct) ? NoContent() : NotFound();

    [HttpPost("job-runs/{runId:guid}/cancel")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<bool>> CancelJobRun(Guid runId, CancellationToken ct)
        => Ok(await dispatcher.Send(new CancelJobRunCommand(runId), ct));

    [HttpPost("chain-runs/{chainRunId:guid}/cancel")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<bool>> CancelChainRun(Guid chainRunId, CancellationToken ct)
        => Ok(await dispatcher.Send(new CancelChainRunCommand(chainRunId), ct));

    [HttpPost("chains/{chainId:guid}/trigger")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> TriggerChain(
        Guid chainId,
        [FromBody] TriggerChainRequest? request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await dispatcher.Send(
                new RunJobChainCommand(chainId, request?.InputPayload),
                ct));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("chains/{chainId:guid}/replay")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> ReplayChain(
        Guid chainId,
        [FromBody] ReplayChainRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await dispatcher.Send(new ReplayJobChainCommand(
                chainId,
                request.OriginalRunId,
                request.FromStepIndex,
                request.InputPayload,
                request.StepPayloadOverrides), ct));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}

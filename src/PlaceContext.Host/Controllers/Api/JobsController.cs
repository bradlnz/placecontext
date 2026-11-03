using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Access;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Application;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

/// <summary>
/// Management API: job definitions — the machine-facing contract the Terraform provider declares
/// generic map/reduce jobs against. See docs/management-api.md. Authenticated via the ApiKey scheme only
/// (see <see cref="ApiKeyAuthenticationHandler"/>), gated by the jobs.* fine-grained permissions, and
/// implicitly tenant-scoped by <c>TenantResolutionMiddleware</c> + EF's global query filter.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class JobsController : ControllerBase
{
    private readonly PlaceContextService _svc;
    private readonly IJobChainSubmissionQueue? _submissions;
    private readonly bool _managedHosting;
    public JobsController(PlaceContextService svc, IJobChainSubmissionQueue? submissions = null,
        IOptions<RunBillingDeploymentOptions>? hosting = null)
    {
        _svc = svc;
        _submissions = submissions;
        _managedHosting = hosting?.Value.ManagedHosting ?? false;
    }

    /// <summary>GET /api/v1/projects/{projectId}/jobs — every job defined under the project, or 404 if
    /// the project itself doesn't exist.</summary>
    [HttpGet("projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<JobResponse>>> ListForProject(Guid projectId)
    {
        var ct = HttpContext.RequestAborted;
        if (await _svc.GetProjectByIdAsync(projectId, ct) is null)
        {
            return NotFound();
        }
        var jobs = await _svc.ListJobsAsync(projectId, ct);
        return Ok(jobs.Select(JobDefinitionMapper.ToResponse).ToList());
    }

    /// <summary>GET /api/v1/jobs/{id} — a single job definition, or 404.</summary>
    [HttpGet("jobs/{id:guid}", Name = "GetJobById")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<JobResponse>> GetById(Guid id)
    {
        var job = await _svc.GetJobAsync(id, HttpContext.RequestAborted);
        return job is null ? NotFound() : Ok(JobDefinitionMapper.ToResponse(job));
    }

    /// <summary>POST /api/v1/projects/{projectId}/jobs — creates a job definition under the project.
    /// 404 if the project doesn't exist; 400 on an invalid workload spec (e.g. neither image nor code
    /// source, or both).</summary>
    [HttpPost("projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Create(Guid projectId, [FromBody] JobRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (await _svc.GetProjectByIdAsync(projectId, ct) is null)
        {
            return NotFound();
        }
        try
        {
            var job = await _svc.CreateJobAsync(JobDefinitionMapper.ToCreateCommand(projectId, request), ct);
            var response = JobDefinitionMapper.ToResponse(job);
            return CreatedAtRoute("GetJobById", new
            {
                id = response.Id
            }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    /// <summary>PUT /api/v1/jobs/{id} — replaces a job definition's configuration. 404 if the job doesn't
    /// exist; 400 on an invalid workload spec.</summary>
    [HttpPut("jobs/{id:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Update(Guid id, [FromBody] JobRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (await _svc.GetJobAsync(id, ct) is null)
        {
            return NotFound();
        }
        try
        {
            var job = await _svc.UpdateJobAsync(JobDefinitionMapper.ToUpdateCommand(id, request), ct);
            return Ok(JobDefinitionMapper.ToResponse(job));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }

    /// <summary>DELETE /api/v1/jobs/{id} — permanently removes a job definition. Does not cascade to its
    /// run history, artifacts, data mappings, or triggers (see <c>DeleteJobCommand</c>); delete those
    /// first if a clean slate is required. 204 on success, 404 if it didn't exist.</summary>
    [HttpDelete("jobs/{id:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _svc.DeleteJobAsync(id, HttpContext.RequestAborted);
        return deleted ? NoContent() : NotFound();
    }

    // ── Job Run Cancel ─────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/job-runs/{runId}/cancel — cancels a running job run: kills its workload
    /// containers/pods and marks the run as Cancelled. Idempotent — safe to call on a run that
    /// already reached a terminal state. Returns 200 with true/false.
    /// </summary>
    [HttpPost("job-runs/{runId:guid}/cancel")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<bool>> CancelJobRun(Guid runId)
    {
        var ct = HttpContext.RequestAborted;
        return Ok(await _svc.CancelJobRunAsync(runId, ct));
    }

    // ── Chain Run Cancel ──────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/workflow-runs/{chainRunId}/cancel — cancels a running chain run: cancels every
    /// still-running step job run and marks the chain run as Cancelled. Idempotent.
    /// </summary>
    [HttpPost("workflow-runs/{chainRunId:guid}/cancel")]
    [HttpPost("chain-runs/{chainRunId:guid}/cancel")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<bool>> CancelChainRun(Guid chainRunId)
    {
        var ct = HttpContext.RequestAborted;
        return Ok(await _svc.CancelChainRunAsync(chainRunId, ct));
    }

    // ── Chain Trigger ────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/workflows/{chainId}/trigger — start a fresh chain run. The chain executes from
    /// the first stage, using the supplied input payload (or the first job's stored shard payloads
    /// when null). Managed hosting returns a durable 202 receipt; self-host returns the run view.
    /// </summary>
    [HttpPost("workflows/{chainId:guid}/trigger")]
    [HttpPost("chains/{chainId:guid}/trigger")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> TriggerChain(
        Guid chainId,
        [FromBody] TriggerChainRequest? request = null,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (_managedHosting)
            {
                if (_submissions is null)
                {
                    return StatusCode(503, new
                    {
                        error = "Managed workflow queue is not configured."
                    });
                }
                var chain = await _svc.GetJobChainAsync(chainId, ct);
                if (chain is null)
                {
                    return NotFound(new
                    {
                        error = "Workflow not found."
                    });
                }
                var receipt = await _submissions.EnqueueAsync(chain.ProjectId, chainId,
                                    request?.InputPayload, idempotencyKey, ct);
                return AcceptedAtAction(nameof(GetWorkflowSubmission),
                    new
                    {
                        trackingId = receipt.TrackingId
                    }, receipt);
            }
            var result = await _svc.RunJobChainAsync(
                chainId,
                inputPayload: request?.InputPayload,
                ct: ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
    }

    [HttpGet("workflow-submissions/{trackingId:guid}")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<IActionResult> GetWorkflowSubmission(Guid trackingId)
    {
        if (_submissions is null)
        {
            return NotFound();
        }
        var receipt = await _submissions.GetAsync(trackingId, HttpContext.RequestAborted);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    // ── Chain Replay ────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/workflows/{chainId}/replay — replay a failed/partial chain run from the
    /// first failed step. The original run is preserved; a new run is created that re-executes
    /// from the failure point.
    /// </summary>
    [HttpPost("workflows/{chainId:guid}/replay")]
    [HttpPost("chains/{chainId:guid}/replay")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> ReplayChain(
        Guid chainId,
        [FromBody] ReplayChainRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (_managedHosting)
            {
                if (_submissions is null)
                {
                    return StatusCode(503, new
                    {
                        error = "Managed workflow queue is not configured."
                    });
                }
                var chain = await _svc.GetJobChainAsync(chainId, ct);
                var original = await _svc.GetChainRunAsync(request.OriginalRunId, ct);
                if (chain is null || original is null || original.ChainId != chainId
                    || original.ProjectId != chain.ProjectId)
                {
                    return NotFound(new
                    {
                        error = "Original workflow run not found."
                    });
                }
                if (request.FromStepIndex is { } step && (step < 0 || step >= original.Steps.Count))
                {
                    return BadRequest(new
                    {
                        error = "Replay step index is out of range."
                    });
                }
                var receipt = await _submissions.EnqueueAsync(chain.ProjectId, chainId,
                                    request.InputPayload,
                                    idempotencyKey: "replay:" + request.OriginalRunId.ToString("N")
                                        + ":" + (string.IsNullOrWhiteSpace(idempotencyKey)
                                            ? Guid.NewGuid().ToString("N") : idempotencyKey.Trim()),
                                    ct: ct, stepPayloadOverrides: request.StepPayloadOverrides,
                                    replayOriginalRunId: request.OriginalRunId,
                                    replayFromStepIndex: request.FromStepIndex);
                return AcceptedAtAction(nameof(GetWorkflowSubmission),
                    new
                    {
                        trackingId = receipt.TrackingId
                    }, receipt);
            }
            var result = await _svc.ReplayJobChainAsync(
                new PlaceContext.Application.Features.ReplayJobChainCommand(
                    chainId, request.OriginalRunId, request.FromStepIndex,
                    request.InputPayload, request.StepPayloadOverrides), ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }
}

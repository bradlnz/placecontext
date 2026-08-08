using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/chain-page")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.ChainsManage)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class JobChainsPageController(IPlaceContextService placeContextService, IPermissionService permissionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobChainsPageResponse>> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var jobsTask = placeContextService.ListJobsAsync(projectId, cancellationToken);
        var chainsTask = placeContextService.ListJobChainsAsync(projectId, cancellationToken);
        var emailTask = permissionService.HasAsync(Permission.EmailSend, cancellationToken);
        var smsTask = permissionService.HasAsync(Permission.SmsSend, cancellationToken);
        await Task.WhenAll(jobsTask, chainsTask, emailTask, smsTask);
        return Ok(new JobChainsPageResponse(
            (await jobsTask).Select(job => new JobChainJobResponse(job.Id, job.Name)).ToList(),
            (await chainsTask).Select(MapChain).ToList(), await emailTask, await smsTask));
    }

    [HttpPost("chains")]
    public Task<ActionResult<JobChainResponse>> Create(Guid projectId, [FromBody] SaveJobChainPageRequest request, CancellationToken cancellationToken) => Save(projectId, null, request, cancellationToken);

    [HttpPut("chains/{chainId:guid}")]
    public Task<ActionResult<JobChainResponse>> Update(Guid projectId, Guid chainId, [FromBody] SaveJobChainPageRequest request, CancellationToken cancellationToken) => Save(projectId, chainId, request, cancellationToken);

    [HttpDelete("chains/{chainId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid chainId, CancellationToken cancellationToken)
    {
        var chain = (await placeContextService.ListJobChainsAsync(projectId, cancellationToken)).FirstOrDefault(item => item.Id == chainId);
        if (chain is null) return NotFound(new { error = "The chain does not exist." });
        return await placeContextService.DeleteJobChainAsync(chainId, cancellationToken) ? NoContent() : NotFound(new { error = "The chain does not exist." });
    }

    [HttpPost("chains/{chainId:guid}/runs")]
    public async Task<ActionResult<JobChainRunResponse>> Run(Guid projectId, Guid chainId, [FromBody] RunJobChainPageRequest? request, CancellationToken cancellationToken)
    {
        var chain = (await placeContextService.ListJobChainsAsync(projectId, cancellationToken)).FirstOrDefault(item => item.Id == chainId);
        if (chain is null) return NotFound(new { error = "The chain does not exist." });
        try { return Ok(MapRun(await placeContextService.RunJobChainAsync(chainId, request?.InputPayload, stepPayloadOverrides: request?.StepPayloadOverrides, ct: cancellationToken))); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("chains/{chainId:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<JobChainRunResponse>>> Runs(Guid projectId, Guid chainId, CancellationToken cancellationToken)
    {
        var chain = (await placeContextService.ListJobChainsAsync(projectId, cancellationToken)).FirstOrDefault(item => item.Id == chainId);
        return chain is null ? NotFound(new { error = "The chain does not exist." }) : Ok((await placeContextService.ListChainRunsAsync(chainId, ct: cancellationToken)).Select(MapRun).ToList());
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid projectId, Guid runId, CancellationToken cancellationToken)
    {
        var run = await FindRun(projectId, runId, cancellationToken);
        if (run is null) return NotFound(new { error = "The chain run does not exist." });
        await placeContextService.CancelChainRunAsync(runId, cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult<JobChainResponse>> Save(Guid projectId, Guid? chainId, SaveJobChainPageRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });
        if (request.Stages.Count == 0 || request.Stages.All(stage => stage.JobIds.Count == 0 && stage.Action is null)) return BadRequest(new { error = "Add at least one step." });
        if (request.Stages.Any(stage => stage.Action?.Type == SendEmailChainAction.ActionType) && !await permissionService.HasAsync(Permission.EmailSend, cancellationToken)) return Forbid();
        if (request.Stages.Any(stage => stage.Action?.Type == SendSmsChainAction.ActionType) && !await permissionService.HasAsync(Permission.SmsSend, cancellationToken)) return Forbid();
        try
        {
            var stages = request.Stages.Select(stage => (IReadOnlyList<Guid>)stage.JobIds.ToList()).ToList();
            var gates = request.Stages.Select(stage => MapGate(stage.Gate)).ToList();
            var actions = request.Stages.Select(stage => MapAction(stage.Action)).ToList();
            var flat = stages.SelectMany(stage => stage).ToList();
            var chain = chainId is { } id
                ? await placeContextService.UpdateJobChainAsync(id, request.Name.Trim(), Clean(request.Description), flat, stages, gates, actions, cancellationToken)
                : await placeContextService.CreateJobChainAsync(projectId, request.Name.Trim(), Clean(request.Description), flat, stages, gates, actions, cancellationToken);
            return Ok(MapChain(chain));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private async Task<ChainRunView?> FindRun(Guid projectId, Guid runId, CancellationToken cancellationToken)
    {
        foreach (var chain in await placeContextService.ListJobChainsAsync(projectId, cancellationToken))
        {
            var run = (await placeContextService.ListChainRunsAsync(chain.Id, ct: cancellationToken)).FirstOrDefault(item => item.Id == runId);
            if (run is not null) return run;
        }
        return null;
    }

    private static JobChainResponse MapChain(JobChainView chain) => new(chain.Id, chain.ProjectId, chain.Name, chain.Description, chain.Stages.Select(stage => new JobChainStageResponse(stage.Jobs.Select(job => new JobChainJobResponse(job.JobId, job.JobName)).ToList(), MapGate(stage.Gate), MapAction(stage.Action))).ToList(), chain.UpdatedAt, chain.UpdatedAt.ToWorkspaceTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
    private static JobChainGateResponse? MapGate(ChainGateView? gate) => gate switch { WaitGateView wait => new("wait", wait.DurationSeconds, null), ConditionGateView condition => new("condition", null, condition.Expression), _ => null };
    private static JobChainActionResponse? MapAction(ChainActionView? action) => action switch { SendEmailChainActionView email => new(email.Type, email.DisplayName, email.Recipient, email.RecipientName, email.Subject, email.Body, email.AttachmentPath), SendSmsChainActionView sms => new(sms.Type, sms.DisplayName, sms.Recipient, null, null, sms.Body, null), _ => null };
    private static ChainGate? MapGate(JobChainGateResponse? gate) => gate?.Type switch { "wait" => new WaitGate(TimeSpan.FromSeconds(gate.DurationSeconds ?? 30)), "condition" => new ConditionGate(gate.Expression ?? "exists:data"), _ => null };
    private static ChainAction? MapAction(JobChainActionResponse? action) => action?.Type switch { SendEmailChainAction.ActionType => new SendEmailChainAction(action.Recipient ?? "", action.RecipientName ?? "", action.Subject ?? "", action.Body ?? "", action.AttachmentPath ?? ""), SendSmsChainAction.ActionType => new SendSmsChainAction(action.Recipient ?? "", action.Body ?? ""), _ => null };
    private static JobChainRunResponse MapRun(ChainRunView run) => new(run.Id, run.ChainId, run.ChainName, run.Status, run.Steps.Select(step => new JobChainRunStepResponse(step.Index, step.StageIndex, step.BranchIndex, step.JobId, step.JobName, step.RunId, step.Status, step.StartedAt, step.FinishedAt, step.ActionType, step.Provider, step.ExternalId, step.Error)).ToList(), run.FinalOutput, run.StartedAt, run.FinishedAt, run.StartedAt.ToWorkspaceTime().ToString("MMM d · HH:mm", CultureInfo.InvariantCulture), run.FinishedAt is { } end ? $"{(end - run.StartedAt).TotalSeconds:0.0} s" : null);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Contracts.Api;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs/projects/{projectId:guid}/chain-page")]
[Authorize(Policy = Permission.ChainsManage)]
[Produces("application/json")]
public sealed class JobChainsPageController(IDispatcher dispatcher, ICurrentTenant currentTenant)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobChainsPageResponse>> Get(Guid projectId, CancellationToken ct)
    {
        var jobsTask = dispatcher.Query(new ListJobsQuery(projectId), ct);
        var chainsTask = dispatcher.Query(new ListJobChainsQuery(projectId), ct);
        await Task.WhenAll(jobsTask, chainsTask);
        return Ok(new JobChainsPageResponse(
            (await jobsTask).Select(job => new JobChainJobResponse(job.Id, job.Name)).ToList(),
            (await chainsTask).Select(MapChain).ToList(),
            User.HasClaim("permission", Permission.EmailSend),
            User.HasClaim("permission", Permission.SmsSend)));
    }

    [HttpPost("chains")]
    public Task<ActionResult<JobChainResponse>> Create(
        Guid projectId,
        SaveJobChainPageRequest request,
        CancellationToken ct) => Save(projectId, null, request, ct);

    [HttpPut("chains/{chainId:guid}")]
    public Task<ActionResult<JobChainResponse>> Update(
        Guid projectId,
        Guid chainId,
        SaveJobChainPageRequest request,
        CancellationToken ct) => Save(projectId, chainId, request, ct);

    [HttpDelete("chains/{chainId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid chainId, CancellationToken ct)
    {
        if (!await ChainBelongsToProject(chainId, projectId, ct))
            return NotFound(new { error = "The chain does not exist." });
        return await dispatcher.Send(new DeleteJobChainCommand(chainId), ct)
            ? NoContent()
            : NotFound(new { error = "The chain does not exist." });
    }

    [HttpPost("chains/{chainId:guid}/runs")]
    public async Task<ActionResult<JobChainRunResponse>> Run(
        Guid projectId,
        Guid chainId,
        RunJobChainPageRequest? request,
        CancellationToken ct)
    {
        if (!await ChainBelongsToProject(chainId, projectId, ct))
            return NotFound(new { error = "The chain does not exist." });
        try
        {
            return Ok(MapRun(await dispatcher.Send(new RunJobChainCommand(
                chainId,
                request?.InputPayload,
                StepPayloadOverrides: request?.StepPayloadOverrides), ct)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("chains/{chainId:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<JobChainRunResponse>>> Runs(
        Guid projectId,
        Guid chainId,
        CancellationToken ct)
    {
        if (!await ChainBelongsToProject(chainId, projectId, ct))
            return NotFound(new { error = "The chain does not exist." });
        return Ok((await dispatcher.Query(new ListChainRunsQuery(chainId), ct))
            .Select(MapRun)
            .ToList());
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid projectId, Guid runId, CancellationToken ct)
    {
        var run = await dispatcher.Query(new GetChainRunQuery(runId), ct);
        if (run is null || !await ChainBelongsToProject(run.ChainId, projectId, ct))
            return NotFound(new { error = "The chain run does not exist." });
        await dispatcher.Send(new CancelChainRunCommand(runId), ct);
        return NoContent();
    }

    private async Task<ActionResult<JobChainResponse>> Save(
        Guid projectId,
        Guid? chainId,
        SaveJobChainPageRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });
        if (request.Stages.Count == 0
            || request.Stages.All(stage => stage.JobIds.Count == 0 && stage.Action is null))
            return BadRequest(new { error = "Add at least one step." });
        if (request.Stages.Any(stage => stage.Action?.Type == SendEmailChainAction.ActionType)
            && !User.HasClaim("permission", Permission.EmailSend))
            return Forbid();
        if (request.Stages.Any(stage => stage.Action?.Type == SendSmsChainAction.ActionType)
            && !User.HasClaim("permission", Permission.SmsSend))
            return Forbid();

        try
        {
            var stages = request.Stages
                .Select(stage => (IReadOnlyList<Guid>)stage.JobIds.ToList())
                .ToList();
            var gates = request.Stages.Select(stage => MapGate(stage.Gate)).ToList();
            var actions = request.Stages.Select(stage => MapAction(stage.Action)).ToList();
            var flat = stages.SelectMany(stage => stage).ToList();
            var chain = chainId is { } id
                ? await dispatcher.Send(new UpdateJobChainCommand(
                    id, request.Name.Trim(), Clean(request.Description), flat, stages, gates, actions), ct)
                : await dispatcher.Send(new CreateJobChainCommand(
                    projectId, request.Name.Trim(), Clean(request.Description), flat, stages, gates, actions), ct);
            return Ok(MapChain(chain));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private async Task<bool> ChainBelongsToProject(Guid chainId, Guid projectId, CancellationToken ct)
        => (await dispatcher.Query(new ListJobChainsQuery(projectId), ct)).Any(item => item.Id == chainId);

    private JobChainResponse MapChain(JobChainView chain) => new(
        chain.Id,
        chain.ProjectId,
        chain.Name,
        chain.Description,
        chain.Stages.Select(stage => new JobChainStageResponse(
            stage.Jobs.Select(job => new JobChainJobResponse(job.JobId, job.JobName)).ToList(),
            MapGate(stage.Gate),
            MapAction(stage.Action))).ToList(),
        chain.UpdatedAt,
        WorkspaceTime(chain.UpdatedAt).ToString("MMM d, yyyy", CultureInfo.InvariantCulture));

    private static JobChainGateResponse? MapGate(ChainGateView? gate) => gate switch
    {
        WaitGateView wait => new("wait", wait.DurationSeconds, null),
        ConditionGateView condition => new("condition", null, condition.Expression),
        _ => null,
    };

    private static JobChainActionResponse? MapAction(ChainActionView? action) => action switch
    {
        SendEmailChainActionView email => new(
            email.Type, email.DisplayName, email.Recipient, email.RecipientName,
            email.Subject, email.Body, email.AttachmentPath),
        SendSmsChainActionView sms => new(
            sms.Type, sms.DisplayName, sms.Recipient, null, null, sms.Body, null),
        _ => null,
    };

    private static ChainGate? MapGate(JobChainGateResponse? gate) => gate?.Type switch
    {
        "wait" => new WaitGate(TimeSpan.FromSeconds(gate.DurationSeconds ?? 30)),
        "condition" => new ConditionGate(gate.Expression ?? "exists:data"),
        _ => null,
    };

    private static ChainAction? MapAction(JobChainActionResponse? action) => action?.Type switch
    {
        SendEmailChainAction.ActionType => new SendEmailChainAction(
            action.Recipient ?? string.Empty,
            action.RecipientName ?? string.Empty,
            action.Subject ?? string.Empty,
            action.Body ?? string.Empty,
            action.AttachmentPath ?? string.Empty),
        SendSmsChainAction.ActionType => new SendSmsChainAction(
            action.Recipient ?? string.Empty,
            action.Body ?? string.Empty),
        _ => null,
    };

    private JobChainRunResponse MapRun(ChainRunView run) => new(
        run.Id,
        run.ChainId,
        run.ChainName,
        run.Status,
        run.Steps.Select(step => new JobChainRunStepResponse(
            step.Index,
            step.StageIndex,
            step.BranchIndex,
            step.JobId,
            step.JobName,
            step.RunId,
            step.Status,
            step.StartedAt,
            step.FinishedAt,
            step.ActionType,
            step.Provider,
            step.ExternalId,
            step.Error)).ToList(),
        run.FinalOutput,
        run.StartedAt,
        run.FinishedAt,
        WorkspaceTime(run.StartedAt).ToString("MMM d · HH:mm", CultureInfo.InvariantCulture),
        run.FinishedAt is { } end ? $"{(end - run.StartedAt).TotalSeconds:0.0} s" : null);

    private DateTimeOffset WorkspaceTime(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(
            value,
            TimeZoneInfo.FindSystemTimeZoneById(currentTenant.TimeZoneId));

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

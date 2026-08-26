using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.CoreApi;
using PlaceContext.Host.Wiki;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// User-scoped REST surface for the native desktop client. Unlike the machine-oriented Core API,
/// these routes accept an OAuth PKCE access token and enforce the signed-in member's permissions.
/// </summary>
[ApiController]
[Route("api/desktop")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "DesktopApi")]
[Produces("application/json")]
public sealed class DesktopApiController : ControllerBase
{
    private readonly IPlaceContextService _service;
    private readonly ICoreApiResourceResolver _resources;
    private readonly ICurrentTenant _tenant;

    public DesktopApiController(
        IPlaceContextService service,
        ICoreApiResourceResolver resources,
        ICurrentTenant tenant)
    {
        _service = service;
        _resources = resources;
        _tenant = tenant;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        ok = true,
        api = "desktop",
        tenant = new { resolved = _tenant.IsResolved, id = _tenant.TenantId, slug = _tenant.Slug },
        userId = User.FindFirst("sub")?.Value,
        role = User.FindFirst("role")?.Value,
        issuedAt = DateTimeOffset.UtcNow,
    });

    [HttpGet("v1/projects")]
    [Authorize(Policy = Permission.ProjectsView)]
    public async Task<ActionResult<IReadOnlyList<CoreProjectResponse>>> ListProjects()
    {
        var projects = await _service.GetProjectsAsync(HttpContext.RequestAborted);
        return Ok(projects.Select(CoreApiMapper.ToResponse).ToList());
    }

    [HttpGet("v1/projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<CoreJobSummaryResponse>>> ListJobs(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });

        var jobs = await _service.ListJobsAsync(projectId, HttpContext.RequestAborted);
        return Ok(jobs.Select(CoreApiMapper.ToSummary).ToList());
    }

    [HttpGet("v1/projects/{projectId:guid}/jobs/{jobId:guid}")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<JobResponse>> GetJobDefinition(Guid projectId, Guid jobId)
    {
        var job = await _service.GetJobAsync(jobId, HttpContext.RequestAborted);
        return job is null || job.ProjectId != projectId
            ? NotFound(new { error = "Job not found in this project." })
            : Ok(JobApiMapper.ToResponse(job));
    }

    [HttpPost("v1/projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> CreateJobDefinition(Guid projectId, [FromBody] JobRequest request)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        try
        {
            var job = await _service.CreateJobAsync(
                JobApiMapper.ToCreateCommand(projectId, request), HttpContext.RequestAborted);
            return Ok(JobApiMapper.ToResponse(job));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("v1/projects/{projectId:guid}/jobs/{jobId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> UpdateJobDefinition(
        Guid projectId,
        Guid jobId,
        [FromBody] JobRequest request)
    {
        var existing = await _service.GetJobAsync(jobId, HttpContext.RequestAborted);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound(new { error = "Job not found in this project." });
        try
        {
            var job = await _service.UpdateJobAsync(
                JobApiMapper.ToUpdateCommand(jobId, request), HttpContext.RequestAborted);
            return Ok(JobApiMapper.ToResponse(job));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("v1/projects/{projectId:guid}/jobs/{jobId:guid}/runs")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<CoreJobRunSummaryResponse>>> ListRuns(
        Guid projectId,
        Guid jobId,
        [FromQuery] int take = 10)
    {
        if (await _resources.GetJobAsync(projectId, jobId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Job not found in this project." });

        var limit = Math.Clamp(take, 1, 100);
        var runs = await _service.ListJobRunsAsync(jobId, HttpContext.RequestAborted);
        return Ok(runs.Take(limit).Select(CoreApiMapper.ToResponse).ToList());
    }

    [HttpPost("v1/projects/{projectId:guid}/jobs/{jobId:guid}/run")]
    [Authorize(Policy = Permission.JobsRun)]
    public async Task<ActionResult<DesktopActionResponse>> RunJob(
        Guid projectId,
        Guid jobId,
        [FromBody] DesktopRunRequest? request)
    {
        if (await _resources.GetJobAsync(projectId, jobId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Job not found in this project." });
        var run = await _service.RunJobAsync(jobId, request?.InputPayload, null, HttpContext.RequestAborted);
        return Ok(new DesktopActionResponse(
            run.Status,
            "Job run completed.",
            run.Id,
            run.ShardResults.Select(shard => new DesktopRunShardResponse(
                shard.Index, shard.ExitCode, shard.Outcome, shard.Artifact, shard.Log)).ToList()));
    }

    [HttpGet("v1/projects/{projectId:guid}/tests")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListTests(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var tests = await _service.ListJobTestCasesAsync(projectId, HttpContext.RequestAborted);
        return Ok(tests.Select(test => new DesktopResourceItemResponse(
            test.Id, projectId, "test", test.Name, test.JobName,
            test.LastRunAt?.ToLocalTime().ToString("g") ?? test.AssertionType.ToString(),
            test.Enabled ? test.LastStatus : "Disabled")).ToList());
    }

    [HttpPost("v1/projects/{projectId:guid}/tests/{testId:guid}/run")]
    [Authorize(Policy = Permission.JobsRun)]
    public async Task<ActionResult<DesktopActionResponse>> RunTest(Guid projectId, Guid testId)
    {
        var test = await _service.GetJobTestCaseAsync(testId, HttpContext.RequestAborted);
        if (test is null || test.ProjectId != projectId)
            return NotFound(new { error = "Test not found in this project." });
        var result = await _service.RunJobTestCaseAsync(testId, HttpContext.RequestAborted);
        return Ok(new DesktopActionResponse(result.LastStatus, result.LastMessage ?? "Test completed.", result.LastJobRunId));
    }

    [HttpGet("v1/projects/{projectId:guid}/chains")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListChains(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var chains = await _service.ListJobChainsAsync(projectId, HttpContext.RequestAborted);
        return Ok(chains.Select(chain => new DesktopResourceItemResponse(
            chain.Id, projectId, "chain", chain.Name, chain.Description ?? "Job pipeline",
            $"{chain.Stages.Count} stages · {chain.Steps.Count} jobs", "Ready")).ToList());
    }

    [HttpPost("v1/projects/{projectId:guid}/chains/{chainId:guid}/run")]
    [Authorize(Policy = Permission.JobsRun)]
    public async Task<ActionResult<DesktopActionResponse>> RunChain(
        Guid projectId,
        Guid chainId,
        [FromBody] DesktopRunRequest? request)
    {
        var chain = (await _service.ListJobChainsAsync(projectId, HttpContext.RequestAborted))
            .FirstOrDefault(value => value.Id == chainId);
        if (chain is null)
            return NotFound(new { error = "Chain not found in this project." });
        var run = await _service.RunJobChainAsync(chainId, request?.InputPayload, null, null, HttpContext.RequestAborted);
        return Ok(new DesktopActionResponse(run.Status, "Chain run started.", run.Id));
    }

    [HttpGet("v1/projects/{projectId:guid}/schedules")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListSchedules(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var triggers = await _service.ListTriggersAsync(projectId, HttpContext.RequestAborted);
        return Ok(triggers.Select(trigger => new DesktopResourceItemResponse(
            trigger.Id, projectId, "schedule", trigger.Name,
            trigger.Kind == "Schedule" ? trigger.CronExpression ?? "Schedule" : trigger.EventName ?? trigger.Kind,
            trigger.NextRunAt?.ToLocalTime().ToString("g") ?? trigger.Kind,
            trigger.Enabled ? "Enabled" : "Disabled")).ToList());
    }

    [HttpPost("v1/projects/{projectId:guid}/schedules/{triggerId:guid}/enabled")]
    [Authorize(Policy = Permission.TriggersManage)]
    public async Task<ActionResult<DesktopActionResponse>> SetScheduleEnabled(
        Guid projectId,
        Guid triggerId,
        [FromBody] DesktopScheduleEnabledRequest request)
    {
        var trigger = await _service.GetTriggerAsync(triggerId, HttpContext.RequestAborted);
        if (trigger is null || trigger.ProjectId != projectId)
            return NotFound(new { error = "Schedule not found in this project." });
        var updated = await _service.SetTriggerEnabledAsync(triggerId, request.Enabled, HttpContext.RequestAborted);
        return Ok(new DesktopActionResponse(
            updated.Enabled ? "Enabled" : "Disabled",
            updated.Enabled ? "Schedule enabled." : "Schedule disabled.",
            null));
    }

    [HttpGet("v1/projects/{projectId:guid}/data-resources")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListDataResources(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var items = new List<DesktopResourceItemResponse>();
        var tables = await _service.ListProjectDataTablesAsync(projectId, HttpContext.RequestAborted);
        items.AddRange(tables.Select(table => new DesktopResourceItemResponse(
            null, projectId, table.IsView ? "view" : "table", table.Name,
            $"{table.RowEstimate:N0} estimated rows", table.IsView ? "SQL view" : "Project table",
            table.ReadOnly ? "Read only" : "Writable")));
        var entities = await _service.ListDataEntitiesAsync(projectId, HttpContext.RequestAborted);
        items.AddRange(entities.Select(entity => new DesktopResourceItemResponse(
            entity.Id, projectId, "entity", entity.Name, entity.TableName,
            $"{entity.Relations.Count} relations · {entity.Tags.Count} tags", "Entity")));
        var mappings = await _service.ListDataMappingsAsync(projectId, HttpContext.RequestAborted);
        items.AddRange(mappings.Select(mapping => new DesktopResourceItemResponse(
            mapping.Id, projectId, "mapping", mapping.JobName, $"Maps to {mapping.TargetTable}",
            $"{mapping.Fields.Count} fields", mapping.Enabled ? "Enabled" : "Disabled")));
        var charts = await _service.ListProjectChartsAsync(projectId, HttpContext.RequestAborted);
        items.AddRange(charts.Select(chart => new DesktopResourceItemResponse(
            null, projectId, "chart", chart.TableName, "Saved project analytics chart",
            chart.GeneratedAt.ToLocalTime().ToString("g"), "Chart")));
        return Ok(items);
    }

    [HttpPost("v1/projects/{projectId:guid}/data/query")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<ActionResult<DesktopQueryResponse>> QueryData(
        Guid projectId,
        [FromBody] DesktopQueryRequest request)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        try
        {
            var sql = PlaceContext.Application.Features.SaveProjectViewHandler.EnsureSelectOnly(request.Sql);
            var result = await _service.ExecuteProjectDataAsync(projectId, sql, HttpContext.RequestAborted);
            return Ok(new DesktopQueryResponse(result.Columns, result.Rows.Take(500).ToList(), result.AffectedRows,
                result.Truncated || result.Rows.Count > 500));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("v1/projects/{projectId:guid}/secrets")]
    [Authorize(Policy = Permission.SecretsManage)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListSecrets(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var secrets = await _service.ListProjectSecretsAsync(projectId, HttpContext.RequestAborted);
        return Ok(secrets.Select(secret => new DesktopResourceItemResponse(
            null, projectId, "secret", secret.Name, "Encrypted project secret",
            secret.CreatedAt.ToLocalTime().ToString("g"), "Stored")).ToList());
    }

    [HttpGet("v1/projects/{projectId:guid}/agents")]
    [Authorize(Policy = Permission.AgentsChat)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListAgents(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var agents = await _service.ListAgentDefinitionsAsync(projectId, HttpContext.RequestAborted);
        return Ok(agents.Select(agent => new DesktopResourceItemResponse(
            agent.Id, projectId, "agent", agent.Name, agent.Description,
            $"{agent.Kind} · {agent.Capabilities.Count} capabilities",
            agent.Enabled ? "Enabled" : "Disabled")).ToList());
    }

    [HttpGet("v1/projects/{projectId:guid}/agent-chats")]
    [Authorize(Policy = Permission.AgentsChat)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListAgentChats(Guid projectId)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var sessions = await _service.ListAgentChatSessionsAsync(projectId, HttpContext.RequestAborted);
        return Ok(sessions.Select(session => new DesktopResourceItemResponse(
            session.Id, projectId, "chat", session.Title ?? "Agent chat", $"{session.Messages.Count} messages",
            session.UpdatedAt.ToLocalTime().ToString("g"), "Session")).ToList());
    }

    [HttpGet("v1/projects/{projectId:guid}/agent-chats/{sessionId:guid}")]
    [Authorize(Policy = Permission.AgentsChat)]
    public async Task<ActionResult<DesktopChatSessionResponse>> GetAgentChat(Guid projectId, Guid sessionId)
    {
        var session = await _service.GetAgentChatSessionAsync(sessionId, HttpContext.RequestAborted);
        if (session is null || session.ProjectId != projectId)
            return NotFound(new { error = "Agent chat not found in this project." });
        return Ok(ToChatResponse(session));
    }

    [HttpPost("v1/projects/{projectId:guid}/agent-chats/messages")]
    [Authorize(Policy = Permission.AgentsChat)]
    public async Task<ActionResult<DesktopChatSessionResponse>> SendAgentMessage(
        Guid projectId,
        [FromBody] DesktopAgentMessageRequest request)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "A message is required." });
        if (request.SessionId is { } sessionId)
        {
            var existing = await _service.GetAgentChatSessionAsync(sessionId, HttpContext.RequestAborted);
            if (existing is null || existing.ProjectId != projectId)
                return NotFound(new { error = "Agent chat not found in this project." });
        }
        var session = await _service.SendAgentMessageAsync(
            new PlaceContext.Application.Features.SendAgentMessageCommand(projectId, request.SessionId, request.Message.Trim()),
            HttpContext.RequestAborted);
        return Ok(ToChatResponse(session));
    }

    [HttpGet("v1/projects/{projectId:guid}/artifacts")]
    [Authorize(Policy = Permission.ArtifactsView)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListArtifacts(Guid projectId, [FromQuery] int take = 200)
    {
        if (await _resources.GetProjectAsync(projectId, HttpContext.RequestAborted) is null)
            return NotFound(new { error = "Project not found." });
        var artifacts = await _service.ListProjectArtifactsAsync(projectId, Math.Clamp(take, 1, 1000), null, HttpContext.RequestAborted);
        return Ok(artifacts.Select(artifact => new DesktopResourceItemResponse(
            artifact.Id, projectId, "artifact", artifact.Title, artifact.ContentType,
            FormatBytes(artifact.SizeBytes), artifact.Kind)).ToList());
    }

    [HttpGet("v1/observability")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListObservability([FromQuery] int take = 50)
    {
        var limit = Math.Clamp(take, 1, 100);
        var items = new List<DesktopResourceItemResponse>();
        var runs = await _service.ListRecentRunReportsAsync(limit, HttpContext.RequestAborted);
        items.AddRange(runs.Select(report => new DesktopResourceItemResponse(
            report.Run.Id, report.Run.ProjectId, "job run", report.JobName, report.ProjectName,
            report.Run.StartedAt.ToLocalTime().ToString("g"), report.Run.Status)));
        var chains = await _service.ListRecentChainRunsAsync(limit, HttpContext.RequestAborted);
        items.AddRange(chains.Select(report => new DesktopResourceItemResponse(
            report.Run.Id, report.ProjectId, "chain run", report.Run.ChainName, report.ProjectName,
            report.Run.StartedAt.ToLocalTime().ToString("g"), report.Run.Status)));
        return Ok(items.OrderByDescending(item => item.Meta).Take(limit));
    }

    [HttpGet("v1/cluster")]
    public async Task<ActionResult<IReadOnlyList<DesktopResourceItemResponse>>> ListCluster()
    {
        var cluster = await _service.GetClusterInfoAsync(HttpContext.RequestAborted);
        return Ok(cluster.Nodes.Select(node => new DesktopResourceItemResponse(
            null, null, "node", node.Name, $"{node.OperatingSystem} · {node.Architecture}",
            string.Join(", ", node.Roles), node.Ready ? "Ready" : "Not ready")).ToList());
    }

    [HttpGet("v1/wiki")]
    public ActionResult<IReadOnlyList<DesktopResourceItemResponse>> ListWiki() => Ok(
        WikiLibrary.Articles.Select(article => new DesktopResourceItemResponse(
            null, null, "wiki", article.Title, article.Summary, article.Slug, "Documentation")).ToList());

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024L * 1024L => $"{bytes / (1024d * 1024d * 1024d):0.##} GB",
        >= 1024L * 1024L => $"{bytes / (1024d * 1024d):0.##} MB",
        >= 1024L => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B",
    };

    private static DesktopChatSessionResponse ToChatResponse(PlaceContext.Application.Dtos.AgentChatSessionView session) =>
        new(
            session.Id,
            session.ProjectId,
            session.Title ?? "Agent chat",
            session.Messages.Select(message => new DesktopChatMessageResponse(
                message.Role, message.Content, message.Timestamp)).ToList(),
            session.UpdatedAt);
}

public sealed record DesktopResourceItemResponse(
    Guid? Id,
    Guid? ProjectId,
    string Kind,
    string Title,
    string Detail,
    string Meta,
    string Status);

public sealed record DesktopRunRequest(string? InputPayload);
public sealed record DesktopScheduleEnabledRequest(bool Enabled);
public sealed record DesktopActionResponse(
    string Status,
    string Message,
    Guid? RunId,
    IReadOnlyList<DesktopRunShardResponse>? Shards = null);
public sealed record DesktopRunShardResponse(
    int Index,
    int ExitCode,
    string Outcome,
    string? Artifact,
    string? Log);
public sealed record DesktopQueryRequest(string Sql);
public sealed record DesktopQueryResponse(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int AffectedRows,
    bool Truncated);
public sealed record DesktopAgentMessageRequest(Guid? SessionId, string Message);
public sealed record DesktopChatMessageResponse(string Role, string Content, DateTimeOffset Timestamp);
public sealed record DesktopChatSessionResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    IReadOnlyList<DesktopChatMessageResponse> Messages,
    DateTimeOffset UpdatedAt);

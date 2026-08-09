using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Api;
using PlaceContext.Data.Helpers;
using PlaceContext.Data.Integration;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
[Produces("application/json")]
public sealed class EntitiesController(
    IDispatcher dispatcher,
    IDataJobsClient jobs,
    IDataProjectsClient projects) : ControllerBase
{
    private static readonly Regex EntityNameRe = new(
        @"^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [HttpGet("entities")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<ActionResult<IReadOnlyList<EntityApiResponse>>> ListEntities()
    {
        if (await ResolveProjectId() is not { } projectId) return ProjectRequired();
        var entities = await dispatcher.Query(
            new ListDataEntitiesQuery(projectId), HttpContext.RequestAborted);
        return Ok(entities.Select(EntityApiMapper.ToResponse).ToList());
    }

    [HttpGet("{entityName}")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<IActionResult> ListRecords(
        string entityName,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!IsEntityName(entityName)) return NotFound();
        if (await ResolveProjectId() is not { } projectId) return ProjectRequired();
        var entity = await FindEntity(projectId, entityName);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}' in this project." });

        var result = await dispatcher.Query(new QueryProjectTablePageQuery(
            entity.ProjectId, entity.TableName, search, page, pageSize), HttpContext.RequestAborted);
        return Ok(EntityApiMapper.ToRecords(entity, result));
    }

    [HttpPost("{entityName}/jobs/{jobId:guid}/run")]
    [Authorize(Policy = Permission.JobsRun)]
    public async Task<ActionResult<JsonElement>> RunJob(
        string entityName,
        Guid jobId,
        [FromBody] EntityRunJobRequest? request = null)
    {
        if (!IsEntityName(entityName)) return NotFound();
        if (await ResolveProjectId() is not { } projectId) return ProjectRequired();
        var entity = await FindEntity(projectId, entityName);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}' in this project." });

        var job = (await jobs.GetCatalogAsync(entity.ProjectId, HttpContext.RequestAborted)).Jobs
            .FirstOrDefault(candidate => candidate.Id == jobId);
        if (job is null || job.ProjectId != entity.ProjectId)
            return NotFound(new { error = $"No job '{jobId}' for project '{entityName}'." });
        if (!job.AllowApiInvocation)
            return StatusCode(403, new { error = $"Job '{job.Name}' is not enabled for API invocation." });

        return Ok(await jobs.RunAsync(jobId, new DataJobRunRequest(
            entity.ProjectId, request?.InputPayload, request?.RunId), HttpContext.RequestAborted));
    }

    [HttpPost("{jobName}")]
    [Authorize(Policy = Permission.JobsRun)]
    public async Task<ActionResult<JsonElement>> RunJobByName(
        string jobName,
        [FromBody] EntityRunJobRequest? request = null)
    {
        if (!IsEntityName(jobName)) return NotFound();
        if (await ResolveProjectId() is not { } projectId) return ProjectRequired();
        var job = (await jobs.GetCatalogAsync(projectId, HttpContext.RequestAborted)).Jobs
            .FirstOrDefault(candidate => JobNameMatches(candidate, jobName));
        if (job is null)
            return NotFound(new { error = $"No job '{jobName}' for this project." });
        if (!job.AllowApiInvocation)
            return StatusCode(403, new { error = $"Job '{job.Name}' is not enabled for API invocation." });

        return Ok(await jobs.RunAsync(job.Id, new DataJobRunRequest(
            projectId, request?.InputPayload, request?.RunId), HttpContext.RequestAborted));
    }

    [HttpGet("{entityName}/{key}")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<IActionResult> GetByKey(string entityName, string key)
    {
        if (!IsEntityName(entityName)) return NotFound();
        if (await ResolveProjectId() is not { } projectId) return ProjectRequired();
        var entity = await FindEntity(projectId, entityName);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}' in this project." });

        var result = await dispatcher.Query(new QueryProjectTablePageQuery(
            entity.ProjectId, entity.TableName, key, 1, 50), HttpContext.RequestAborted);
        var labelIndex = ResolveLabelIndex(entity, result.Columns);
        var matches = result.Rows
            .Where(row => labelIndex >= 0 && labelIndex < row.Count
                && string.Equals(row[labelIndex], key, StringComparison.OrdinalIgnoreCase))
            .Select(row => (IReadOnlyList<string?>)row.ToList())
            .ToList();
        if (matches.Count == 0)
            return NotFound(new { error = $"No '{entity.Name}' row with key '{key}'." });

        return Ok(new EntityRecordsResponse(
            entity.Id, entity.Name, entity.TableName, result.Columns.ToList(), matches,
            matches.Count, 1, matches.Count));
    }

    public static bool EntityNameMatches(DataEntityView entity, string entityName) =>
        string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entity.TableName, entityName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProjectDataReservedNames.Slug(entity.Name), entityName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProjectDataReservedNames.Slug(entity.TableName), entityName, StringComparison.OrdinalIgnoreCase);

    public static bool JobNameMatches(DataJobSummary job, string jobName) =>
        string.Equals(job.Name, jobName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProjectDataReservedNames.Slug(job.Name), jobName, StringComparison.OrdinalIgnoreCase);

    public static string Slug(string raw) => ProjectDataReservedNames.Slug(raw);

    private static bool IsEntityName(string name) =>
        !ProjectDataReservedNames.IsReserved(name) && EntityNameRe.IsMatch(name);

    private async Task<DataEntityView?> FindEntity(Guid projectId, string name) =>
        (await dispatcher.Query(new ListDataEntitiesQuery(projectId), HttpContext.RequestAborted))
            .FirstOrDefault(entity => EntityNameMatches(entity, name));

    private async Task<Guid?> ResolveProjectId()
    {
        var idRaw = HeaderOrQuery("X-Project-Id", "projectId");
        var nameRaw = HeaderOrQuery("X-Project", "project");
        var all = await projects.ListAsync(HttpContext.RequestAborted);
        if (idRaw is not null && Guid.TryParse(idRaw, out var id))
            return all.Any(project => project.Id == id) ? id : null;
        if (nameRaw is null) return null;
        if (Guid.TryParse(nameRaw, out var nameAsId))
            return all.Any(project => project.Id == nameAsId) ? nameAsId : null;
        return all.FirstOrDefault(project =>
            string.Equals(project.Name, nameRaw, StringComparison.OrdinalIgnoreCase)
            || string.Equals(project.Path, nameRaw, StringComparison.OrdinalIgnoreCase)
            || project.Path.EndsWith("/" + nameRaw, StringComparison.OrdinalIgnoreCase)
            || project.Path.EndsWith("\\" + nameRaw, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private string? HeaderOrQuery(string header, string query)
    {
        var value = Request.Headers[header].ToString();
        if (string.IsNullOrWhiteSpace(value)) value = Request.Query[query].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private BadRequestObjectResult ProjectRequired() => BadRequest(new
    {
        error = "No project resolved. Pass X-Project-Id (GUID) or X-Project (name) on the request.",
    });

    private static int ResolveLabelIndex(DataEntityView entity, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0) return -1;
        if (!string.IsNullOrWhiteSpace(entity.LabelColumn))
            for (var index = 0; index < columns.Count; index++)
                if (string.Equals(columns[index], entity.LabelColumn, StringComparison.OrdinalIgnoreCase))
                    return index;
        return 0;
    }
}

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

/// <summary>
/// Entity data API — one REST collection per registered data entity, scoped to the project resolved
/// by <c>ProjectResolutionMiddleware</c> (<c>X-Project-Id</c> / <c>X-Project</c>).
/// <list type="bullet">
///   <item><c>GET /api/v1/entities</c> — registry for the current project</item>
///   <item><c>GET /api/v1/{entity-name}</c> — paginated rows for that entity</item>
///   <item><c>GET /api/v1/{entity-name}/{key}</c> — rows matching the label column (or first column)</item>
/// </list>
/// Auth: personal user API tokens or the workspace admin key. Requires <c>data.read</c>.
/// Route segments reserved by the management API (<c>projects</c>, <c>jobs</c>, <c>schedules</c>,
/// <c>entities</c>, <c>search</c>)
/// never match as entity names.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes =
    UserApiTokenAuthenticationHandler.SchemeName + "," + ApiKeyAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class EntitiesController : ControllerBase
{
    private static readonly Regex EntityNameRe = new(
        @"^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IPlaceContextService _svc;
    private readonly ICurrentProject _project;

    public EntitiesController(IPlaceContextService svc, ICurrentProject project)
    {
        _svc = svc;
        _project = project;
    }

    /// <summary>GET /api/v1/entities — entity registry for the project resolved by middleware.</summary>
    [HttpGet("entities")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<ActionResult<IReadOnlyList<EntityApiResponse>>> ListEntities()
    {
        if (RequireProject() is { } err) return err;
        var entities = await _svc.ListDataEntitiesAsync(_project.ProjectId!.Value, HttpContext.RequestAborted);
        return Ok(entities.Select(EntityApiMapper.ToResponse).ToList());
    }

    /// <summary>
    /// GET /api/v1/{entity-name} — page of rows for the named entity (name, table name, or slug).
    /// Query: <c>search</c>, <c>page</c>, <c>pageSize</c>.
    /// </summary>
    [HttpGet("{entityName}")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<IActionResult> ListRecords(
        string entityName,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (ProjectDataReservedNames.IsReserved(entityName) || !EntityNameRe.IsMatch(entityName))
            return NotFound();
        if (RequireProject() is { } err) return err;

        var entity = await FindEntityByNameAsync(entityName);
        if (entity is null) return NotFound(new { error = $"Unknown entity '{entityName}' in this project." });

        var result = await _svc.QueryProjectTablePageAsync(
            entity.ProjectId, entity.TableName, search, page, pageSize, ct: HttpContext.RequestAborted);

        return Ok(EntityApiMapper.ToRecords(entity, result));
    }

    /// <summary>
    /// GET /api/v1/{entity-name}/{key} — rows whose label column (or first column) equals <paramref name="key"/>.
    /// </summary>
    [HttpGet("{entityName}/{key}")]
    [Authorize(Policy = Permission.DataRead)]
    public async Task<IActionResult> GetByKey(string entityName, string key)
    {
        if (ProjectDataReservedNames.IsReserved(entityName) || !EntityNameRe.IsMatch(entityName))
            return NotFound();
        if (RequireProject() is { } err) return err;

        var entity = await FindEntityByNameAsync(entityName);
        if (entity is null) return NotFound(new { error = $"Unknown entity '{entityName}' in this project." });

        // Pull a page filtered by the key via the store's search (ILIKE across columns), then
        // narrow to an exact match on the label/first column client-side. Good enough for keys that
        // are unique-ish; avoids building ad-hoc SQL with untrusted identifiers beyond the table name.
        var result = await _svc.QueryProjectTablePageAsync(
            entity.ProjectId, entity.TableName, key, page: 1, pageSize: 50, ct: HttpContext.RequestAborted);

        var labelIdx = ResolveLabelIndex(entity, result.Columns);
        var matches = result.Rows
            .Where(r => labelIdx >= 0 && labelIdx < r.Count
                        && string.Equals(r[labelIdx], key, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.ToList())
            .ToList();

        if (matches.Count == 0)
            return NotFound(new { error = $"No '{entity.Name}' row with key '{key}'." });

        return Ok(new EntityRecordsResponse(
            entity.Id, entity.Name, entity.TableName,
            result.Columns.ToList(), matches, matches.Count, 1, matches.Count));
    }

    private ActionResult? RequireProject()
    {
        if (_project.IsResolved) return null;
        return BadRequest(new
        {
            error = "No project resolved. Pass X-Project-Id (GUID) or X-Project (name) on the request.",
        });
    }

    private async Task<DataEntityView?> FindEntityByNameAsync(string entityName)
    {
        var list = await _svc.ListDataEntitiesAsync(_project.ProjectId!.Value, HttpContext.RequestAborted);
        return list.FirstOrDefault(e => EntityNameMatches(e, entityName));
    }

    /// <summary>
    /// Matches against display name, table name, or a slug form of either
    /// (spaces/underscores → hyphens, case-insensitive).
    /// </summary>
    public static bool EntityNameMatches(DataEntityView e, string entityName)
    {
        if (string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(e.TableName, entityName, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(ProjectDataReservedNames.Slug(e.Name), entityName, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(ProjectDataReservedNames.Slug(e.TableName), entityName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string Slug(string raw) => ProjectDataReservedNames.Slug(raw);

    private static int ResolveLabelIndex(DataEntityView entity, IReadOnlyList<string> columns)
    {
        if (columns.Count == 0) return -1;
        if (!string.IsNullOrWhiteSpace(entity.LabelColumn))
        {
            for (var i = 0; i < columns.Count; i++)
                if (string.Equals(columns[i], entity.LabelColumn, StringComparison.OrdinalIgnoreCase))
                    return i;
        }
        return 0;
    }
}

public sealed record EntityApiResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string TableName,
    string? LabelColumn,
    string Slug,
    IReadOnlyList<EntityRelationResponse> Relations,
    IReadOnlyList<string> Tags,
    DateTimeOffset UpdatedAt);

public sealed record EntityRelationResponse(string Column, string TargetEntity, string TargetColumn);

public sealed record EntityRecordsResponse(
    Guid EntityId,
    string EntityName,
    string TableName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long Total,
    int Page,
    int PageSize);

public static class EntityApiMapper
{
    public static EntityApiResponse ToResponse(DataEntityView e) => new(
        e.Id, e.ProjectId, e.Name, e.TableName, e.LabelColumn,
        ProjectDataReservedNames.Slug(e.Name),
        e.Relations.Select(r => new EntityRelationResponse(r.Column, r.TargetEntity, r.TargetColumn)).ToList(),
        e.Tags.ToList(), e.UpdatedAt);

    public static EntityRecordsResponse ToRecords(DataEntityView e, ProjectTablePageResult result) => new(
        e.Id, e.Name, e.TableName,
        result.Columns.ToList(),
        result.Rows.Select(r => r.ToList()).ToList(),
        result.TotalCount, result.Page, result.PageSize);
}

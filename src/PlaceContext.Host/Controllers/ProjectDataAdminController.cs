using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/data-admin")]
[Authorize(Policy = Permission.DataRead)]
public sealed class ProjectDataAdminController(IPlaceContextService placeContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProjectDataAdminResponse>> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var mappingsTask = placeContext.ListDataMappingsAsync(projectId, cancellationToken);
        var jobsTask = placeContext.ListJobsAsync(projectId, cancellationToken);
        var tablesTask = placeContext.ListProjectDataTablesAsync(projectId, cancellationToken);
        var entitiesTask = placeContext.ListDataEntitiesAsync(projectId, cancellationToken);
        var linksTask = placeContext.ListRecordLinkGroupsAsync(projectId, cancellationToken);
        await Task.WhenAll(mappingsTask, jobsTask, tablesTask, entitiesTask, linksTask);

        return Ok(new ProjectDataAdminResponse(
            (await mappingsTask).Where(mapping => !string.Equals(mapping.SourceKind, "chain", StringComparison.OrdinalIgnoreCase)).ToArray(),
            (await jobsTask).Select(job => new DataAdminJobResponse(job.Id, job.Name, job.ReturnType.ToString())).ToArray(),
            await tablesTask,
            await entitiesTask,
            await linksTask));
    }

    [HttpPost("mappings")]
    public async Task<ActionResult<DataMappingView>> SaveMapping(
        Guid projectId,
        SaveDataMappingPageRequest request,
        CancellationToken cancellationToken)
    {
        if (request.JobId == Guid.Empty || string.IsNullOrWhiteSpace(request.TargetTable) || request.Fields.Count == 0)
            return BadRequest(new { error = "A source job, target table, and at least one field are required." });

        var result = await placeContext.SaveDataMappingAsync(new SaveDataMappingCommand(
            projectId,
            request.JobId,
            request.TargetTable.Trim(),
            string.IsNullOrWhiteSpace(request.RowsPath) ? null : request.RowsPath.Trim(),
            request.Fields,
            request.Enabled,
            request.Id,
            "job"), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("mappings/{mappingId:guid}")]
    public async Task<IActionResult> DeleteMapping(Guid projectId, Guid mappingId, CancellationToken cancellationToken)
    {
        _ = projectId;
        await placeContext.DeleteDataMappingAsync(mappingId, cancellationToken);
        return NoContent();
    }

    [HttpPost("entities")]
    public async Task<ActionResult<DataEntityView>> SaveEntity(
        Guid projectId,
        SaveDataEntityPageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TableName))
            return BadRequest(new { error = "An entity name and source table are required." });

        var result = await placeContext.SaveDataEntityAsync(new SaveDataEntityCommand(
            projectId,
            request.Name.Trim(),
            request.TableName.Trim(),
            string.IsNullOrWhiteSpace(request.LabelColumn) ? null : request.LabelColumn.Trim(),
            request.Relations,
            request.Tags,
            request.Id), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("entities/{entityId:guid}")]
    public async Task<IActionResult> DeleteEntity(Guid projectId, Guid entityId, CancellationToken cancellationToken)
    {
        _ = projectId;
        await placeContext.DeleteDataEntityAsync(entityId, cancellationToken);
        return NoContent();
    }

    [HttpPost("links/rescan")]
    public async Task<ActionResult<object>> Rescan(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await placeContext.RescanRecordLinksAsync(projectId, cancellationToken);
        return Ok(result);
    }
}

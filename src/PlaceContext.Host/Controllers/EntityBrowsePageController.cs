using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/entity-page/{entityName}")]
[Authorize(Policy = Permission.DataRead)]
public sealed class EntityBrowsePageController(IPlaceContextService placeContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EntityBrowsePageResponse>> Get(
        Guid projectId,
        string entityName,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var entity = await FindEntity(projectId, entityName, cancellationToken);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}'." });

        var rowsTask = placeContext.QueryProjectTablePageAsync(
            projectId,
            entity.TableName,
            search,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 200),
            ct: cancellationToken);
        var columnsTask = placeContext.ListProjectTableColumnsAsync(
            projectId,
            entity.TableName,
            cancellationToken);
        await Task.WhenAll(rowsTask, columnsTask);

        return Ok(new EntityBrowsePageResponse(entity, await columnsTask, await rowsTask));
    }

    [HttpPost("records/create")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<ActionResult<CreateEntityRecordResult>> Create(
        Guid projectId,
        string entityName,
        EntityRecordCreatePageRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindEntity(projectId, entityName, cancellationToken);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}'." });
        return Ok(await placeContext.CreateEntityRecordAsync(
            projectId,
            entity.TableName,
            request.Values,
            cancellationToken));
    }

    [HttpPost("records/update")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<ActionResult<object>> Update(
        Guid projectId,
        string entityName,
        EntityRecordUpdatePageRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindEntity(projectId, entityName, cancellationToken);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}'." });
        var affected = await placeContext.UpdateEntityRecordAsync(
            projectId,
            entity.TableName,
            request.Keys,
            request.Values,
            cancellationToken);
        return Ok(new { affected });
    }

    [HttpPost("records/delete")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<ActionResult<object>> Delete(
        Guid projectId,
        string entityName,
        EntityRecordDeletePageRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindEntity(projectId, entityName, cancellationToken);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}'." });
        var affected = await placeContext.DeleteEntityRecordAsync(
            projectId,
            entity.TableName,
            request.Keys,
            cancellationToken);
        return Ok(new { affected });
    }

    [HttpPost("records/links")]
    public async Task<ActionResult<IReadOnlyList<RecordLink>>> Links(
        Guid projectId,
        string entityName,
        EntityRecordLinksPageRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await FindEntity(projectId, entityName, cancellationToken);
        if (entity is null)
            return NotFound(new { error = $"Unknown entity '{entityName}'." });
        return Ok(await placeContext.RelatedRecordLinksForRowAsync(
            projectId,
            entity.TableName,
            request.Values,
            cancellationToken));
    }

    private async Task<DataEntityView?> FindEntity(
        Guid projectId,
        string name,
        CancellationToken cancellationToken)
    {
        var decoded = Uri.UnescapeDataString(name);
        return (await placeContext.ListDataEntitiesAsync(projectId, cancellationToken))
            .FirstOrDefault(entity =>
                string.Equals(entity.Name, decoded, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.TableName, decoded, StringComparison.OrdinalIgnoreCase));
    }
}

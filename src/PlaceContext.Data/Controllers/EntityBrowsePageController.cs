using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Api;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/entity-page/{entityName}")]
[Authorize(Policy = Permission.DataRead)]
public sealed class EntityBrowsePageController(IDispatcher dispatcher) : ControllerBase
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

        var rowsTask = dispatcher.Query(new QueryProjectTablePageQuery(
            projectId, entity.TableName, search, Math.Max(1, page), Math.Clamp(pageSize, 1, 200)),
            cancellationToken);
        var columnsTask = dispatcher.Query(new ListProjectTableColumnsQuery(
            projectId, entity.TableName), cancellationToken);
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
        return Ok(await dispatcher.Send(new CreateEntityRecordCommand(
            projectId, entity.TableName, request.Values), cancellationToken));
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
        var affected = await dispatcher.Send(new UpdateEntityRecordCommand(
            projectId, entity.TableName, request.Keys, request.Values), cancellationToken);
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
        var affected = await dispatcher.Send(new DeleteEntityRecordCommand(
            projectId, entity.TableName, request.Keys), cancellationToken);
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
        return Ok(await dispatcher.Query(new RelatedRecordLinksForRowQuery(
            projectId, entity.TableName, request.Values), cancellationToken));
    }

    private async Task<DataEntityView?> FindEntity(
        Guid projectId, string name, CancellationToken cancellationToken)
    {
        var decoded = Uri.UnescapeDataString(name);
        return (await dispatcher.Query(new ListDataEntitiesQuery(projectId), cancellationToken))
            .FirstOrDefault(entity =>
                string.Equals(entity.Name, decoded, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.TableName, decoded, StringComparison.OrdinalIgnoreCase));
    }
}

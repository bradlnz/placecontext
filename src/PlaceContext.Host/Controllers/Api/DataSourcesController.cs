using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/sources")]
[Authorize(AuthenticationSchemes = UserApiTokenAuthenticationHandler.SchemeName, Policy = Permission.DataRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DataSourcesController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId)
        => Ok(await dispatcher.Query(new ListDataSourcesQuery(projectId), HttpContext.RequestAborted));

    [HttpPost]
    [Authorize(Policy = Permission.SecretsManage)]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> Create(Guid projectId, CreateDataSourceRequest request)
    {
        try
        {
            return Ok(await dispatcher.Send(new CreateDataSourceCommand(projectId, request.Name, request.ConnectionString,
                request.Schema, request.Table, request.KeyColumn, request.Columns, request.Tags, request.EnableEmbeddings, request.Provider,
                request.Processing is { Enabled: true } processing ? new(processing.Criteria, processing.MinimumConfidence) : null,
                request.Processing?.ProcessingApproved == true,
                request.Queue is { } queue ? new(queue.QueueId, queue.RequiredFields, queue.RedactedFields, queue.TrimText) : null), HttpContext.RequestAborted));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Check the source name, connection options, columns, key and tags." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Source could not be created in this project." });
        }
    }

    [HttpPut("{sourceId:guid}/processing")]
    [Authorize(Policy = Permission.SecretsManage)]
    [Authorize(Policy = Permission.DataWrite)]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> ConfigureProcessing(Guid projectId, Guid sourceId, ConfigureSourceProcessingRequest request)
    {
        try
        {
            return Ok(await dispatcher.Send(new ConfigureSourceProcessingCommand(projectId, sourceId,
                request.Enabled ? new(request.Criteria, request.MinimumConfidence) : null, request.ProcessingApproved), HttpContext.RequestAborted));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Provide valid Jev criteria, threshold and processing approval." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Source unavailable or currently processing. Wait for its run to finish and retry." });
        }
    }

    [HttpPut("{sourceId:guid}/queue")]
    [Authorize(Policy = Permission.SecretsManage)]
    [Authorize(Policy = Permission.DataWrite)]
    [Authorize(Policy = Permission.JobsRun)]
    [Authorize(Policy = Permission.ChainsManage)]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> ConfigureQueue(Guid projectId, Guid sourceId, ConfigureSourceQueueRequest request)
    {
        try
        {
            return Ok(await dispatcher.Send(new ConfigureSourceQueueCommand(projectId, sourceId,
                new(request.QueueId, request.RequiredFields, request.RedactedFields, request.TrimText)), HttpContext.RequestAborted));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Choose an active queue in this project and valid approved fields." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Source unavailable or processing. Wait for the run to finish and retry." });
        }
    }

    [HttpPost("{sourceId:guid}/sync")]
    [Authorize(Policy = Permission.DataWrite)]
    public async Task<IActionResult> Sync(Guid projectId, Guid sourceId, [FromQuery] bool rebuild = false)
    {
        try
        {
            return Ok(await dispatcher.Send(new SyncDataSourceCommand(projectId, sourceId, rebuild), HttpContext.RequestAborted));
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Source sync failed. Check configuration, source limits and embedding availability. Previous data was retained." });
        }
    }

    [HttpGet("{sourceId:guid}/records")]
    public async Task<IActionResult> Records(Guid projectId, Guid sourceId, int offset = 0, int take = 50)
    {
        try
        {
            return Ok(await dispatcher.Query(new ReadSourceRecordsQuery(projectId, sourceId, offset, take), HttpContext.RequestAborted));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Use a non-negative offset and take between 1 and 100." });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("search")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> SearchAll(Guid projectId, SearchDataSourceRequest request)
    {
        try
        {
            return Ok(await dispatcher.Query(new SearchProjectSourcesQuery(projectId, request.Query, request.Take), HttpContext.RequestAborted));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Use a query of 1–1000 characters and take between 1 and 25 per source." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Source retrieval is unavailable. Check configured sources and embedding models." });
        }
    }

    [HttpPost("{sourceId:guid}/search")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Search(Guid projectId, Guid sourceId, SearchDataSourceRequest request)
    {
        try
        {
            return Ok(await dispatcher.Query(new SearchDataSourceQuery(projectId, sourceId, request.Query, request.Take), HttpContext.RequestAborted));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "Use a query of 1–1000 characters and take between 1 and 25." });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { error = "Source retrieval is unavailable. Check source configuration and rebuild after changing embedding models." });
        }
    }

    [HttpDelete("{sourceId:guid}")]
    [Authorize(Policy = Permission.SecretsManage)]
    public async Task<IActionResult> Delete(Guid projectId, Guid sourceId)
    {
        await dispatcher.Send(new DeleteDataSourceCommand(projectId, sourceId), HttpContext.RequestAborted);
        return NoContent();
    }
}

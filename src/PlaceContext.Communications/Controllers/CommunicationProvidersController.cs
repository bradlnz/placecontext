using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Communications.Contracts;

namespace PlaceContext.Communications.Controllers;

[ApiController]
[Route("api/v1/settings/communications")]
[Authorize(Policy = Permission.SettingsManage)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CommunicationProvidersController(
    ICommunicationProviderService providers,
    ICommunicationSender sender,
    ICommunicationDirectoryClient directory) : ControllerBase
{
    [HttpGet("context")]
    public async Task<IActionResult> Context(CancellationToken ct)
    {
        var providersTask = providers.ListAsync(ct);
        var projectsTask = directory.ListProjectsAsync(ct);
        await Task.WhenAll(providersTask, projectsTask);
        return Ok(new CommunicationsSettingsResponse(
            await providersTask,
            await projectsTask));
    }

    [HttpGet("projects/{projectId:guid}/secrets")]
    public async Task<IActionResult> Secrets(Guid projectId, CancellationToken ct)
        => Ok(await directory.ListSecretsAsync(projectId, ct));

    [HttpGet("providers")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await providers.ListAsync(ct));

    [HttpGet("providers/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => await providers.GetAsync(id, ct) is { } provider ? Ok(provider) : NotFound();

    [HttpPost("providers")]
    public async Task<IActionResult> Create(
        CommunicationProviderInput request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await providers.CreateAsync(request, ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("providers/{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        CommunicationProviderInput request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await providers.UpdateAsync(id, request, ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("providers/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await providers.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpPost("providers/{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await providers.SetDefaultAsync(id, ct));
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpPost("providers/{id:guid}/two-factor")]
    public async Task<IActionResult> SetTwoFactor(
        Guid id,
        SetCommunicationTwoFactorRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await providers.SetTwoFactorAsync(id, request.Enabled, ct));
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpPost("providers/{id:guid}/test")]
    public async Task<IActionResult> SendTest(
        Guid id,
        SendCommunicationTestRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await sender.SendTestAsync(id, request.Recipient, ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}

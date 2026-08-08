using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Infrastructure.Comms;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Authorize(Policy = Policies.DefaultAdmin)]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[Route("api/v1/settings/communications")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CommunicationProvidersController : ControllerBase
{
    private readonly CommunicationProviderService _providers;
    private readonly DatabaseCommunicationSender _sender;
    private readonly IPlaceContextService _placeContext;

    public CommunicationProvidersController(
        CommunicationProviderService providers,
        DatabaseCommunicationSender sender,
        IPlaceContextService placeContext)
        => (_providers, _sender, _placeContext) = (providers, sender, placeContext);

    [HttpGet("context")]
    public async Task<ActionResult<CommunicationsSettingsResponse>> Context(CancellationToken ct)
    {
        var providersTask = _providers.ListAsync(ct);
        var projectsTask = _placeContext.GetProjectsAsync(ct);
        await Task.WhenAll(providersTask, projectsTask);
        return Ok(new CommunicationsSettingsResponse(
            await providersTask,
            await projectsTask));
    }

    [HttpGet("projects/{projectId:guid}/secrets")]
    public async Task<IActionResult> Secrets(Guid projectId, CancellationToken ct)
        => Ok(await _placeContext.ListProjectSecretsAsync(projectId, ct));

    [HttpGet("providers")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _providers.ListAsync(ct));

    [HttpGet("providers/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => await _providers.GetAsync(id, ct) is { } provider ? Ok(provider) : NotFound();

    [HttpPost("providers")]
    public async Task<IActionResult> Create(
        [FromBody] CommunicationProviderInput request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _providers.CreateAsync(request, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("providers/{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] CommunicationProviderInput request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _providers.UpdateAsync(id, request, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("providers/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _providers.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("providers/{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await _providers.SetDefaultAsync(id, ct));
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("providers/{id:guid}/two-factor")]
    public async Task<IActionResult> SetTwoFactor(
        Guid id,
        [FromBody] SetTwoFactorRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _providers.SetTwoFactorAsync(id, request.Enabled, ct));
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("providers/{id:guid}/test")]
    public async Task<IActionResult> SendTest(
        Guid id,
        [FromBody] SendTestRequest request,
        CancellationToken ct)
    {
        try
        {
            var delivery = await _sender.SendTestAsync(id, request.Recipient, ct);
            return Ok(new { delivery.Provider, delivery.ExternalId });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

}

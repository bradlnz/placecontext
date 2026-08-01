using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Infrastructure.Comms;

namespace PlaceContext.Host.Controllers;

[Authorize(Policy = Policies.DefaultAdmin)]
[Route("/api/settings/communication-providers")]
public sealed class CommunicationProvidersController : ControllerBase
{
    private readonly CommunicationProviderService _providers;
    private readonly DatabaseCommunicationSender _sender;

    public CommunicationProvidersController(
        CommunicationProviderService providers,
        DatabaseCommunicationSender sender)
        => (_providers, _sender) = (providers, sender);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _providers.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => await _providers.GetAsync(id, ct) is { } provider ? Ok(provider) : NotFound();

    [HttpPost]
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

    [HttpPut("{id:guid}")]
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _providers.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await _providers.SetDefaultAsync(id, ct));
        }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/two-factor")]
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

    [HttpPost("{id:guid}/test")]
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

    public sealed record SetTwoFactorRequest(bool Enabled);
    public sealed record SendTestRequest(string Recipient);
}

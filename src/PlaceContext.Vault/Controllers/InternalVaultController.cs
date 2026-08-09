using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Vault.Controllers;

[ApiController]
[Route("api/vault/internal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalVaultController(
    IProjectSecretRepository secrets,
    ISecretProtector protector) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/secrets/resolve-all")]
    public async Task<IActionResult> ResolveAll(Guid projectId, CancellationToken cancellationToken)
    {
        var ciphers = await secrets.GetCiphersAsync(projectId, cancellationToken);
        return Ok(ciphers.ToDictionary(
            pair => pair.Key,
            pair => protector.Unprotect(pair.Value),
            StringComparer.Ordinal));
    }

    [HttpPost("projects/{projectId:guid}/secrets/resolve")]
    public async Task<IActionResult> Resolve(
        Guid projectId,
        ResolveSecretsRequest request,
        CancellationToken cancellationToken)
    {
        var requested = request.Names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        var ciphers = await secrets.GetCiphersAsync(projectId, cancellationToken);
        var values = ciphers
            .Where(pair => requested.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => protector.Unprotect(pair.Value), StringComparer.Ordinal);
        return Ok(values);
    }

    public sealed record ResolveSecretsRequest(IReadOnlyList<string> Names);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Authorize(Policy = Policies.DefaultAdmin)]
[Route("api/v1/settings/connections")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ConnectionsSettingsController(IPlaceContextService placeContext) : ControllerBase
{
    private static readonly string[] DatabaseKeys =
    [
        ProjectDatabaseEnvironmentVariables.Host,
        ProjectDatabaseEnvironmentVariables.Port,
        ProjectDatabaseEnvironmentVariables.Name,
        ProjectDatabaseEnvironmentVariables.Username,
        ProjectDatabaseEnvironmentVariables.Password,
        ProjectDatabaseEnvironmentVariables.SslMode,
    ];

    private static readonly string[] IndexKeys =
    [
        OpenSearchEnvironmentVariables.Endpoint,
        OpenSearchEnvironmentVariables.Username,
        OpenSearchEnvironmentVariables.Password,
        OpenSearchEnvironmentVariables.Index,
    ];

    private static readonly string[] SslModes =
        ["Disable", "Allow", "Prefer", "Require", "Verify-CA", "Verify-Full"];

    [HttpGet("context")]
    public async Task<ActionResult<ConnectionsSettingsResponse>> Context(CancellationToken ct)
    {
        var projects = await placeContext.GetProjectsAsync(ct);
        var response = new List<ConnectionProjectView>(projects.Count);
        foreach (var project in projects)
            response.Add(await ToViewAsync(project, ct));

        return Ok(new ConnectionsSettingsResponse(response, SslModes));
    }

    [HttpPut("projects/{projectId:guid}/database")]
    public async Task<ActionResult<ConnectionProjectView>> PutDatabase(
        Guid projectId,
        [FromBody] ExternalDatabaseRequest request,
        CancellationToken ct)
    {
        var project = await FindProjectAsync(projectId, ct);
        if (project is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Host))
            return BadRequest(new { error = "Host is required." });
        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { error = "Username is required." });
        if (string.IsNullOrEmpty(request.Password))
            return BadRequest(new { error = "Password is required." });

        var port = string.IsNullOrWhiteSpace(request.Port) ? null : request.Port.Trim();
        if (port is not null && !int.TryParse(port, out _))
            return BadRequest(new { error = "Port must be a number." });
        if (!SslModes.Contains(request.SslMode, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "Invalid SSL mode." });

        var values = new Dictionary<string, string>
        {
            [ProjectDatabaseEnvironmentVariables.Host] = request.Host.Trim(),
            [ProjectDatabaseEnvironmentVariables.Username] = request.Username.Trim(),
            [ProjectDatabaseEnvironmentVariables.Password] = request.Password,
            [ProjectDatabaseEnvironmentVariables.SslMode] = request.SslMode.Trim(),
        };
        if (port is not null)
            values[ProjectDatabaseEnvironmentVariables.Port] = port;
        if (!string.IsNullOrWhiteSpace(request.Database))
            values[ProjectDatabaseEnvironmentVariables.Name] = request.Database.Trim();

        await WriteSecretsAsync(projectId, values, ct);
        return Ok(await ToViewAsync(project, ct));
    }

    [HttpDelete("projects/{projectId:guid}/database")]
    public async Task<ActionResult<ConnectionProjectView>> DeleteDatabase(Guid projectId, CancellationToken ct)
        => await DeleteConnectionAsync(projectId, DatabaseKeys, ct);

    [HttpPut("projects/{projectId:guid}/index")]
    public async Task<ActionResult<ConnectionProjectView>> PutIndex(
        Guid projectId,
        [FromBody] ExternalIndexRequest request,
        CancellationToken ct)
    {
        var project = await FindProjectAsync(projectId, ct);
        if (project is null)
            return NotFound();

        var endpoint = request.Endpoint.Trim();
        if (endpoint.Length == 0)
            return BadRequest(new { error = "Endpoint is required." });
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            return BadRequest(new { error = "Endpoint must be an absolute HTTP or HTTPS URL." });

        var values = new Dictionary<string, string>
        {
            [OpenSearchEnvironmentVariables.Endpoint] = endpoint.TrimEnd('/'),
        };
        if (!string.IsNullOrWhiteSpace(request.Username))
            values[OpenSearchEnvironmentVariables.Username] = request.Username.Trim();
        if (!string.IsNullOrWhiteSpace(request.Password))
            values[OpenSearchEnvironmentVariables.Password] = request.Password;
        if (!string.IsNullOrWhiteSpace(request.Index))
            values[OpenSearchEnvironmentVariables.Index] = request.Index.Trim();

        await WriteSecretsAsync(projectId, values, ct);
        return Ok(await ToViewAsync(project, ct));
    }

    [HttpDelete("projects/{projectId:guid}/index")]
    public async Task<ActionResult<ConnectionProjectView>> DeleteIndex(Guid projectId, CancellationToken ct)
        => await DeleteConnectionAsync(projectId, IndexKeys, ct);

    private async Task<ActionResult<ConnectionProjectView>> DeleteConnectionAsync(
        Guid projectId,
        IReadOnlyList<string> keys,
        CancellationToken ct)
    {
        var project = await FindProjectAsync(projectId, ct);
        if (project is null)
            return NotFound();

        foreach (var key in keys)
            await placeContext.DeleteProjectSecretAsync(projectId, key, ct);

        return Ok(await ToViewAsync(project, ct));
    }

    private async Task WriteSecretsAsync(
        Guid projectId,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct)
    {
        foreach (var (key, value) in values)
        {
            await placeContext.DeleteProjectSecretAsync(projectId, key, ct);
            await placeContext.AddProjectSecretAsync(projectId, key, value, ct);
        }
    }

    private async Task<ProjectSummaryView?> FindProjectAsync(Guid projectId, CancellationToken ct)
        => (await placeContext.GetProjectsAsync(ct)).FirstOrDefault(project => project.Id == projectId);

    private async Task<ConnectionProjectView> ToViewAsync(ProjectSummaryView project, CancellationToken ct)
    {
        var secrets = await placeContext.ListProjectSecretsAsync(project.Id, ct);
        return new ConnectionProjectView(
            project.Id,
            project.Name,
            secrets.Any(secret => secret.Name == ProjectDatabaseEnvironmentVariables.Host),
            secrets.Any(secret => secret.Name == OpenSearchEnvironmentVariables.Endpoint));
    }
}

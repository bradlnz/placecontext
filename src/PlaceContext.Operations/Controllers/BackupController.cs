using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Operations.Backup;
using PlaceContext.Operations.Contracts.Backup;

namespace PlaceContext.Operations.Controllers;

/// <summary>
/// Backup / restore — tenant settings + job definitions as a portable JSON manifest. Restricted to the
/// default admin (like the /settings/backup page it backs): a manifest can recreate/modify jobs and
/// settings for the whole workspace on import.
/// </summary>
[Authorize(Policy = "DefaultAdmin")]
[Route("backup")]
public sealed class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;

    public BackupController(IBackupService backupService)
    {
        _backupService = backupService;
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var manifest = await _backupService.ExportManifestAsync(HttpContext.RequestAborted);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"placecontext-backup-{DateTimeOffset.UtcNow:yyyy-MM-dd}.json";
        return File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    [HttpGet("jobs-code")]
    public async Task<IActionResult> ExportJobsCode()
    {
        var manifest = await _backupService.ExportManifestAsync(HttpContext.RequestAborted);
        var archive = JobsCodeArchiveBuilder.Build(manifest);
        var fileName = $"placecontext-jobs-code-{manifest.ExportedAt:yyyy-MM-dd}.zip";
        return File(archive, "application/zip", fileName);
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import()
    {
        BackupManifest manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                Request.Body, cancellationToken: HttpContext.RequestAborted)
                ?? throw new ArgumentException("Empty or invalid manifest body.");
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"Invalid manifest JSON: {ex.Message}" });
        }

        try
        {
            var result = await _backupService.ImportManifestAsync(
                manifest,
                cancellationToken: HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

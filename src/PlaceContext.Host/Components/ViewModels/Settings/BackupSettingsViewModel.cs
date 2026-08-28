using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class BackupSettingsViewModel(PlaceContextService service, PortalUiState ui)
    : PageViewModel
{
    public const long MaxManifestBytes = 20 * 1024 * 1024;
    public BackupManifest? Pending { get; private set; }
    public string? FileName { get; private set; }
    public bool Confirming { get; set; }
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public ImportResultView? Result { get; private set; }

    public void Load() =>
        ui.Set("Backup", "export/import this workspace's settings and job definitions");

    public async Task SelectFileAsync(InputFileChangeEventArgs args)
    {
        Message = null;
        Result = null;
        Confirming = false;
        Pending = null;
        var file = args.File;
        if (file.Size > MaxManifestBytes)
        {
            Message = "File too large — a manifest should be well under 20 MB.";
            NotifyStateChanged();
            return;
        }
        try
        {
            using var stream = file.OpenReadStream(MaxManifestBytes);
            Pending =
                await JsonSerializer.DeserializeAsync<BackupManifest>(stream)
                ?? throw new InvalidOperationException("Empty or invalid manifest.");
            FileName = file.Name;
        }
        catch (Exception ex)
        {
            Message = $"Couldn't read that file as a backup manifest: {ex.Message}";
        }
        NotifyStateChanged();
    }

    public async Task ImportAsync()
    {
        if (Pending is null)
            return;
        Busy = true;
        Message = null;
        NotifyStateChanged();
        try
        {
            Result = await service.ImportManifestAsync(Pending);
            Message = "Import complete.";
            Confirming = false;
        }
        catch (Exception ex)
        {
            Message = $"Import failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }
}
